using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WebPush;

namespace WeatherOutfit.Reminders;

public sealed class DailyReminder(IConfiguration config, ILogger<DailyReminder> logger)
{
    [Function(nameof(DailyReminder))]
    public async Task Run([TimerTrigger("0 30 7 * * *")] TimerInfo timer)
    {
        var connectionString = config.GetConnectionString("WeatherOutfit") ?? config["ConnectionStrings:WeatherOutfit"];
        var subject = config["Vapid:Subject"]; var publicKey = config["Vapid:PublicKey"]; var privateKey = config["Vapid:PrivateKey"];
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey)) throw new InvalidOperationException("Missing reminder configuration.");
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT DeviceId, Endpoint, P256dh, Auth
            FROM Reminders
            WHERE Enabled = 1
              AND [Time] = @reminderTime
              AND Endpoint IS NOT NULL
              AND P256dh IS NOT NULL
              AND Auth IS NOT NULL
            """, connection);
        command.Parameters.AddWithValue("@reminderTime", new TimeOnly(7, 30));
        await using var reader = await command.ExecuteReaderAsync();
        var client = new WebPushClient();
        var payload = JsonSerializer.Serialize(new { title = "Weather Outfit", body = "今日穿搭建議已準備好，點開查看天氣與搭配。", url = "/WEATHER-OUTFIT/" });
        while (await reader.ReadAsync())
        {
            try { await client.SendNotificationAsync(new PushSubscription(reader.GetString(1), reader.GetString(2), reader.GetString(3)), payload, new VapidDetails(subject, publicKey, privateKey)); }
            catch (WebPushException error) { logger.LogWarning(error, "Push failed for {DeviceId}", reader.GetGuid(0)); }
        }
    }
}
