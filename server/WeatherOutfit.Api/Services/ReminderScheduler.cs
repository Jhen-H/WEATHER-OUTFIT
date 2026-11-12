namespace WeatherOutfit.Api.Services;

public sealed class ReminderScheduler(IServiceScopeFactory scopeFactory, PushSender sender, ILogger<ReminderScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ReminderStore>();
            foreach (var reminder in await store.GetDueReminders(DateTimeOffset.UtcNow))
            {
                try { await sender.SendAsync(reminder, stoppingToken); }
                catch (Exception exception) { logger.LogError(exception, "Unable to send push for device {DeviceId}", reminder.DeviceId); }
            }
        }
    }
}
