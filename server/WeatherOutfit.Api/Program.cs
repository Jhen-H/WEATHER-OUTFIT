using WeatherOutfit.Api.Models;
using WeatherOutfit.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string frontendPolicy = "WeatherOutfitFrontend";
builder.Services.AddCors(options => options.AddPolicy(frontendPolicy, policy => policy
    .WithOrigins("http://127.0.0.1:5173", "http://localhost:5173", "https://jhen-h.github.io")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddSingleton<ReminderStore>();
builder.Services.AddHostedService<ReminderScheduler>();

var app = builder.Build();
app.UseCors(frontendPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Weather Outfit API" }));
app.MapPost("/api/devices", (RegisterDeviceRequest request, ReminderStore store) =>
{
    var device = store.RegisterDevice(request.DeviceName);
    return Results.Created($"/api/devices/{device.Id}", device);
});
app.MapPut("/api/reminders/{deviceId:guid}", (Guid deviceId, UpdateReminderRequest request, ReminderStore store) =>
{
    if (!TimeOnly.TryParse(request.Time, out var time)) return Results.BadRequest(new { message = "提醒時間需使用 HH:mm 格式。" });
    var reminder = store.SaveReminder(deviceId, request.Enabled, time, request.TimeZoneId);
    return reminder is null ? Results.NotFound(new { message = "找不到此裝置，請先註冊裝置。" }) : Results.Ok(reminder);
});
app.MapPost("/api/push-subscriptions", (SavePushSubscriptionRequest request, ReminderStore store) =>
{
    if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out _)) return Results.BadRequest(new { message = "Push endpoint 格式不正確。" });
    var subscription = store.SaveSubscription(request);
    return subscription is null ? Results.NotFound(new { message = "找不到此裝置，請先註冊裝置。" }) : Results.Ok(subscription);
});
app.MapGet("/api/reminders/{deviceId:guid}", (Guid deviceId, ReminderStore store) =>
{
    var reminder = store.GetReminder(deviceId);
    return reminder is null ? Results.NotFound(new { message = "尚未設定提醒。" }) : Results.Ok(reminder);
});
app.MapPost("/api/reminders/{deviceId:guid}/test", (Guid deviceId, ReminderStore store) =>
{
    var reminder = store.GetReminder(deviceId);
    if (reminder is null || reminder.Subscription is null) return Results.BadRequest(new { message = "尚未儲存 Push 訂閱資料。" });
    return Results.Accepted($"/api/reminders/{deviceId}", new { message = "後端已收到測試推播請求；完成 VAPID 設定後，這裡會送出真正通知。" });
});

app.Run();
