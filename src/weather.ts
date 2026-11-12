export type City = {
  id: string;
  name: string;
  latitude: number;
  longitude: number;
};

export const cities: City[] = [
  { id: "keelung", name: "基隆市", latitude: 25.1283, longitude: 121.7419 },
  { id: "taipei", name: "臺北市", latitude: 25.033, longitude: 121.5654 },
  { id: "new-taipei", name: "新北市", latitude: 25.017, longitude: 121.4628 },
  { id: "taoyuan", name: "桃園市", latitude: 24.9937, longitude: 121.301 },
  { id: "hsinchu-city", name: "新竹市", latitude: 24.8138, longitude: 120.9675 },
  { id: "hsinchu-county", name: "新竹縣", latitude: 24.8397, longitude: 121.0177 },
  { id: "miaoli", name: "苗栗縣", latitude: 24.5602, longitude: 120.8214 },
  { id: "taichung", name: "臺中市", latitude: 24.1477, longitude: 120.6736 },
  { id: "changhua", name: "彰化縣", latitude: 24.0756, longitude: 120.544 },
  { id: "nantou", name: "南投縣", latitude: 23.9609, longitude: 120.9719 },
  { id: "yunlin", name: "雲林縣", latitude: 23.7092, longitude: 120.4313 },
  { id: "chiayi-city", name: "嘉義市", latitude: 23.4801, longitude: 120.4491 },
  { id: "chiayi-county", name: "嘉義縣", latitude: 23.4518, longitude: 120.2555 },
  { id: "tainan", name: "臺南市", latitude: 22.9997, longitude: 120.227 },
  { id: "kaohsiung", name: "高雄市", latitude: 22.6273, longitude: 120.3014 },
  { id: "pingtung", name: "屏東縣", latitude: 22.5519, longitude: 120.5488 },
  { id: "yilan", name: "宜蘭縣", latitude: 24.7021, longitude: 121.7378 },
  { id: "hualien", name: "花蓮縣", latitude: 23.9911, longitude: 121.6112 },
  { id: "taitung", name: "臺東縣", latitude: 22.7554, longitude: 121.1501 },
  { id: "penghu", name: "澎湖縣", latitude: 23.5712, longitude: 119.5793 },
  { id: "kinmen", name: "金門縣", latitude: 24.4327, longitude: 118.3171 },
  { id: "lienchiang", name: "連江縣", latitude: 26.1603, longitude: 119.9499 },
];

export function findNearestCity(latitude: number, longitude: number): City {
  return cities.reduce((nearest, city) => {
    const nearestDistance = (nearest.latitude - latitude) ** 2 + (nearest.longitude - longitude) ** 2;
    const cityDistance = (city.latitude - latitude) ** 2 + (city.longitude - longitude) ** 2;
    return cityDistance < nearestDistance ? city : nearest;
  });
}

export type Weather = {
  date: string;
  temperature: number;
  apparentTemperature: number;
  high: number;
  low: number;
  rainChance: number;
  uvIndex: number;
  windSpeed: number;
  weatherCode: number;
};

export function formatForecastDay(date: string, index: number) {
  if (index === 0) return "今天";
  if (index === 1) return "明天";
  return new Intl.DateTimeFormat("zh-TW", { weekday: "short" }).format(new Date(`${date}T12:00:00+08:00`));
}

export function formatShortDate(date: string) {
  return new Intl.DateTimeFormat("zh-TW", { month: "numeric", day: "numeric" }).format(new Date(`${date}T12:00:00+08:00`));
}

export async function getWeatherForecast(city: City): Promise<Weather[]> {
  const params = new URLSearchParams({
    latitude: String(city.latitude),
    longitude: String(city.longitude),
    current: "temperature_2m,apparent_temperature,weather_code,wind_speed_10m",
    daily: "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,uv_index_max,wind_speed_10m_max",
    forecast_days: "7",
    timezone: "Asia/Taipei",
  });
  const response = await fetch(`https://api.open-meteo.com/v1/forecast?${params}`);
  if (!response.ok) throw new Error("無法取得天氣資料");
  const data = await response.json();
  return data.daily.time.map((date: string, index: number): Weather => {
    const high = Math.round(data.daily.temperature_2m_max[index]);
    const low = Math.round(data.daily.temperature_2m_min[index]);
    const isToday = index === 0;
    return {
      date,
      temperature: isToday ? Math.round(data.current.temperature_2m) : Math.round((high + low) / 2),
      apparentTemperature: isToday ? Math.round(data.current.apparent_temperature) : Math.round((high + low) / 2),
      high,
      low,
      rainChance: data.daily.precipitation_probability_max[index] ?? 0,
      uvIndex: Math.round(data.daily.uv_index_max[index] ?? 0),
      windSpeed: Math.round(isToday ? data.current.wind_speed_10m : data.daily.wind_speed_10m_max[index] ?? 0),
      weatherCode: isToday ? data.current.weather_code : data.daily.weather_code[index],
    };
  });
}

export function weatherLabel(code: number) {
  if ([0, 1].includes(code)) return { icon: "☀", label: "晴天" };
  if ([2, 3].includes(code)) return { icon: "☁", label: "多雲" };
  if ([45, 48].includes(code)) return { icon: "〰", label: "有霧" };
  if (code >= 51 && code <= 67) return { icon: "☂", label: "有雨" };
  if (code >= 71 && code <= 77) return { icon: "❄", label: "降雪" };
  if (code >= 80 && code <= 82) return { icon: "☔", label: "陣雨" };
  if (code >= 95) return { icon: "ϟ", label: "雷雨" };
  return { icon: "☁", label: "天氣變化" };
}
