using WeatherOutfit.Api.Models;
using WeatherOutfit.Api.Services;
using WeatherOutfit.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--generate-vapid-keys", StringComparer.OrdinalIgnoreCase))
{
    var keys = VapidHelper.GenerateVapidKeys();
    Console.WriteLine($"PublicKey={keys.PublicKey}");
    Console.WriteLine($"PrivateKey={keys.PrivateKey}");
    return;
}

// 每日提醒固定於台灣時間 07:30，資料庫儲存相同時間以避免排程與資料不一致。
var dailyReminderTime = new TimeOnly(7, 30);

const string frontendPolicy = "WeatherOutfitFrontend";
builder.Services.AddCors(options => options.AddPolicy(frontendPolicy, policy => policy
    .WithOrigins("http://127.0.0.1:5173", "http://localhost:5173", "https://jhen-h.github.io")
    .AllowAnyHeader()
    .AllowAnyMethod()));
var connectionString = builder.Configuration.GetConnectionString("WeatherOutfit");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("找不到 ConnectionStrings:WeatherOutfit。請用 .NET User Secrets 設定 Azure SQL 連線字串。");
builder.Services.AddDbContext<WeatherOutfitDbContext>(options => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped<ReminderStore>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("reminder-write", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});

var app = builder.Build();
app.UseCors(frontendPolicy);
app.UseRateLimiter();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeatherOutfitDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH('dbo.Devices', 'SecretHash') IS NULL
        BEGIN
            ALTER TABLE dbo.Devices ADD SecretHash nvarchar(128) NULL;
        END

        IF COL_LENGTH('dbo.Reminders', 'CityName') IS NULL
        BEGIN
            ALTER TABLE dbo.Reminders ADD CityName nvarchar(50) NOT NULL CONSTRAINT DF_Reminders_CityName DEFAULT N'臺中市';
            ALTER TABLE dbo.Reminders ADD Latitude float NOT NULL CONSTRAINT DF_Reminders_Latitude DEFAULT 24.1477;
            ALTER TABLE dbo.Reminders ADD Longitude float NOT NULL CONSTRAINT DF_Reminders_Longitude DEFAULT 120.6736;
        END
        """);
    // 修正舊版前端可能存入的自訂時間；目前排程固定於 07:30。
    await db.Database.ExecuteSqlRawAsync("""
        UPDATE dbo.Reminders SET [Time] = '07:30' WHERE [Time] <> '07:30';
        """);
    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'dbo.NotificationLogs', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.NotificationLogs
            (
                Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                DeviceId uniqueidentifier NOT NULL,
                ScheduledDate date NOT NULL,
                CreatedAt datetimeoffset NOT NULL,
                SentAt datetimeoffset NULL,
                Status nvarchar(20) NOT NULL,
                FailureReason nvarchar(500) NULL,
                CONSTRAINT UQ_NotificationLogs_Device_ScheduledDate UNIQUE (DeviceId, ScheduledDate)
            );
        END
        """);
    // 加入晚間「明日」提醒後，發送紀錄改以「裝置＋日期＋種類」判斷是否已送過。
    // 分成三個批次執行：SQL Server 編譯同一批次時，還看不到同批次才新增的欄位。
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH('dbo.NotificationLogs', 'Kind') IS NULL
            ALTER TABLE dbo.NotificationLogs ADD Kind nvarchar(20) NOT NULL CONSTRAINT DF_NotificationLogs_Kind DEFAULT N'morning';
        """);
    await db.Database.ExecuteSqlRawAsync("""
        IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_NotificationLogs_Device_ScheduledDate')
            ALTER TABLE dbo.NotificationLogs DROP CONSTRAINT UQ_NotificationLogs_Device_ScheduledDate;
        """);
    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_NotificationLogs_Device_Date_Kind')
            ALTER TABLE dbo.NotificationLogs ADD CONSTRAINT UQ_NotificationLogs_Device_Date_Kind UNIQUE (DeviceId, ScheduledDate, Kind);
        """);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Weather Outfit API" }));
app.MapGet("/api/push-public-key", (IConfiguration configuration) =>
{
    var publicKey = configuration["Vapid:PublicKey"];
    return string.IsNullOrWhiteSpace(publicKey)
        ? Results.Problem("VAPID public key has not been configured.", statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { publicKey });
});
app.MapPost("/api/devices", async (RegisterDeviceRequest request, ReminderStore store) =>
{
    var device = await store.RegisterDevice(request.DeviceName);
    return Results.Created($"/api/devices/{device.Id}", device);
}).RequireRateLimiting("reminder-write");
app.MapPut("/api/reminders/{deviceId:guid}", async (Guid deviceId, UpdateReminderRequest request, ReminderStore store) =>
{
    if (!await store.IsDeviceAuthorized(deviceId, request.DeviceSecret)) return Results.Unauthorized();
    if (!TimeOnly.TryParse(request.Time, out _)) return Results.BadRequest(new { message = "提醒時間需使用 HH:mm 格式。" });
    var reminder = await store.SaveReminder(deviceId, request.Enabled, dailyReminderTime, request.TimeZoneId, request.CityName, request.Latitude, request.Longitude);
    return reminder is null ? Results.NotFound(new { message = "找不到此裝置，請先註冊裝置。" }) : Results.Ok(reminder);
}).RequireRateLimiting("reminder-write");
app.MapPost("/api/push-subscriptions", async (SavePushSubscriptionRequest request, ReminderStore store) =>
{
    if (!await store.IsDeviceAuthorized(request.DeviceId, request.DeviceSecret)) return Results.Unauthorized();
    if (!IsAllowedPushEndpoint(request.Endpoint)) return Results.BadRequest(new { message = "Push endpoint 格式不正確或不受支援。" });
    var subscription = await store.SaveSubscription(request);
    return subscription is null ? Results.NotFound(new { message = "找不到此裝置，請先註冊裝置。" }) : Results.Ok(subscription);
}).RequireRateLimiting("reminder-write");
app.MapGet("/api/reminders/{deviceId:guid}", async (Guid deviceId, string deviceSecret, ReminderStore store) =>
{
    if (!await store.IsDeviceAuthorized(deviceId, deviceSecret)) return Results.Unauthorized();
    var reminder = await store.GetReminder(deviceId);
    return reminder is null ? Results.NotFound(new { message = "尚未設定提醒。" }) : Results.Ok(reminder);
});
app.Run();

static bool IsAllowedPushEndpoint(string endpoint)
{
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
    var host = uri.Host.ToLowerInvariant();
    return host is "fcm.googleapis.com" or "updates.push.services.mozilla.com" or "web.push.apple.com"
        || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
        || host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
}
