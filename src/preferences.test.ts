import { describe, expect, it } from "vitest";
import { DAILY_REMINDER_TIME, defaultPreferences, normalizePreferences } from "./preferences";

describe("讀取舊資料", () => {
  it("舊收藏的 PNG 圖片網址會換成 WebP，並補上 ID", () => {
    const result = normalizePreferences({
      favorites: [{ title: "炎熱亞麻約會搭配", image: "/WEATHER-OUTFIT/outfits/hot-linen-date-v1.png", items: ["亞麻上衣"] }],
    });
    expect(result.favorites[0].image).toBe("/WEATHER-OUTFIT/outfits/hot-linen-date-v1.webp");
    expect(result.favorites[0].id).toBe("/WEATHER-OUTFIT/outfits/hot-linen-date-v1.webp");
  });

  it("提醒時間一律固定為 07:30", () => {
    const result = normalizePreferences({ reminder: { enabled: true, time: "08:15" } });
    expect(result.reminder).toEqual({ enabled: true, time: DAILY_REMINDER_TIME });
  });

  it("舊版的「中性呈現」回到不限，未知的值回到預設", () => {
    const result = normalizePreferences({ presentation: "neutral", defaultOccasion: "party", clothingIds: ["shirt", "unknown"] });
    expect(result.presentation).toBe("any");
    expect(result.defaultOccasion).toBe(defaultPreferences.defaultOccasion);
    expect(result.clothingIds).toEqual(["shirt"]);
  });

  it("色系最多保留 3 種", () => {
    const result = normalizePreferences({ colorIds: ["black-white", "earth", "light", "blue"] });
    expect(result.colorIds).toEqual(["black-white", "earth", "light"]);
  });

  it("格式錯誤的資料回到預設值", () => {
    expect(normalizePreferences(null)).toEqual(defaultPreferences);
    expect(normalizePreferences("broken")).toEqual(defaultPreferences);
  });
});
