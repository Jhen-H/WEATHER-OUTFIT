using System.Collections.Concurrent;
using WeatherOutfit.Api.Models;

namespace WeatherOutfit.Api.Services;

public sealed class ReminderStore
{
    private readonly ConcurrentDictionary<Guid, Device> _devices = new();
    private readonly ConcurrentDictionary<Guid, Reminder> _reminders = new();

    public Device RegisterDevice(string? deviceName)
    {
        var device = new Device(Guid.NewGuid(), string.IsNullOrWhiteSpace(deviceName) ? "未命名裝置" : deviceName.Trim(), DateTimeOffset.UtcNow);
        _devices[device.Id] = device;
        return device;
    }

    public Reminder? SaveReminder(Guid deviceId, bool enabled, TimeOnly time, string timeZoneId)
    {
        if (!_devices.ContainsKey(deviceId)) return null;
        return _reminders.AddOrUpdate(deviceId,
            _ => new Reminder { DeviceId = deviceId, Enabled = enabled, Time = time, TimeZoneId = timeZoneId },
            (_, current) => { current.Enabled = enabled; current.Time = time; current.TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Taipei" : timeZoneId; return current; });
    }

    public Reminder? GetReminder(Guid deviceId) => _reminders.TryGetValue(deviceId, out var reminder) ? reminder : null;

    public Reminder? SaveSubscription(SavePushSubscriptionRequest request)
    {
        if (!_devices.ContainsKey(request.DeviceId)) return null;
        var reminder = _reminders.GetOrAdd(request.DeviceId, id => new Reminder { DeviceId = id, Enabled = false, Time = new TimeOnly(7, 30), TimeZoneId = "Asia/Taipei" });
        reminder.Subscription = new PushSubscription(request.Endpoint, request.P256dh, request.Auth, DateTimeOffset.UtcNow);
        return reminder;
    }

    public IEnumerable<Reminder> GetDueReminders(DateTimeOffset utcNow)
    {
        foreach (var reminder in _reminders.Values.Where(item => item.Enabled && item.Subscription is not null))
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(reminder.TimeZoneId); } catch { zone = TimeZoneInfo.Utc; }
            var local = TimeZoneInfo.ConvertTime(utcNow, zone);
            if (local.Hour != reminder.Time.Hour || local.Minute != reminder.Time.Minute) continue;
            if (reminder.LastScheduledAt?.UtcDateTime.Minute == utcNow.UtcDateTime.Minute) continue;
            reminder.LastScheduledAt = utcNow;
            yield return reminder;
        }
    }
}
