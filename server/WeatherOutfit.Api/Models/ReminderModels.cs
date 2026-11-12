namespace WeatherOutfit.Api.Models;

public sealed record RegisterDeviceRequest(string? DeviceName);
public sealed record UpdateReminderRequest(bool Enabled, string Time, string TimeZoneId = "Asia/Taipei");
public sealed record SavePushSubscriptionRequest(Guid DeviceId, string Endpoint, string P256dh, string Auth);
public sealed record Device(Guid Id, string Name, DateTimeOffset CreatedAt);
public sealed record PushSubscription(string Endpoint, string P256dh, string Auth, DateTimeOffset UpdatedAt);

public sealed class Reminder
{
    public required Guid DeviceId { get; init; }
    public required bool Enabled { get; set; }
    public required TimeOnly Time { get; set; }
    public required string TimeZoneId { get; set; }
    public PushSubscription? Subscription { get; set; }
    public DateTimeOffset? LastScheduledAt { get; set; }
}
