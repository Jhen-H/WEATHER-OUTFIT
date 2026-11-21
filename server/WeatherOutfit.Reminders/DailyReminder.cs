using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using WebPush;

namespace WeatherOutfit.Reminders;

public sealed class DailyReminder(IConfiguration config, ILogger<DailyReminder> logger)
{
    [Function(nameof(DailyReminder))]
    // Azure Timer 預設以 UTC 解讀；23:30 UTC 即台灣時間隔日 07:30。
    // 不依賴 WEBSITE_TIME_ZONE，避免設定遺失時悄悄改成台灣下午 15:30 才執行。
    public async Task Run([TimerTrigger("0 30 23 * * *")] TimerInfo timer)
    {
        var connectionString = config.GetConnectionString("WeatherOutfit") ?? config["ConnectionStrings:WeatherOutfit"];
        var subject = config["Vapid:Subject"]; var publicKey = config["Vapid:PublicKey"]; var privateKey = config["Vapid:PrivateKey"];
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey)) throw new InvalidOperationException("Missing reminder configuration.");

        var reminders = new List<PushTarget>();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT DeviceId, Endpoint, P256dh, Auth, CityName, Latitude, Longitude
                FROM Reminders
                WHERE Enabled = 1 AND [Time] = @reminderTime
                  AND Endpoint IS NOT NULL AND P256dh IS NOT NULL AND Auth IS NOT NULL
                """, connection);
            command.Parameters.AddWithValue("@reminderTime", new TimeOnly(7, 30));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                reminders.Add(new PushTarget(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetDouble(6)));
        }

        logger.LogInformation("Daily reminder started at {Timestamp} UTC; {ReminderCount} device(s) eligible for 07:30 Taiwan reminders.", DateTimeOffset.UtcNow, reminders.Count);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var client = new WebPushClient();
        var scheduledDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
        foreach (var reminder in reminders)
        {
            // DB 的唯一鍵保證同一台裝置在同一天最多送一次，即使 Function 被重跑也不會重複打擾使用者。
            if (!await TryStartNotificationAsync(connectionString, reminder.DeviceId, scheduledDate))
            {
                logger.LogInformation("Skipping duplicate daily reminder for {DeviceId} on {ScheduledDate}", reminder.DeviceId, scheduledDate);
                continue;
            }

            try
            {
                var body = await BuildNotificationBody(http, reminder);
                var payload = JsonSerializer.Serialize(new { title = "Weather Outfit｜今日穿搭", body, url = "/WEATHER-OUTFIT/" });
                await client.SendNotificationAsync(new PushSubscription(reminder.Endpoint, reminder.P256dh, reminder.Auth), payload, new VapidDetails(subject, publicKey, privateKey));
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, scheduledDate, "Sent", null);
            }
            catch (WebPushException error)
            {
                logger.LogWarning(error, "Push failed for {DeviceId}", reminder.DeviceId);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, scheduledDate, "Failed", error.Message);
                if (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    await ClearInvalidSubscriptionAsync(connectionString, reminder.DeviceId);
            }
            catch (Exception error)
            {
                logger.LogError(error, "Unexpected daily reminder failure for {DeviceId}", reminder.DeviceId);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, scheduledDate, "Failed", error.Message);
            }
        }
    }

    private static async Task<bool> TryStartNotificationAsync(string connectionString, Guid deviceId, DateOnly scheduledDate)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                INSERT INTO NotificationLogs (DeviceId, ScheduledDate, CreatedAt, Status)
                VALUES (@deviceId, @scheduledDate, @createdAt, N'Sending');
                """, connection);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@scheduledDate", scheduledDate.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            return false;
        }
    }

    private static async Task CompleteNotificationAsync(string connectionString, Guid deviceId, DateOnly scheduledDate, string status, string? failureReason)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE NotificationLogs
            SET Status = @status, SentAt = @sentAt, FailureReason = @failureReason
            WHERE DeviceId = @deviceId AND ScheduledDate = @scheduledDate;
            """, connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@sentAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@failureReason", (object?)failureReason?.Substring(0, Math.Min(500, failureReason.Length)) ?? DBNull.Value);
        command.Parameters.AddWithValue("@deviceId", deviceId);
        command.Parameters.AddWithValue("@scheduledDate", scheduledDate.ToDateTime(TimeOnly.MinValue));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearInvalidSubscriptionAsync(string connectionString, Guid deviceId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE Reminders
            SET Endpoint = NULL, P256dh = NULL, Auth = NULL
            WHERE DeviceId = @deviceId;
            """, connection);
        command.Parameters.AddWithValue("@deviceId", deviceId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> BuildNotificationBody(HttpClient http, PushTarget reminder)
    {
        try
        {
            var apiBase = (config["WeatherApiBaseUrl"] ?? "https://weather-outfit-api-jhen2026-fgedbwhahdd4dzdx.japanwest-01.azurewebsites.net").TrimEnd('/');
            var url = $"{apiBase}/api/notification-advice?cityName={Uri.EscapeDataString(reminder.CityName)}&latitude={reminder.Latitude}&longitude={reminder.Longitude}";
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var data = await JsonDocument.ParseAsync(stream);
            return data.RootElement.GetProperty("body").GetString() ?? $"{reminder.CityName}今日穿搭建議已準備好。點開查看完整搭配。";
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Unable to get weather for push notification for {CityName}", reminder.CityName);
            return $"{reminder.CityName}今日穿搭建議已準備好。點開查看完整搭配。";
        }
    }

    private static string OutfitAdvice(double low, double high, int rainChance)
    {
        if (rainChance >= 50) return "建議帶傘、穿防潑水外套與耐濕鞋";
        if (high >= 30 && high - low >= 8) return "白天穿輕薄透氣上衣，早晚帶薄外套";
        if (high >= 30) return "建議短袖與透氣材質，記得防曬";
        if (low <= 18) return "建議長袖或外套保暖";
        if (high - low >= 8) return "日夜溫差大，建議分層並帶薄外套";
        return "建議輕薄上衣搭配長褲或裙裝";
    }

    private static string WeatherLabel(int code) => code switch
    {
        0 or 1 => "晴天", 2 or 3 => "多雲", >= 51 and <= 67 => "有雨", >= 80 and <= 82 => "陣雨", >= 95 => "雷雨", _ => "天氣變化",
    };

    private sealed record PushTarget(Guid DeviceId, string Endpoint, string P256dh, string Auth, string CityName, double Latitude, double Longitude);
}
