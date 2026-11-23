import { describe, expect, it } from "vitest";
import { makeOutfits } from "./recommendation";
import { defaultPreferences, type Preferences } from "./preferences";
import type { Weather } from "./weather";

// 一個溫和、無雨的基準天氣；每個測試只覆寫它要驗證的條件。
const day = (overrides: Partial<Weather> = {}): Weather => ({
  date: "2026-11-23",
  temperature: 23,
  apparentTemperature: 23,
  high: 26,
  low: 20,
  rainChance: 10,
  uvIndex: 4,
  windSpeed: 8,
  weatherCode: 1,
  ...overrides,
});

const prefs = (overrides: Partial<Preferences> = {}): Preferences => ({ ...defaultPreferences, ...overrides });
const ids = (outfits: { id: string }[]) => outfits.map((outfit) => outfit.id);

describe("天氣安全性", () => {
  it("明顯降雨時排除怕雨的造型", () => {
    const outfits = makeOutfits(day({ rainChance: 80 }), prefs());
    expect(outfits.length).toBeGreaterThan(0);
    expect(outfits.every((outfit) => outfit.weatherFit.rain !== "avoid")).toBe(true);
  });

  it("明顯降雨時，雨天專用款排在最前面", () => {
    const [first] = makeOutfits(day({ rainChance: 80 }), prefs());
    expect(first.weatherFit.rain).toBe("good");
  });

  it("沒有明顯降雨時不推薦雨天專用款", () => {
    const outfits = makeOutfits(day({ rainChance: 10 }), prefs());
    expect(outfits.length).toBeGreaterThan(0);
    expect(outfits.every((outfit) => !outfit.weatherFit.preferWhenRaining)).toBe(true);
  });

  it("同時用最高溫與最低溫檢查，炎熱天不推薦冬季外套", () => {
    const hot = day({ high: 34, low: 27, temperature: 31 });
    const outfits = makeOutfits(hot, prefs());
    expect(outfits.length).toBeGreaterThan(0);
    for (const outfit of outfits) {
      expect(hot.low).toBeGreaterThanOrEqual(outfit.weatherFit.minTemperature - 3);
      expect(hot.high).toBeLessThanOrEqual(outfit.weatherFit.maxTemperature + 3);
    }
    expect(ids(outfits)).not.toContain("winter-puffer");
  });

  it("寒冷天不推薦夏季短袖", () => {
    const outfits = makeOutfits(day({ high: 15, low: 9, temperature: 12 }), prefs());
    expect(outfits.length).toBeGreaterThan(0);
    expect(ids(outfits)).not.toContain("hot-sunny-casual");
  });

  it("日夜溫差 8°C 以上時排除怕溫差的造型", () => {
    const outfits = makeOutfits(day({ high: 30, low: 21 }), prefs());
    expect(outfits.length).toBeGreaterThan(0);
    expect(outfits.every((outfit) => outfit.weatherFit.temperatureSwing !== "avoid")).toBe(true);
  });
});

describe("情境一致性", () => {
  it("場合與呈現是硬篩選", () => {
    const outfits = makeOutfits(day(), prefs({ presentation: "men" }), "formal");
    expect(outfits.length).toBeGreaterThan(0);
    for (const outfit of outfits) {
      expect(outfit.occasions).toContain("formal");
      expect(outfit.presentation).toBe("men");
    }
  });
});

describe("個人偏好", () => {
  it("有符合勾選單品的造型時，只保留符合的", () => {
    const base = makeOutfits(day(), prefs(), "casual");
    // 找一個只出現在部分候選裡的單品，才看得出篩選效果。
    const partial = base.flatMap((outfit) => outfit.clothingIds).find((id) => !base.every((outfit) => outfit.clothingIds.includes(id)));
    expect(partial).toBeDefined();

    const outfits = makeOutfits(day(), prefs({ clothingIds: [partial!] }), "casual");
    expect(outfits.length).toBeGreaterThan(0);
    expect(outfits.length).toBeLessThan(base.length);
    expect(outfits.every((outfit) => outfit.clothingIds.includes(partial!))).toBe(true);
  });

  it("勾選的單品完全沒有符合時，降級為原本天氣安全的推薦，而不是空白", () => {
    const base = makeOutfits(day(), prefs(), "casual");
    const outfits = makeOutfits(day(), prefs({ clothingIds: ["not-in-catalog"] }), "casual");
    expect(new Set(ids(outfits))).toEqual(new Set(ids(base)));
  });

  it("符合喜歡色系的造型排在前面", () => {
    const base = makeOutfits(day(), prefs(), "casual");
    const color = base.flatMap((outfit) => outfit.colorIds).find((id) => !base.every((outfit) => outfit.colorIds.includes(id)));
    expect(color).toBeDefined();

    const [first] = makeOutfits(day(), prefs({ colorIds: [color!] }), "casual");
    expect(first.colorIds).toContain(color!);
  });
});
