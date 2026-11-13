using WeatherOutfit.Api.Models;
using WeatherOutfit.Api.Services;
using WeatherOutfit.Api.Data;
using Microsoft.EntityFrameworkCore;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--generate-vapid-keys", StringComparer.OrdinalIgnoreCase))
{
    var keys = VapidHelper.GenerateVapidKeys();
    Console.WriteLine($"PublicKey={keys.PublicKey}");
    Console.WriteLine($"PrivateKey={keys.PrivateKey}");
    return;
}

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
builder.Services.AddHttpClient<PushSender>();

var app = builder.Build();
app.UseCors(frontendPolicy);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeatherOutfitDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH('dbo.Reminders', 'CityName') IS NULL
        BEGIN
            ALTER TABLE dbo.Reminders ADD CityName nvarchar(50) NOT NULL CONSTRAINT DF_Reminders_CityName DEFAULT N'臺中市';
            ALTER TABLE dbo.Reminders ADD Latitude float NOT NULL CONSTRAINT DF_Reminders_Latitude DEFAULT 24.1477;
            ALTER TABLE dbo.Reminders ADD Longitude float NOT NULL CONSTRAINT DF_Reminders_Longitude DEFAULT 120.6736;
        END
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
app.MapGet("/api/notification-advice", async (string cityName, double latitude, double longitude, PushSender sender) =>
    Results.Ok(new { body = await sender.GetAdviceAsync(cityName, latitude, longitude) }));
app.MapPost("/api/devices", async (RegisterDeviceRequest request, ReminderStore store) =>
{
    var device = await store.RegisterDevice(request.DeviceName);
    return Results.Created($"/api/devices/{device.Id}", device);
});
app.MapPut("/api/reminders/{deviceId:guid}", async (Guid deviceId, UpdateReminderRequest request, ReminderStore store) =>
{
    if (!TimeOnly.TryParse(request.Time, out var time)) return Results.BadRequest(new { message = "提醒時間需使用 HH:mm 格式。" });
    var reminder = await store.SaveReminder(deviceId, request.Enabled, time, request.TimeZoneId, request.CityName, request.Latitude, request.Longitude);
    return reminder is null ? Results.NotFound(new { message = "找不到此裝置，請先註冊裝置。" }) : Results.Ok(reminder);
});
app.MapPost("/api/push-subscriptions", async (SavePushSubscriptionRequest request, ReminderStore store) =>
{
    if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out _)) return Results.BadRequest(new { message = "Push endpoint 格式不正確。" });
    var subscription = await store.SaveSubscription(request);
    return subscription is null ? Results.NotFound(new { message = "找不到此裝置，請先註冊裝置。" }) : Results.Ok(subscription);
});
app.MapGet("/api/reminders/{deviceId:guid}", async (Guid deviceId, ReminderStore store) =>
{
    var reminder = await store.GetReminder(deviceId);
    return reminder is null ? Results.NotFound(new { message = "尚未設定提醒。" }) : Results.Ok(reminder);
});
app.MapPost("/api/reminders/{deviceId:guid}/test", async (Guid deviceId, ReminderStore store, PushSender sender) =>
{
    var reminder = await store.GetReminder(deviceId);
    if (reminder is null || reminder.Subscription is null) return Results.BadRequest(new { message = "尚未儲存 Push 訂閱資料。" });
    await sender.SendAsync(reminder);
    return Results.Accepted($"/api/reminders/{deviceId}", new { message = "測試推播已送出。" });
});

app.Run();
