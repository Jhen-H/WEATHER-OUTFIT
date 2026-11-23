import { describe, expect, it } from "vitest";
import { createPreferencesBackup, DAILY_REMINDER_TIME, defaultPreferences, normalizePreferences, restorePreferencesBackup, type Preferences } from "./preferences";

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

describe("備份與換裝置", () => {
  const saved: Preferences = {
    ...defaultPreferences,
    clothingIds: ["shirt"],
    colorIds: ["blue"],
    presentation: "women",
    defaultOccasion: "commute",
    reminder: { enabled: true, time: DAILY_REMINDER_TIME },
    favorites: [{ id: "hot-linen-date", title: "炎熱亞麻約會搭配", image: "/outfits/hot-linen-date-v1.webp", items: ["亞麻上衣"], savedAt: "2026-11-20T00:00:00.000Z" }],
  };

  it("匯出後再匯入，偏好與收藏完整保留", () => {
    const restored = restorePreferencesBackup(createPreferencesBackup(saved), defaultPreferences);
    expect(restored.clothingIds).toEqual(saved.clothingIds);
    expect(restored.colorIds).toEqual(saved.colorIds);
    expect(restored.presentation).toBe("women");
    expect(restored.defaultOccasion).toBe("commute");
    expect(restored.favorites).toEqual(saved.favorites);
  });

  it("每日提醒屬於裝置本身，不會被備份檔覆蓋", () => {
    expect(JSON.parse(createPreferencesBackup(saved)).preferences.reminder).toBeUndefined();
    const current = { ...defaultPreferences, reminder: { enabled: false, time: DAILY_REMINDER_TIME } };
    expect(restorePreferencesBackup(createPreferencesBackup(saved), current).reminder.enabled).toBe(false);
  });

  it("不是備份檔時顯示清楚的錯誤", () => {
    expect(() => restorePreferencesBackup("not json", defaultPreferences)).toThrow("檔案不是有效的備份格式。");
    expect(() => restorePreferencesBackup(JSON.stringify({ hello: "world" }), defaultPreferences)).toThrow("這不是 Weather Outfit 的備份檔。");
  });
});
