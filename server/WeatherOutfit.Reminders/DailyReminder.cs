using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.Json;
using WebPush;

namespace WeatherOutfit.Reminders;

// 兩種提醒：晚上 22:00 預告明天、早上 07:30 提醒今天。
// Kind 會寫進 NotificationLogs，讓「同一台裝置、同一天、同一種提醒」只送一次。
public enum ReminderKind { Morning, Evening }

public sealed class DailyReminder(IConfiguration config, ILogger<DailyReminder> logger)
{
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);
    private const int PushTtlSeconds = 6 * 60 * 60;
    private static readonly TimeSpan StaleSendingAfter = TimeSpan.FromMinutes(10);

    [Function(nameof(DailyReminder))]
    // 23:30 UTC = 台灣 07:30；23:45 UTC 只補發第一次失敗的裝置。
    public async Task Run([TimerTrigger("0 30,45 23 * * *")] TimerInfo timer)
    {
        await SendRemindersAsync(ReminderKind.Morning, timer.IsPastDue ? "timer (past due)" : "timer");
    }

    [Function("EveningReminder")]
    // 14:00 UTC = 台灣 22:00；14:15 UTC 只補發第一次失敗的裝置。
    public async Task RunEvening([TimerTrigger("0 0,15 14 * * *")] TimerInfo timer)
    {
        await SendRemindersAsync(ReminderKind.Evening, timer.IsPastDue ? "timer (past due)" : "timer");
    }

    // 手動觸發：與排程共用同一段發送邏輯，以 Function 金鑰保護。
    // POST /api/RunDailyReminderNow           → 早上的「今日」提醒
    // POST /api/RunDailyReminderNow?kind=evening → 晚上的「明日」提醒
    // 管理端點被隔離時，Portal 無法測試 Timer 函式；HTTP 函式不受影響。
    // 每台裝置每天每種提醒最多成功送出一次，所以重複呼叫不會重複推播。
    [Function("RunDailyReminderNow")]
    public async Task<IActionResult> RunNow([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest request)
    {
        var kind = string.Equals(request.Query["kind"], "evening", StringComparison.OrdinalIgnoreCase) ? ReminderKind.Evening : ReminderKind.Morning;
        var result = await SendRemindersAsync(kind, "manual");
        return new OkObjectResult(result);
    }

    private async Task<ReminderRunResult> SendRemindersAsync(ReminderKind kind, string trigger)
    {
        var connectionString = BuildConnectionString(config.GetConnectionString("WeatherOutfit") ?? config["ConnectionStrings:WeatherOutfit"]);
        var subject = config["Vapid:Subject"];
        var publicKey = config["Vapid:PublicKey"];
        var privateKey = config["Vapid:PrivateKey"];
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            throw new InvalidOperationException("Missing VAPID configuration (Vapid:Subject / Vapid:PublicKey / Vapid:PrivateKey).");

        var taiwanNow = DateTimeOffset.UtcNow.ToOffset(TaiwanOffset);
        var taiwanToday = DateOnly.FromDateTime(taiwanNow.DateTime);
        // ScheduledDate 記錄的是「預報的那一天」：晚上提醒的是明天，早上提醒的是今天。
        var forecastDate = kind == ReminderKind.Evening ? taiwanToday.AddDays(1) : taiwanToday;
        var kindName = KindName(kind);
        logger.LogInformation("{Kind} reminder ({Trigger}) run at {TaiwanTime:yyyy-MM-dd HH:mm} Taiwan time for {ForecastDate}.", kindName, trigger, taiwanNow, forecastDate);

        var reminders = await LoadRemindersAsync(connectionString);
        logger.LogInformation("{ReminderCount} enabled device(s) with a push subscription.", reminders.Count);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var client = new WebPushClient();
        var vapid = new VapidDetails(subject, publicKey, privateKey);
        var adviceByCity = new Dictionary<string, string>();
        var failures = new List<string>();
        int sent = 0, skipped = 0, failed = 0;

        foreach (var reminder in reminders)
        {
            if (!await TryClaimNotificationAsync(connectionString, reminder.DeviceId, forecastDate, kindName))
            {
                skipped++;
                continue;
            }

            try
            {
                if (!adviceByCity.TryGetValue(reminder.CityName, out var body))
                {
                    body = await BuildNotificationBody(http, reminder, kind);
                    adviceByCity[reminder.CityName] = body;
                }

                var payload = JsonSerializer.Serialize(kind == ReminderKind.Evening
                    // 點開晚上的通知時，App 直接切到「明天」分頁。
                    ? new { title = "Weather Outfit｜明日穿搭", body, url = "/WEATHER-OUTFIT/?day=tomorrow" }
                    : new { title = "Weather Outfit｜今日穿搭", body, url = "/WEATHER-OUTFIT/" });
                var options = new Dictionary<string, object>
                {
                    ["vapidDetails"] = vapid,
                    ["TTL"] = PushTtlSeconds,
                    ["headers"] = new Dictionary<string, object> { ["Urgency"] = "high" },
                };
                await client.SendNotificationAsync(new PushSubscription(reminder.Endpoint, reminder.P256dh, reminder.Auth), payload, options);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, forecastDate, kindName, "Sent", null);
                sent++;
            }
            catch (WebPushException error)
            {
                failed++;
                var reason = $"{(int)error.StatusCode} {error.Message}";
                failures.Add($"{ShortId(reminder.DeviceId)}: {reason}");
                logger.LogWarning(error, "Push service returned {StatusCode} for {DeviceId}", (int)error.StatusCode, reminder.DeviceId);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, forecastDate, kindName, "Failed", reason);
                if (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    await ClearInvalidSubscriptionAsync(connectionString, reminder.DeviceId);
            }
            catch (Exception error)
            {
                failed++;
                failures.Add($"{ShortId(reminder.DeviceId)}: {error.Message}");
                logger.LogError(error, "Unexpected {Kind} reminder failure for {DeviceId}", kindName, reminder.DeviceId);
                await CompleteNotificationAsync(connectionString, reminder.DeviceId, forecastDate, kindName, "Failed", error.Message);
            }
        }

        logger.LogInformation("{Kind} reminder ({Trigger}) finished: {Sent} sent, {Skipped} already handled, {Failed} failed.", kindName, trigger, sent, skipped, failed);
        return new ReminderRunResult(kindName, forecastDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), trigger, reminders.Count, sent, skipped, failed, failures);
    }

    private static string KindName(ReminderKind kind) => kind == ReminderKind.Evening ? "evening" : "morning";

    // 回應只顯示裝置 ID 前 8 碼，足以對照資料庫，又不必回傳完整識別碼。
    private static string ShortId(Guid deviceId) => deviceId.ToString()[..8];

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

    // 回傳 true 代表這次執行取得發送權：
    // 1. 這台裝置、這一天、這種提醒第一次：新增一筆 Sending。
    // 2. 上次 Failed，或 Sending 卡住超過 10 分鐘：改回 Sending 後重送（補發）。
    // 3. 已經 Sent，或另一個執行正在送：跳過。
    private async Task<bool> TryClaimNotificationAsync(string connectionString, Guid deviceId, DateOnly scheduledDate, string kind)
    {
        await using var connection = await OpenWithRetryAsync(connectionString);
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var insert = new SqlCommand("""
                INSERT INTO NotificationLogs (DeviceId, ScheduledDate, Kind, CreatedAt, Status)
                VALUES (@deviceId, @scheduledDate, @kind, @now, N'Sending');
                """, connection);
            AddKey(insert, deviceId, scheduledDate, kind);
            insert.Parameters.AddWithValue("@now", now);
            await insert.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            await using var reclaim = new SqlCommand("""
                UPDATE NotificationLogs
                SET Status = N'Sending', CreatedAt = @now, SentAt = NULL, FailureReason = NULL
                WHERE DeviceId = @deviceId AND ScheduledDate = @scheduledDate AND Kind = @kind
                  AND (Status = N'Failed' OR (Status = N'Sending' AND CreatedAt < @staleBefore));
                """, connection);
            AddKey(reclaim, deviceId, scheduledDate, kind);
            reclaim.Parameters.AddWithValue("@now", now);
            reclaim.Parameters.AddWithValue("@staleBefore", now - StaleSendingAfter);
            return await reclaim.ExecuteNonQueryAsync() > 0;
        }
    }

    private async Task CompleteNotificationAsync(string connectionString, Guid deviceId, DateOnly scheduledDate, string kind, string status, string? failureReason)
    {
        await using var connection = await OpenWithRetryAsync(connectionString);
        await using var command = new SqlCommand("""
            UPDATE NotificationLogs
            SET Status = @status, SentAt = @sentAt, FailureReason = @failureReason
            WHERE DeviceId = @deviceId AND ScheduledDate = @scheduledDate AND Kind = @kind;
            """, connection);
        AddKey(command, deviceId, scheduledDate, kind);
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

    private static void AddKey(SqlCommand command, Guid deviceId, DateOnly scheduledDate, string kind)
    {
        command.Parameters.AddWithValue("@deviceId", deviceId);
        command.Parameters.AddWithValue("@scheduledDate", scheduledDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@kind", kind);
    }

    private async Task<string> BuildNotificationBody(HttpClient http, PushTarget reminder, ReminderKind kind)
    {
        var tomorrow = kind == ReminderKind.Evening;
        var fallback = tomorrow
            ? $"{reminder.CityName}明天的天氣與穿搭建議已準備好，點開查看完整搭配。"
            : $"{reminder.CityName}今日穿搭建議已準備好，點開查看完整搭配。";

        // 直接查 Open-Meteo；網路短暫不穩時，間隔 5、10 秒再試，三次都失敗才改送備用文字。
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            await Task.Delay(delays[attempt]);
            try
            {
                return await WeatherAdvice.BuildAsync(http, reminder.CityName, reminder.Latitude, reminder.Longitude, tomorrow);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Weather lookup attempt {Attempt} failed for {CityName}.", attempt + 1, reminder.CityName);
            }
        }
        logger.LogError("Weather lookup failed {Attempts} times for {CityName}; sending fallback text.", delays.Length, reminder.CityName);
        return fallback;
    }

    public sealed record ReminderRunResult(string Kind, string ForecastDate, string Trigger, int Eligible, int Sent, int SkippedAlreadyHandled, int Failed, List<string> Failures);

    private sealed record PushTarget(Guid DeviceId, string Endpoint, string P256dh, string Auth, string CityName, double Latitude, double Longitude);
}
