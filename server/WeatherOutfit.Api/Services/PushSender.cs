using System.Globalization;
using System.Text.Json;

namespace WeatherOutfit.Api.Services;

public sealed class PushSender(ILogger<PushSender> logger, HttpClient http)
{
    public async Task<string> GetAdviceAsync(string cityName, double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            // 明確指定台灣「今天」的日期，而非依 API 預設時區或陣列索引猜測，避免通知誤用明天的預報。
            var taiwanToday = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var lat = latitude.ToString(CultureInfo.InvariantCulture);
            var lon = longitude.ToString(CultureInfo.InvariantCulture);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&current=weather_code&timezone=Asia%2FTaipei&start_date={taiwanToday}&end_date={taiwanToday}";
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var daily = data.RootElement.GetProperty("daily");
            var high = Math.Round(daily.GetProperty("temperature_2m_max")[0].GetDouble());
            var low = Math.Round(daily.GetProperty("temperature_2m_min")[0].GetDouble());
            var rainChance = daily.GetProperty("precipitation_probability_max")[0].GetInt32();
            var weatherCode = daily.GetProperty("weather_code")[0].GetInt32();
            var forecastDate = daily.GetProperty("time")[0].GetString() ?? taiwanToday;
            var currentCode = data.RootElement.TryGetProperty("current", out var current)
                && current.TryGetProperty("weather_code", out var currentWeather)
                ? currentWeather.GetInt32()
                : weatherCode;
            var weatherSummary = rainChance >= 50
                ? $"今天預報可能有雨（降雨 {rainChance}%）"
                : $"今天預報{WeatherLabel(weatherCode)}（降雨 {rainChance}%）";
            var currentSummary = currentCode != weatherCode
                ? $"目前{WeatherLabel(currentCode)}；"
                : string.Empty;
            return $"{cityName}{currentSummary}{weatherSummary}，{low:0}–{high:0}°C（{forecastDate}）。{OutfitAdvice(low, high, rainChance)}。點開查看完整搭配。";
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Unable to get weather for push notification for {CityName}", cityName);
            return $"{cityName}今日穿搭建議已準備好。點開查看完整搭配。";
        }
    }

    // 依序判斷，先符合的規則優先。溫差要排在「保暖」之前：
    // 例如 16–27°C 的日子，白天其實偏暖，比起「保暖」更需要「分層、帶薄外套」。
    private static string OutfitAdvice(double low, double high, int rainChance)
    {
        if (rainChance >= 50) return "建議帶傘、穿防潑水外套與耐濕鞋";
        if (high >= 30 && high - low >= 8) return "白天穿輕薄透氣上衣，早晚帶薄外套";
        if (high >= 30) return "建議短袖與透氣材質，記得防曬";
        if (high - low >= 8) return "日夜溫差大，建議分層並帶薄外套";
        if (high <= 20) return "建議長袖或外套保暖";
        if (low <= 18) return "早晚偏涼，建議帶件薄外套";
        return "建議輕薄上衣搭配長褲或裙裝";
    }

    private static string WeatherLabel(int code) => code switch
    {
        0 or 1 => "晴天", 2 or 3 => "多雲", >= 51 and <= 67 => "有雨", >= 80 and <= 82 => "陣雨", >= 95 => "雷雨", _ => "天氣變化",
    };
}
