using Microsoft.EntityFrameworkCore;
using WeatherOutfit.Api.Data;
using WeatherOutfit.Api.Models;

namespace WeatherOutfit.Api.Services;

public sealed class ReminderStore(WeatherOutfitDbContext db)
{
    public async Task<Device> RegisterDevice(string? name)
    {
        var device = new Device(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? "未命名裝置" : name.Trim(), DateTimeOffset.UtcNow);
        db.Devices.Add(new DeviceEntity { Id = device.Id, Name = device.Name, CreatedAt = device.CreatedAt });
        await db.SaveChangesAsync();
        return device;
    }

    public async Task<Reminder?> SaveReminder(Guid deviceId, bool enabled, TimeOnly time, string timeZoneId, string cityName, double latitude, double longitude)
    {
        if (!await db.Devices.AnyAsync(device => device.Id == deviceId)) return null;
        var item = await db.Reminders.FindAsync(deviceId) ?? new ReminderEntity { DeviceId = deviceId };
        item.Enabled = enabled; item.Time = time; item.TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Taipei" : timeZoneId;
        item.CityName = string.IsNullOrWhiteSpace(cityName) ? "臺中市" : cityName.Trim();
        item.Latitude = latitude; item.Longitude = longitude;
        if (db.Entry(item).State == EntityState.Detached) db.Reminders.Add(item);
        await db.SaveChangesAsync();
        return ToModel(item);
    }

    public async Task<Reminder?> GetReminder(Guid deviceId)
    {
        var item = await db.Reminders.AsNoTracking().SingleOrDefaultAsync(reminder => reminder.DeviceId == deviceId);
        return item is null ? null : ToModel(item);
    }

    public async Task<Reminder?> SaveSubscription(SavePushSubscriptionRequest request)
    {
        if (!await db.Devices.AnyAsync(device => device.Id == request.DeviceId)) return null;
        var item = await db.Reminders.FindAsync(request.DeviceId) ?? new ReminderEntity { DeviceId = request.DeviceId, Time = new TimeOnly(7, 30) };
        item.Endpoint = request.Endpoint; item.P256dh = request.P256dh; item.Auth = request.Auth; item.SubscriptionUpdatedAt = DateTimeOffset.UtcNow;
        if (db.Entry(item).State == EntityState.Detached) db.Reminders.Add(item);
        await db.SaveChangesAsync();
        return ToModel(item);
    }

    public async Task<IReadOnlyList<Reminder>> GetDueReminders(DateTimeOffset utcNow)
    {
        var items = await db.Reminders.Where(item => item.Enabled && item.Endpoint != null && item.P256dh != null && item.Auth != null).ToListAsync();
        var due = new List<Reminder>();
        foreach (var item in items)
        {
            try
            {
                var local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId));
                if (local.Hour != item.Time.Hour || local.Minute != item.Time.Minute || item.LastScheduledAt?.UtcDateTime.Minute == utcNow.UtcDateTime.Minute) continue;
                item.LastScheduledAt = utcNow;
                due.Add(ToModel(item));
            }
            catch (TimeZoneNotFoundException) { }
        }
        if (due.Count > 0) await db.SaveChangesAsync();
        return due;
    }

    private static Reminder ToModel(ReminderEntity item) => new()
    {
        DeviceId = item.DeviceId, Enabled = item.Enabled, Time = item.Time, TimeZoneId = item.TimeZoneId, CityName = item.CityName, Latitude = item.Latitude, Longitude = item.Longitude, LastScheduledAt = item.LastScheduledAt,
        Subscription = item.Endpoint is null || item.P256dh is null || item.Auth is null ? null : new PushSubscription(item.Endpoint, item.P256dh, item.Auth, item.SubscriptionUpdatedAt ?? DateTimeOffset.UtcNow)
    };
}
