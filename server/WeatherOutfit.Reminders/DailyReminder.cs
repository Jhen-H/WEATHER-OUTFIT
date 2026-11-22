using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.Json;
using WebPush;

namespace WeatherOutfit.Reminders;

public sealed class DailyReminder(IConfiguration config, ILogger<DailyReminder> logger)
{
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);
    private const int PushTtlSeconds = 6 * 60 * 60;
    private static readonly TimeSpan StaleSendingAfter = TimeSpan.FromMinutes(10);

    [Function(nameof(DailyReminder))]
    // 23:30 UTC = 台灣 07:30；23:45 UTC 只補發第一次失敗的裝置。
    public async Task Run([TimerTrigger("0 30,45 23 * * *")] TimerInfo timer)
    {
        var connectionString = BuildConnectionString(config.GetConnectionString("WeatherOutfit") ?? config["ConnectionStrings:WeatherOutfit"]);
        var subject = config["Vapid:Subject"];
        var publicKey = config["Vapid:PublicKey"];
        var privateKey = config["Vapid:PrivateKey"];
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            throw new InvalidOperationException("Missing VAPID configuration (Vapid:Subject / Vapid:PublicKey / Vapid:PrivateKey).");

        var taiwanNow = DateTimeOffset.UtcNow.ToOffset(TaiwanOffset);
        var scheduledDate = DateOnly.FromDateTime(taiwanNow.DateTime);
        logger.LogInformation("Daily reminder run at {TaiwanTime:yyyy-MM-dd HH:mm} Taiwan time (past due: {IsPastDue}).", taiwanNow, timer.IsPastDue);

        var reminders = await LoadRemindersAsync(connectionString);
        logger.LogInformation("{ReminderCount} enabled device(s) with a push subscription.", reminders.Count);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var client = new WebPushClient();
        var vapid = new VapidDetails(subject, publicKey, privateKey);
        var adviceByCity = new Dictionary<string, string>();
        int sent = 0, skipped = 0, failed = 0;

        foreach (var reminder in reminders)
        {
            if (!await TryClaimNotificationAsync(connectionString, reminder.DeviceId, scheduledDate))
            {
                skipped++;
                continue;
            }

            try
            {
                if (!adviceByCity.TryGetValue(reminder.CityName, out var body))
                {
                    body = await BuildNotificationBody(http, reminder);
                    adviceByCity[reminder.CityName] = body;
                }

                var payload = JsonSerializer.Serialize(new { title = "Weather Outfit｜今日穿搭", body, url = "/WEATHER-OUTFIT/" });
                var options = new Dictionary<string, object>
                {
                    ["vapidDetails"] = vapid,
                    ["TTL"] = PushTtlSeconds,
                    ["headers"] = new Dictionary<string, object> { ["Urgency"] = "high" },
                };
                await client.SendNotificationAsync(new PushSubscription(reminder.Endpoint, reminder.P256dh, reminder.Auth), payload, options);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, scheduledDate, "Sent", null);
                sent++;
            }
            catch (WebPushException error)
            {
                failed++;
                logger.LogWarning(error, "Push service returned {StatusCode} for {DeviceId}", (int)error.StatusCode, reminder.DeviceId);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, scheduledDate, "Failed", $"{(int)error.StatusCode} {error.Message}");
                if (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    await ClearInvalidSubscriptionAsync(connectionString, reminder.DeviceId);
            }
            catch (Exception error)
            {
                failed++;
                logger.LogError(error, "Unexpected daily reminder failure for {DeviceId}", reminder.DeviceId);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, scheduledDate, "Failed", error.Message);
            }
        }

        logger.LogInformation("Daily reminder finished: {Sent} sent, {Skipped} already handled today, {Failed} failed.", sent, skipped, failed);
    }

    private static string BuildConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Missing ConnectionStrings:WeatherOutfit.");

        // Azure SQL serverless 喚醒通常需要超過預設 15 秒。
        var current = new SqlConnectionStringBuilder(raw);
        current.ConnectTimeout = Math.Max(current.ConnectTimeout, 60);
        current.ConnectRetryCount = 3;
        current.ConnectRetryInterval = 10;
        return current.ConnectionString;
    }

    private async Task<SqlConnection> OpenWithRetryAsync(string connectionString)
    {
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30) };
        for (var attempt = 0; ; attempt++)
        {
            await Task.Delay(delays[attempt]);
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (SqlException error) when (attempt < delays.Length - 1)
            {
                await connection.DisposeAsync();
                logger.LogWarning(error, "SQL connection attempt {Attempt} failed; retrying.", attempt + 1);
            }
        }
    }

    private async Task<List<PushTarget>> LoadRemindersAsync(string connectionString)
    {
        var reminders = new List<PushTarget>();
        await using var connection = await OpenWithRetryAsync(connectionString);
        await using var command = new SqlCommand("""
            SELECT DeviceId, Endpoint, P256dh, Auth, CityName, Latitude, Longitude
            FROM Reminders
            WHERE Enabled = 1
              AND Endpoint IS NOT NULL AND P256dh IS NOT NULL AND Auth IS NOT NULL
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            reminders.Add(new PushTarget(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetDouble(6)));
        return reminders;
    }

    private async Task<bool> TryClaimNotificationAsync(string connectionString, Guid deviceId, DateOnly scheduledDate)
    {
        await using var connection = await OpenWithRetryAsync(connectionString);
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var insert = new SqlCommand("""
                INSERT INTO NotificationLogs (DeviceId, ScheduledDate, CreatedAt, Status)
                VALUES (@deviceId, @scheduledDate, @now, N'Sending');
                """, connection);
            AddKey(insert, deviceId, scheduledDate);
            insert.Parameters.AddWithValue("@now", now);
            await insert.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            await using var reclaim = new SqlCommand("""
                UPDATE NotificationLogs
                SET Status = N'Sending', CreatedAt = @now, SentAt = NULL, FailureReason = NULL
                WHERE DeviceId = @deviceId AND ScheduledDate = @scheduledDate
                  AND (Status = N'Failed' OR (Status = N'Sending' AND CreatedAt < @staleBefore));
                """, connection);
            AddKey(reclaim, deviceId, scheduledDate);
            reclaim.Parameters.AddWithValue("@now", now);
            reclaim.Parameters.AddWithValue("@staleBefore", now - StaleSendingAfter);
            return await reclaim.ExecuteNonQueryAsync() > 0;
        }
    }

    private async Task CompleteNotificationAsync(string connectionString, Guid deviceId, DateOnly scheduledDate, string status, string? failureReason)
    {
        await using var connection = await OpenWithRetryAsync(connectionString);
        await using var command = new SqlCommand("""
            UPDATE NotificationLogs
            SET Status = @status, SentAt = @sentAt, FailureReason = @failureReason
            WHERE DeviceId = @deviceId AND ScheduledDate = @scheduledDate;
            """, connection);
        AddKey(command, deviceId, scheduledDate);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@sentAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@failureReason", (object?)failureReason?[..Math.Min(500, failureReason.Length)] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ClearInvalidSubscriptionAsync(string connectionString, Guid deviceId)
    {
        await using var connection = await OpenWithRetryAsync(connectionString);
        await using var command = new SqlCommand("""
            UPDATE Reminders SET Endpoint = NULL, P256dh = NULL, Auth = NULL WHERE DeviceId = @deviceId;
            """, connection);
        command.Parameters.AddWithValue("@deviceId", deviceId);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddKey(SqlCommand command, Guid deviceId, DateOnly scheduledDate)
    {
        command.Parameters.AddWithValue("@deviceId", deviceId);
        command.Parameters.AddWithValue("@scheduledDate", scheduledDate.ToDateTime(TimeOnly.MinValue));
    }

    private async Task<string> BuildNotificationBody(HttpClient http, PushTarget reminder)
    {
        var fallback = $"{reminder.CityName}今日穿搭建議已準備好，點開查看完整搭配。";
        var apiBase = (config["WeatherApiBaseUrl"] ?? "https://weather-outfit-api-jhen2026-fgedbwhahdd4dzdx.japanwest-01.azurewebsites.net").TrimEnd('/');
        var url = $"{apiBase}/api/notification-advice?cityName={Uri.EscapeDataString(reminder.CityName)}" +
                  $"&latitude={reminder.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&longitude={reminder.Longitude.ToString(CultureInfo.InvariantCulture)}";

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var data = await JsonDocument.ParseAsync(stream);
                var body = data.RootElement.GetProperty("body").GetString();
                return string.IsNullOrWhiteSpace(body) ? fallback : body;
            }
            catch (Exception error) when (attempt == 1)
            {
                logger.LogWarning(error, "Weather advice request failed for {CityName}; retrying once.", reminder.CityName);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Unable to get weather advice for {CityName}; sending fallback text.", reminder.CityName);
            }
        }
        return fallback;
    }

    private sealed record PushTarget(Guid DeviceId, string Endpoint, string P256dh, string Auth, string CityName, double Latitude, double Longitude);
}
