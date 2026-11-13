using Microsoft.EntityFrameworkCore;

namespace WeatherOutfit.Api.Data;

public sealed class WeatherOutfitDbContext(DbContextOptions<WeatherOutfitDbContext> options) : DbContext(options)
{
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<ReminderEntity> Reminders => Set<ReminderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceEntity>().HasKey(device => device.Id);
        modelBuilder.Entity<DeviceEntity>().Property(device => device.Name).HasMaxLength(100);
        modelBuilder.Entity<DeviceEntity>().HasOne(device => device.Reminder).WithOne(reminder => reminder.Device).HasForeignKey<ReminderEntity>(reminder => reminder.DeviceId);
        modelBuilder.Entity<ReminderEntity>().HasKey(reminder => reminder.DeviceId);
        modelBuilder.Entity<ReminderEntity>().Property(reminder => reminder.TimeZoneId).HasMaxLength(100);
        modelBuilder.Entity<ReminderEntity>().Property(reminder => reminder.CityName).HasMaxLength(50);
        modelBuilder.Entity<ReminderEntity>().Property(reminder => reminder.Endpoint).HasMaxLength(2048);
    }
}

public sealed class DeviceEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "未命名裝置";
    public DateTimeOffset CreatedAt { get; set; }
    public ReminderEntity? Reminder { get; set; }
}

public sealed class ReminderEntity
{
    public Guid DeviceId { get; set; }
    public DeviceEntity Device { get; set; } = null!;
    public bool Enabled { get; set; }
    public TimeOnly Time { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Taipei";
    public string CityName { get; set; } = "臺中市";
    public double Latitude { get; set; } = 24.1477;
    public double Longitude { get; set; } = 120.6736;
    public string? Endpoint { get; set; }
    public string? P256dh { get; set; }
    public string? Auth { get; set; }
    public DateTimeOffset? SubscriptionUpdatedAt { get; set; }
    public DateTimeOffset? LastScheduledAt { get; set; }
}
