import type { Weather } from "./weather";
import type { OccasionId, Preferences } from "./preferences";
import { outfitCatalog, type OutfitRecord } from "./outfitCatalog";

export type Outfit = OutfitRecord;

export function makeOutfits(weather: Weather, preferences?: Preferences, occasion?: OccasionId): Outfit[] {
  const temperatureRange = weather.high - weather.low;
  const rain = weather.rainChance >= 50;
  const userPreferences = preferences;
  // 場合是使用者當下明確選擇，因此先作為篩選條件，不只是小幅加分。
  const occasionMatches = occasion ? outfitCatalog.filter((outfit) => outfit.occasions.includes(occasion)) : outfitCatalog;
  const occasionCandidates = occasionMatches.length > 0 ? occasionMatches : outfitCatalog;
  const presentationMatches = userPreferences?.presentation && userPreferences.presentation !== "any"
    ? occasionCandidates.filter((outfit) => outfit.presentation === userPreferences.presentation)
    : occasionCandidates;
  // 呈現類型與場合會改變圖片本身，因此維持硬篩選；衣物與色系則是「最常穿／喜歡」的偏好，
  // 只能影響排序，不能讓使用者明明選了通勤卻得不到任何通勤建議。
  const presentationCandidates = presentationMatches;
  // 天氣安全性優先於偏好：不把明確的雨天專用／怕雨造型只當作扣分項目。
  const weatherCandidates = presentationCandidates.filter((outfit) => meetsEssentialWeatherConditions(outfit));
  // 偏好採「有符合就優先保留；完全沒有才降級」：既尊重使用者的單品設定，
  // 又不會讓某個場合因素材還不足而整張卡片空白。
  const clothingCandidates = userPreferences?.clothingIds.length
    ? keepMatchesOrFallback(weatherCandidates, (outfit) => outfit.clothingIds.some((id) => userPreferences.clothingIds.includes(id)))
    : weatherCandidates;
  const candidates = userPreferences?.colorIds.length
    ? keepMatchesOrFallback(clothingCandidates, (outfit) => outfit.colorIds.some((id) => userPreferences.colorIds.includes(id)))
    : clothingCandidates;
  return [...candidates].sort((a, b) => score(b) - score(a));

  function keepMatchesOrFallback(source: Outfit[], matches: (outfit: Outfit) => boolean) {
    const preferred = source.filter(matches);
    return preferred.length ? preferred : source;
  }

  function meetsEssentialWeatherConditions(outfit: Outfit) {
    const fit = outfit.weatherFit;
    const dayHasLargeSwing = temperatureRange >= 8;

    // 雨天專用款（防潑水外套、雨傘搭配）在沒有明顯降雨時不顯示；
    // 怕雨材質則在明顯降雨時排除。
    if (!rain && fit.preferWhenRaining) return false;
    if (rain && fit.rain === "avoid") return false;

    // 最低溫檢查保暖需求、最高溫檢查透氣需求；不只用平均溫度，避免溫差大的日子穿錯。
    if (weather.low < fit.minTemperature - 3) return false;
    if (weather.high > fit.maxTemperature + 3) return false;
    if (dayHasLargeSwing && fit.temperatureSwing === "avoid") return false;
    if (weather.uvIndex >= 8 && fit.uv === "avoid") return false;
    return true;
  }

  function score(outfit: Outfit) {
    const fit = outfit.weatherFit;
    // 以全天高低溫判斷，避免只因早上舒適就推薦白天會太熱的衣服。
    const coldGap = Math.max(0, fit.minTemperature - weather.low);
    const heatGap = Math.max(0, weather.high - fit.maxTemperature);
    const temperatureGap = coldGap + heatGap;
    const temperatureScore = temperatureGap === 0 ? 8 : temperatureGap <= 3 ? 2 : -8;
    const rainScore = rain
      ? fit.rain === "good" ? 8 : fit.rain === "okay" ? 2 : -8
      : fit.preferWhenRaining ? -10 : 0;
    const uvScore = weather.uvIndex < 7 ? 0 : fit.uv === "good" ? 3 : fit.uv === "okay" ? 1 : -3;
    const swingScore = temperatureRange < 8 ? 0 : fit.temperatureSwing === "good" ? 3 : fit.temperatureSwing === "okay" ? 1 : -3;
    const clothingScore = userPreferences ? outfit.clothingIds.filter((id) => userPreferences.clothingIds.includes(id)).length * 10 : 0;
    const colorScore = userPreferences ? outfit.colorIds.filter((id) => userPreferences.colorIds.includes(id)).length * 4 : 0;
    const occasionScore = occasion && outfit.occasions.includes(occasion) ? 12 : 0;
    return temperatureScore + rainScore + uvScore + swingScore + clothingScore + colorScore + occasionScore;
  }
}
