namespace WeatherOutfit.Api.Services;

public sealed class ReminderScheduler(ReminderStore store, ILogger<ReminderScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var reminder in store.GetDueReminders(DateTimeOffset.UtcNow))
                logger.LogInformation("Reminder due for device {DeviceId}; Web Push sender is not configured yet.", reminder.DeviceId);
        }
    }
}
