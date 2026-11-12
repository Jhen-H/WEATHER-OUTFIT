export type OccasionId = "commute" | "school" | "casual" | "date" | "outdoor" | "formal";
export type PresentationId = "any" | "women" | "men";

export type Choice = {
  id: string;
  label: string;
  icon: string;
  group?: string;
  swatch?: string;
};

export const clothingOptions: Choice[] = [
  { id: "shirt", label: "襯衫", icon: "👔", group: "上衣" },
  { id: "tshirt", label: "T-shirt", icon: "👕", group: "上衣" },
  { id: "crop-top", label: "短版上衣", icon: "✂️", group: "上衣" },
  { id: "loose-top", label: "寬鬆上衣", icon: "👘", group: "上衣" },
  { id: "long-sleeve", label: "長袖上衣", icon: "🧤", group: "上衣" },
  { id: "sleeveless-top", label: "無袖上衣", icon: "🦺", group: "上衣" },
  { id: "sweatshirt", label: "大學 T", icon: "🧶", group: "上衣" },
  { id: "thin-jacket", label: "薄外套", icon: "🥼", group: "外搭" },
  { id: "knit-cardigan", label: "針織外套", icon: "🧵", group: "外搭" },
  { id: "blazer", label: "西裝外套", icon: "🤵", group: "外搭" },
  { id: "denim-jacket", label: "牛仔外套", icon: "🔷", group: "外搭" },
  { id: "hoodie", label: "連帽外套", icon: "🧢", group: "外搭" },
  { id: "puffer", label: "羽絨外套", icon: "🪶", group: "外搭" },
  { id: "long-coat", label: "長版大衣", icon: "🧥", group: "外搭" },
  { id: "denim-shorts", label: "牛仔短褲", icon: "🔹", group: "下身" },
  { id: "shorts", label: "短褲", icon: "🩳", group: "下身" },
  { id: "five-shorts", label: "五分短褲", icon: "🏄", group: "下身" },
  { id: "denim-pants", label: "牛仔長褲", icon: "👖", group: "下身" },
  { id: "long-pants", label: "長褲", icon: "🚶", group: "下身" },
  { id: "pleated-skirt", label: "百褶裙", icon: "🎀", group: "下身" },
  { id: "long-skirt", label: "長裙", icon: "🥻", group: "下身" },
  { id: "wide-pants", label: "寬褲", icon: "↔️", group: "下身" },
  { id: "dress", label: "洋裝", icon: "👗", group: "連身" },
  { id: "overalls", label: "吊帶褲", icon: "🧑‍🔧", group: "連身" },
  { id: "jumpsuit", label: "連身褲", icon: "🕺", group: "連身" },
];

export const colorOptions: Choice[] = [
  { id: "black-white", label: "黑白灰", icon: "●", swatch: "linear-gradient(135deg,#27272a 0 33%,#8c8c8c 33% 66%,#fff 66%)" },
  { id: "earth", label: "大地色", icon: "●", swatch: "#b7845b" },
  { id: "light", label: "淺色", icon: "●", swatch: "#f6d7cf" },
  { id: "blue", label: "藍色", icon: "●", swatch: "#7ca7cf" },
  { id: "purple", label: "紫色", icon: "●", swatch: "#b59ccf" },
  { id: "pink", label: "粉色", icon: "●", swatch: "#eab0bd" },
  { id: "green", label: "綠色", icon: "●", swatch: "#92ae95" },
  { id: "bright", label: "亮色", icon: "●", swatch: "#efb33f" },
];

export const occasions: { id: OccasionId; label: string; icon: string }[] = [
  { id: "commute", label: "通勤", icon: "💼" },
  { id: "school", label: "上課", icon: "📚" },
  { id: "casual", label: "休閒", icon: "☕" },
  { id: "date", label: "約會", icon: "✦" },
  { id: "outdoor", label: "運動／戶外", icon: "⛰" },
  { id: "formal", label: "正式", icon: "✧" },
];

export const presentationOptions: { id: PresentationId; label: string; icon: string }[] = [
  { id: "any", label: "不限", icon: "✦" },
  { id: "women", label: "女裝呈現", icon: "♁" },
  { id: "men", label: "男裝呈現", icon: "♙" },
];

export type SavedOutfit = { id: string; title: string; image: string; items: string[]; savedAt: string };
export type Preferences = {
  clothingIds: string[];
  colorIds: string[];
  presentation: PresentationId;
  defaultOccasion: OccasionId;
  reminder: { enabled: boolean; time: string };
  favorites: SavedOutfit[];
};

export const defaultPreferences: Preferences = { clothingIds: [], colorIds: [], presentation: "any", defaultOccasion: "casual", reminder: { enabled: false, time: "07:30" }, favorites: [] };
const storageKey = "weather-outfit-preferences";

export function loadPreferences(): Preferences {
  try {
    const value = localStorage.getItem(storageKey);
    if (!value) return defaultPreferences;
    const saved = { ...defaultPreferences, ...JSON.parse(value) } as Preferences;
    // 舊版收藏以圖片當辨識值；補上 ID 後仍能正常顯示與取消收藏。
    return {
      ...saved,
      // 舊版若曾選到「中性呈現」，新版回到不限，避免沒有對應選項時卡住。
      presentation: (saved.presentation as string) === "neutral" ? "any" : saved.presentation,
      reminder: { ...defaultPreferences.reminder, ...saved.reminder },
      favorites: saved.favorites.map((favorite) => ({ ...favorite, id: favorite.id ?? favorite.image })),
    };
  } catch {
    return defaultPreferences;
  }
}

export function savePreferences(preferences: Preferences) {
  localStorage.setItem(storageKey, JSON.stringify(preferences));
}
