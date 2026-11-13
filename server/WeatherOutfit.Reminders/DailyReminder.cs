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

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var client = new WebPushClient();
        foreach (var reminder in reminders)
        {
            var body = await BuildNotificationBody(http, reminder);
            var payload = JsonSerializer.Serialize(new { title = "Weather Outfit｜今日穿搭", body, url = "/WEATHER-OUTFIT/" });
            try { await client.SendNotificationAsync(new PushSubscription(reminder.Endpoint, reminder.P256dh, reminder.Auth), payload, new VapidDetails(subject, publicKey, privateKey)); }
            catch (WebPushException error) { logger.LogWarning(error, "Push failed for {DeviceId}", reminder.DeviceId); }
        }
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
