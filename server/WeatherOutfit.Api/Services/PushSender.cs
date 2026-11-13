using System.Text.Json;
using WebPush;
using WeatherOutfit.Api.Models;

namespace WeatherOutfit.Api.Services;

public sealed class PushSender(IConfiguration configuration, ILogger<PushSender> logger, HttpClient http)
{
    public async Task SendAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        if (reminder.Subscription is null) return;
        var subject = configuration["Vapid:Subject"];
        var publicKey = configuration["Vapid:PublicKey"];
        var privateKey = configuration["Vapid:PrivateKey"];
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            throw new InvalidOperationException("尚未設定 VAPID 金鑰。請在 User Secrets 或雲端環境變數設定 Vapid:Subject、PublicKey、PrivateKey。");

        var payload = JsonSerializer.Serialize(new { title = "Weather Outfit｜今日穿搭", body = await BuildNotificationBody(reminder, cancellationToken), url = "/WEATHER-OUTFIT/" });
        var subscription = new WebPush.PushSubscription(reminder.Subscription.Endpoint, reminder.Subscription.P256dh, reminder.Subscription.Auth);
        await new WebPushClient().SendNotificationAsync(subscription, payload, new VapidDetails(subject, publicKey, privateKey));
        logger.LogInformation("Push sent to device {DeviceId}", reminder.DeviceId);
    }

    private async Task<string> BuildNotificationBody(Reminder reminder, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={reminder.Latitude}&longitude={reminder.Longitude}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=1&timezone=Asia%2FTaipei";
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var daily = data.RootElement.GetProperty("daily");
            var high = Math.Round(daily.GetProperty("temperature_2m_max")[0].GetDouble());
            var low = Math.Round(daily.GetProperty("temperature_2m_min")[0].GetDouble());
            var rainChance = daily.GetProperty("precipitation_probability_max")[0].GetInt32();
            var weatherCode = daily.GetProperty("weather_code")[0].GetInt32();
            return $"{reminder.CityName}今天{WeatherLabel(weatherCode)}，{low:0}–{high:0}°C。{OutfitAdvice(low, high, rainChance)}。點開查看完整搭配。";
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
}
