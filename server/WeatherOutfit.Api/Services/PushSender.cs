using System.Text.Json;
using WebPush;
using WeatherOutfit.Api.Models;

namespace WeatherOutfit.Api.Services;

public sealed class PushSender(IConfiguration configuration, ILogger<PushSender> logger)
{
    public async Task SendAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        if (reminder.Subscription is null) return;
        var subject = configuration["Vapid:Subject"];
        var publicKey = configuration["Vapid:PublicKey"];
        var privateKey = configuration["Vapid:PrivateKey"];
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            throw new InvalidOperationException("尚未設定 VAPID 金鑰。請在 User Secrets 或雲端環境變數設定 Vapid:Subject、PublicKey、PrivateKey。");

        var payload = JsonSerializer.Serialize(new { title = "Weather Outfit", body = "今日穿搭建議已準備好，點開查看天氣與搭配。", url = "/WEATHER-OUTFIT/" });
        var subscription = new WebPush.PushSubscription(reminder.Subscription.Endpoint, reminder.Subscription.P256dh, reminder.Subscription.Auth);
        await new WebPushClient().SendNotificationAsync(subscription, payload, new VapidDetails(subject, publicKey, privateKey));
        logger.LogInformation("Push sent to device {DeviceId}", reminder.DeviceId);
    }
}
