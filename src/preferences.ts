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

// 每日提醒固定在台灣時間 07:30，與 Azure Function 的排程一致。
export const DAILY_REMINDER_TIME = "07:30";

export type SavedOutfit = { id: string; title: string; image: string; items: string[]; savedAt: string };
export type Preferences = {
  clothingIds: string[];
  colorIds: string[];
  presentation: PresentationId;
  defaultOccasion: OccasionId;
  reminder: { enabled: boolean; time: string };
  favorites: SavedOutfit[];
};

export const defaultPreferences: Preferences = { clothingIds: [], colorIds: [], presentation: "any", defaultOccasion: "casual", reminder: { enabled: false, time: DAILY_REMINDER_TIME }, favorites: [] };
const storageKey = "weather-outfit-preferences";

const knownClothingIds = new Set(clothingOptions.map((item) => item.id));
const knownColorIds = new Set(colorOptions.map((item) => item.id));
const knownOccasionIds = new Set<string>(occasions.map((item) => item.id));
const knownPresentationIds = new Set<string>(presentationOptions.map((item) => item.id));

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const stringList = (value: unknown) => Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];

// 圖片已從 PNG 改為 WebP；舊收藏存的是當時的圖片網址，讀取時一併換成新副檔名。
const migrateImage = (image: string) => image.replace(/\.png$/i, ".webp");

function normalizeFavorites(value: unknown): SavedOutfit[] {
  if (!Array.isArray(value)) return [];
  return value.filter(isRecord).flatMap((item) => {
    if (typeof item.title !== "string" || typeof item.image !== "string") return [];
    const image = migrateImage(item.image);
    return [{
      // 舊版收藏以圖片當辨識值；補上 ID 後仍能正常顯示與取消收藏。
      id: typeof item.id === "string" ? item.id : image,
      title: item.title,
      image,
      items: stringList(item.items),
      savedAt: typeof item.savedAt === "string" ? item.savedAt : new Date().toISOString(),
    }];
  });
}

// 把任何來源（localStorage、匯入的備份檔）的資料整理成合法的偏好設定，未知或格式錯誤的欄位回到預設值。
export function normalizePreferences(raw: unknown): Preferences {
  if (!isRecord(raw)) return defaultPreferences;
  const presentation = raw.presentation === "neutral" ? "any" : raw.presentation; // 舊版的「中性呈現」已移除。
  const reminder = isRecord(raw.reminder) ? raw.reminder : {};
  return {
    clothingIds: stringList(raw.clothingIds).filter((id) => knownClothingIds.has(id)),
    colorIds: stringList(raw.colorIds).filter((id) => knownColorIds.has(id)).slice(0, 3),
    presentation: typeof presentation === "string" && knownPresentationIds.has(presentation) ? presentation as PresentationId : defaultPreferences.presentation,
    defaultOccasion: typeof raw.defaultOccasion === "string" && knownOccasionIds.has(raw.defaultOccasion) ? raw.defaultOccasion as OccasionId : defaultPreferences.defaultOccasion,
    // 提醒時間固定為 07:30；舊版留下的其他時間若被保留，每次開 App 都會寫回資料庫。
    reminder: { enabled: reminder.enabled === true, time: DAILY_REMINDER_TIME },
    favorites: normalizeFavorites(raw.favorites),
  };
}

export function loadPreferences(): Preferences {
  try {
    const value = localStorage.getItem(storageKey);
    return value ? normalizePreferences(JSON.parse(value)) : defaultPreferences;
  } catch {
    return defaultPreferences;
  }
}

export function savePreferences(preferences: Preferences) {
  localStorage.setItem(storageKey, JSON.stringify(preferences));
}

// ---- 備份與換裝置 ----
// 偏好與收藏只存在這台瀏覽器；匯出成檔案後，可在另一台裝置匯入。
// 每日提醒綁定的是「這台裝置」的推播訂閱，所以不包含在備份裡，匯入時保留目前裝置的提醒設定。
const backupFormat = "weather-outfit-backup";

export function createPreferencesBackup(preferences: Preferences, exportedAt = new Date()): string {
  const { reminder: _deviceOnly, ...portable } = preferences;
  return JSON.stringify({ format: backupFormat, version: 1, exportedAt: exportedAt.toISOString(), preferences: portable }, null, 2);
}

export function restorePreferencesBackup(text: string, current: Preferences): Preferences {
  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    throw new Error("檔案不是有效的備份格式。");
  }
  if (!isRecord(data) || data.format !== backupFormat || !isRecord(data.preferences))
    throw new Error("這不是 Weather Outfit 的備份檔。");
  return { ...normalizePreferences(data.preferences), reminder: current.reminder };
}
