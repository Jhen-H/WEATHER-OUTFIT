using System.Globalization;
using System.Text.Json;

namespace WeatherOutfit.Api.Services;

public sealed class PushSender(ILogger<PushSender> logger, HttpClient http)
{
    // tomorrow = false：早上 07:30 的「今日」通知；tomorrow = true：晚上 22:00 的「明日」通知。
    public async Task<string> GetAdviceAsync(string cityName, double latitude, double longitude, bool tomorrow = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // 明確指定台灣的日期，而非依 API 預設時區或陣列索引猜測，避免拿錯天的預報。
            var taiwanToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
            var targetDate = (tomorrow ? taiwanToday.AddDays(1) : taiwanToday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var lat = latitude.ToString(CultureInfo.InvariantCulture);
            var lon = longitude.ToString(CultureInfo.InvariantCulture);
            // 明天的通知不需要即時天氣，只查每日預報。
            var current = tomorrow ? "" : "&current=weather_code";
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max{current}&timezone=Asia%2FTaipei&start_date={targetDate}&end_date={targetDate}";
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var daily = data.RootElement.GetProperty("daily");
            var high = Math.Round(daily.GetProperty("temperature_2m_max")[0].GetDouble());
            var low = Math.Round(daily.GetProperty("temperature_2m_min")[0].GetDouble());
            var rainChance = daily.GetProperty("precipitation_probability_max")[0].GetInt32();
            var weatherCode = daily.GetProperty("weather_code")[0].GetInt32();
            var forecastDate = daily.GetProperty("time")[0].GetString() ?? targetDate;
            var advice = OutfitAdvice(low, high, rainChance, tomorrow);

            if (tomorrow)
            {
                var tomorrowSummary = rainChance >= 50
                    ? $"明天預報可能有雨（降雨 {rainChance}%）"
                    : $"明天預報{WeatherLabel(weatherCode)}（降雨 {rainChance}%）";
                return $"{cityName}{tomorrowSummary}，{low:0}–{high:0}°C（{forecastDate}）。{advice}。點開查看明天的完整搭配。";
            }

            var currentCode = data.RootElement.TryGetProperty("current", out var currentWeather)
                && currentWeather.TryGetProperty("weather_code", out var code)
                ? code.GetInt32()
                : weatherCode;
            var weatherSummary = rainChance >= 50
                ? $"今天預報可能有雨（降雨 {rainChance}%）"
                : $"今天預報{WeatherLabel(weatherCode)}（降雨 {rainChance}%）";
            var currentSummary = currentCode != weatherCode
                ? $"目前{WeatherLabel(currentCode)}；"
                : string.Empty;
            return $"{cityName}{currentSummary}{weatherSummary}，{low:0}–{high:0}°C（{forecastDate}）。{advice}。點開查看完整搭配。";
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Unable to get weather for push notification for {CityName}", cityName);
            return tomorrow
                ? $"{cityName}明天的天氣與穿搭建議已準備好。點開查看完整搭配。"
                : $"{cityName}今日穿搭建議已準備好。點開查看完整搭配。";
        }
    }

    // 依序判斷，先符合的規則優先。溫差要排在「保暖」之前：
    // 例如 16–27°C 的日子，白天其實偏暖，比起「保暖」更需要「分層、帶薄外套」。
    // 每條規則各有兩種說法：早上是「今天怎麼穿」，晚上是「明天要準備什麼」。
    private static string OutfitAdvice(double low, double high, int rainChance, bool tomorrow)
    {
        var (today, prepare) = (low, high, rainChance) switch
        {
            _ when rainChance >= 50 => ("建議帶傘、穿防潑水外套與耐濕鞋", "建議準備雨傘、防潑水外套與耐濕鞋"),
            _ when high >= 30 && high - low >= 8 => ("白天穿輕薄透氣上衣，早晚帶薄外套", "建議準備輕薄透氣上衣，早晚再加一件薄外套"),
            _ when high >= 30 => ("建議短袖與透氣材質，記得防曬", "建議準備短袖、透氣材質與防曬用品"),
            _ when high - low >= 8 => ("日夜溫差大，建議分層並帶薄外套", "日夜溫差大，建議準備可分層的衣物與薄外套"),
            _ when high <= 20 => ("建議長袖或外套保暖", "建議準備長袖與保暖外套"),
            _ when low <= 18 => ("早晚偏涼，建議帶件薄外套", "早晚偏涼，建議準備一件薄外套"),
            _ => ("建議輕薄上衣搭配長褲或裙裝", "建議準備輕薄上衣搭配長褲或裙裝"),
        };
        return tomorrow ? prepare : today;
    }

    private static string WeatherLabel(int code) => code switch
    {
        0 or 1 => "晴天", 2 or 3 => "多雲", >= 51 and <= 67 => "有雨", >= 80 and <= 82 => "陣雨", >= 95 => "雷雨", _ => "天氣變化",
    };
}
