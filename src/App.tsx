import { useEffect, useMemo, useState } from "react";
import { clothingOptions, colorOptions, loadPreferences, occasions, presentationOptions, savePreferences, type OccasionId, type Preferences } from "./preferences";
import { makeOutfits, type Outfit } from "./recommendation";
import { cities, createFallbackForecast, findNearestCity, formatForecastDay, formatShortDate, getWeatherForecast, weatherLabel, type Weather } from "./weather";
import { ClothingIcon } from "./ClothingIcon";
import { savePushReminder, sendServerPushTest } from "./pushReminder";

const initialWeather: Weather = { date: new Date().toISOString().slice(0, 10), temperature: 28, apparentTemperature: 30, high: 32, low: 23, rainChance: 60, uvIndex: 7, windSpeed: 9, weatherCode: 1 };
type View = "home" | "preferences" | "favorites" | "why";
type ForecastView = "today" | "tomorrow" | "week";

export default function App() {
  const [cityId, setCityId] = useState("taichung");
  const [currentLocation, setCurrentLocation] = useState<{ latitude: number; longitude: number } | null>(null);
  const [forecast, setForecast] = useState<Weather[]>(() => createFallbackForecast());
  const [forecastView, setForecastView] = useState<ForecastView>("today");
  const [status, setStatus] = useState("正在更新天氣資料…");
  const [locationError, setLocationError] = useState<string | null>(null);
  const [outfitIndex, setOutfitIndex] = useState(0);
  const [showDetails, setShowDetails] = useState(false);
  const [view, setView] = useState<View>("home");
  const [preferences, setPreferences] = useState<Preferences>(() => loadPreferences());
  const [occasion, setOccasion] = useState<OccasionId>(() => loadPreferences().defaultOccasion);
  const city = useMemo(() => {
    if (!currentLocation) return cities.find((item) => item.id === cityId)!;
    const nearest = findNearestCity(currentLocation.latitude, currentLocation.longitude);
    return { id: "current", name: nearest.name, ...currentLocation };
  }, [cityId, currentLocation]);
  const selectedDay = forecastView === "tomorrow" ? 1 : 0;
  const weather = forecast[selectedDay] ?? forecast[0] ?? initialWeather;
  const outfits = useMemo(() => makeOutfits(weather, preferences, occasion), [weather, preferences, occasion]);
  const outfit = outfits[outfitIndex] ?? null;
  const matchesClothingPreference = !preferences.clothingIds.length || Boolean(outfit?.clothingIds.some((id) => preferences.clothingIds.includes(id)));
  const matchesColorPreference = !preferences.colorIds.length || Boolean(outfit?.colorIds.some((id) => preferences.colorIds.includes(id)));
  const isExactPreferenceMatch = matchesClothingPreference && matchesColorPreference;
  const weatherInfo = weatherLabel(weather.weatherCode);
  const range = weather.high - weather.low;

  useEffect(() => { savePreferences(preferences); }, [preferences]);
  // 使用者切換縣市後，若已開啟提醒便同步這台裝置的提醒城市；不再要求重新關開提醒。
  useEffect(() => {
    if (!preferences.reminder.enabled) return;
    void savePushReminder(preferences.reminder, city).catch(() => undefined);
  }, [city.id, city.name, city.latitude, city.longitude, preferences.reminder]);
  useEffect(() => { if (outfitIndex >= outfits.length) setOutfitIndex(0); }, [outfitIndex, outfits.length]);
  useEffect(() => { setOutfitIndex(0); }, [preferences.clothingIds, preferences.colorIds, preferences.presentation, occasion]);
  useEffect(() => {
    let active = true;
    setStatus("正在更新天氣資料…");
    getWeatherForecast(city).then((result) => {
      if (!active) return;
      setForecast(result); setStatus("未來 7 天天氣資料已更新");
    }).catch(() => {
      if (!active) return;
      setForecast(createFallbackForecast());
      setStatus("目前顯示 7 天範例天氣資料");
    });
    return () => { active = false; };
  }, [city]);

  function changeCity(nextCity: string) { setCityId(nextCity); setCurrentLocation(null); setLocationError(null); setForecastView("today"); setOutfitIndex(0); setShowDetails(false); }
  function useCurrentLocation() {
    if (!navigator.geolocation) { setLocationError("你的瀏覽器不支援定位，請手動選擇縣市"); return; }
    setLocationError(null);
    setStatus("正在取得你的位置…");
    navigator.geolocation.getCurrentPosition(
      (position) => { const nearest = findNearestCity(position.coords.latitude, position.coords.longitude); setCurrentLocation({ latitude: position.coords.latitude, longitude: position.coords.longitude }); setStatus(`已取得目前位置，接近 ${nearest.name}`); setForecastView("today"); setOutfitIndex(0); },
      (error) => setLocationError(({ 1: "位置權限被拒絕，請在瀏覽器允許位置權限，或手動選擇縣市", 2: "目前無法取得位置，請確認裝置的位置服務是否開啟", 3: "取得位置逾時；可重新嘗試或手動選擇縣市" }[error.code] ?? "無法取得位置，請手動選擇縣市")),
      { enableHighAccuracy: false, timeout: 30000, maximumAge: 300000 },
    );
  }
  function toggleValue(field: "clothingIds" | "colorIds", id: string) {
    setPreferences((current) => {
      const selected = current[field];
      if (selected.includes(id)) return { ...current, [field]: selected.filter((value) => value !== id) };
      if (field === "colorIds" && selected.length >= 3) return current;
      return { ...current, [field]: [...selected, id] };
    });
  }
  function isFavorite(current: Outfit) { return preferences.favorites.some((item) => item.id === current.id); }
  function toggleFavorite(current: Outfit) {
    setPreferences((value) => isFavorite(current)
      ? { ...value, favorites: value.favorites.filter((item) => item.id !== current.id) }
      : { ...value, favorites: [...value.favorites, { id: current.id, title: current.title, image: current.image, items: current.items, savedAt: new Date().toISOString() }] });
  }
  function removeFavorite(id: string) { setPreferences((value) => ({ ...value, favorites: value.favorites.filter((item) => item.id !== id) })); }
  function resetStyleFilters() { setPreferences((value) => ({ ...value, clothingIds: [], colorIds: [], presentation: "any" })); }

  const weatherMood = weather.weatherCode <= 1 ? "clear" : weather.weatherCode <= 3 ? "cloudy" : "rainy";
  const selectedDayLabel = formatForecastDay(weather.date, selectedDay);
  return <main className={`app-shell weather-${weatherMood}`}><section className="app-card" aria-label="Weather Outfit 今日穿搭">
    {view === "preferences" && <PreferencesView city={city} preferences={preferences} setPreferences={setPreferences} toggleValue={toggleValue} setOccasion={setOccasion} onBack={() => setView("home")} onFavorites={() => setView("favorites")} onWhy={() => setView("why")} />}
    {view === "favorites" && <FavoritesView favorites={preferences.favorites} onBack={() => setView("preferences")} onRemove={removeFavorite} />}
    {view === "why" && <WhyView onBack={() => setView("preferences")} />}
    {view === "home" && <>
      <header className="topbar"><div><p className="eyebrow">WEATHER OUTFIT</p><h1>{forecastView === "week" ? "一週天氣預報" : `${selectedDayLabel}，穿得剛剛好`}</h1></div><div className="topbar-actions"><button className="icon-button" aria-label="我的偏好" onClick={() => setView("preferences")}>⚙</button></div></header>
      <div className="city-row"><div className="city-controls"><label htmlFor="city">城市</label><select id="city" value={city.id} onChange={(event) => changeCity(event.target.value)}>{currentLocation && <option value="current">目前位置</option>}{cities.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}</select><button className="location-button" onClick={useCurrentLocation}>⌖ 使用目前位置</button></div><div className="forecast-mode-tabs" aria-label="選擇預報範圍"><button className={forecastView === "today" ? "active" : ""} onClick={() => { setForecastView("today"); setOutfitIndex(0); setShowDetails(false); }}>今天</button><button className={forecastView === "tomorrow" ? "active" : ""} onClick={() => { setForecastView("tomorrow"); setOutfitIndex(0); setShowDetails(false); }}>明天</button><button className={forecastView === "week" ? "active" : ""} onClick={() => { setForecastView("week"); setShowDetails(false); }}>一週</button></div><div className="city-status">{locationError ? <><span className="sync-status error-status">{locationError}</span><button className="location-button" onClick={useCurrentLocation}>重新嘗試</button></> : <span className="sync-status">{status}</span>}</div></div>
      {forecastView === "week" ? <section className="weekly-weather-card"><p className="eyebrow">NEXT 7 DAYS</p>{forecast.slice(0, 7).map((day, index) => <article key={day.date}><div><b>{formatForecastDay(day.date, index)}</b><span>{formatShortDate(day.date)}</span></div><i aria-hidden="true">{weatherLabel(day.weatherCode).icon}</i><p>{weatherLabel(day.weatherCode).label}</p><strong>{day.high}° <small>/ {day.low}°</small></strong><em>降雨 {day.rainChance}%</em></article>)}</section> : <><section className="weather-card"><span className="weather-icon" aria-hidden="true">{weatherInfo.icon}</span><div><h2>{weatherInfo.label} <strong>{weather.temperature}°</strong></h2><p>{selectedDay === 0 ? `體感 ${weather.apparentTemperature}° · 風速 ${weather.windSpeed} km/h` : `預估日均 ${weather.temperature}° · 最大風速 ${weather.windSpeed} km/h`}</p></div><dl className="weather-stats"><div><dt>高 / 低</dt><dd>{weather.high}° / {weather.low}°</dd></div><div><dt>降雨</dt><dd>{weather.rainChance}%</dd></div><div><dt>UV</dt><dd>{weather.uvIndex}</dd></div></dl></section><section className="occasion-panel"><p className="eyebrow">{selectedDayLabel.toUpperCase()} PLAN</p><div className="occasion-chips">{occasions.map((item) => <button key={item.id} className={occasion === item.id ? "occasion-chip active" : "occasion-chip"} onClick={() => { setOccasion(item.id); setOutfitIndex(0); }}>{item.icon} {item.label}</button>)}</div></section>{outfit ? <section className="outfit-card"><div className="section-heading"><p className="eyebrow">{selectedDayLabel.toUpperCase()} OUTFIT</p><h2>{outfit.title}</h2><p className="outfit-items">{outfit.items.join(" · ")}</p></div><img src={outfit.image} alt={`${outfit.title} 的完整穿搭示意`} /><div className="reason-panel"><p><b>為什麼這樣穿？</b>{outfit.reason}</p><p className="tip">☂ {outfit.extra}</p>{range >= 8 && <p className="tip">↕ 日夜溫差 {range}°，分層穿搭更方便。</p>}<p className="match-note">{isExactPreferenceMatch ? `已依你的呈現偏好、衣物、色系與「${occasions.find((item) => item.id === occasion)?.label}」篩選推薦。` : `已優先符合「${occasions.find((item) => item.id === occasion)?.label}」與天氣；目前素材沒有完全符合你的${!matchesClothingPreference ? "衣物" : "色系"}偏好，因此顯示最接近的建議。`}</p></div><div className="actions"><button className="secondary" onClick={() => setOutfitIndex((outfitIndex + 1) % outfits.length)}>換一套</button><button className="secondary" onClick={() => setShowDetails(!showDetails)}>{showDetails ? "收起詳情" : "查看詳情"}</button><button className={isFavorite(outfit) ? "saved" : "primary"} onClick={() => toggleFavorite(outfit)}>{isFavorite(outfit) ? "已收藏 ♥" : "加入收藏 ♡"}</button></div>{showDetails && <div className="detail-box"><h3>穿搭清單</h3><ul>{outfit.items.map((item) => <li key={item}>{item}</li>)}</ul><p>推薦會先考慮天氣安全性，再顯示符合你勾選條件的造型。</p></div>}</section> : <section className="empty-card"><span>⌕</span><h2>目前沒有符合條件的搭配</h2><p>沒有隨機推薦，因為目前條件沒有對應照片。</p><div className="empty-actions"><button onClick={resetStyleFilters}>清除衣物、色系與呈現偏好</button><button onClick={() => setView("preferences")}>回到偏好調整</button></div></section>}</>}
    </>}
  </section></main>;
}


function PreferencesView({ city, preferences, setPreferences, toggleValue, setOccasion, onBack, onFavorites, onWhy }: { city: { name: string; latitude: number; longitude: number }; preferences: Preferences; setPreferences: React.Dispatch<React.SetStateAction<Preferences>>; toggleValue: (field: "clothingIds" | "colorIds", id: string) => void; setOccasion: (value: OccasionId) => void; onBack: () => void; onFavorites: () => void; onWhy: () => void }) {
  const groups = [...new Set(clothingOptions.map((item) => item.group))];
  return <>
    <PageHeader title="我的偏好" subtitle="這些設定只會儲存在這台裝置的瀏覽器中。" onBack={onBack} />
    <section className="settings-card"><h2>穿搭呈現</h2><p>篩選推薦圖片的穿搭呈現；選「不限」會顯示全部。</p><div className="occasion-chips settings-chips">{presentationOptions.map((item) => <button key={item.id} className={preferences.presentation === item.id ? "occasion-chip active" : "occasion-chip"} onClick={() => setPreferences((value) => ({ ...value, presentation: item.id }))}>{item.icon} {item.label}</button>)}</div></section>
    <section className="settings-card"><h2>我的衣物</h2><p>可複選，先勾你最常穿、希望優先出現在建議中的單品。</p>{groups.map((group) => <div className="choice-group" key={group}><h3>{group}</h3><div className="selection-grid">{clothingOptions.filter((item) => item.group === group).map((item) => <button key={item.id} aria-pressed={preferences.clothingIds.includes(item.id)} className={preferences.clothingIds.includes(item.id) ? "choice-card selected" : "choice-card"} onClick={() => toggleValue("clothingIds", item.id)}><span className="clothing-icon"><ClothingIcon id={item.id} /></span>{item.label}<b>{preferences.clothingIds.includes(item.id) ? "✓" : "+"}</b></button>)}</div></div>)}</section>
    <section className="settings-card"><h2>喜歡色系</h2><p>可複選，最多 3 種；幫助推薦更接近你的風格。</p><div className="color-grid">{colorOptions.map((item) => <button key={item.id} disabled={!preferences.colorIds.includes(item.id) && preferences.colorIds.length >= 3} className={preferences.colorIds.includes(item.id) ? "color-choice selected" : "color-choice"} onClick={() => toggleValue("colorIds", item.id)}><i style={{ background: item.swatch }} />{item.label}{preferences.colorIds.includes(item.id) && " ✓"}</button>)}</div></section>
    <section className="settings-card"><h2>預設場合</h2><p>打開首頁時，先替你選好這個情境；你仍可在首頁隨時切換。</p><div className="occasion-chips settings-chips">{occasions.map((item) => <button key={item.id} className={preferences.defaultOccasion === item.id ? "occasion-chip active" : "occasion-chip"} onClick={() => { setPreferences((value) => ({ ...value, defaultOccasion: item.id })); setOccasion(item.id); }}>{item.icon} {item.label}</button>)}</div></section>
    <ReminderSettings city={city} reminder={preferences.reminder} onChange={(reminder) => setPreferences((value) => ({ ...value, reminder }))} />
    <div className="settings-links"><button onClick={onFavorites}>♡ 我的收藏 <span>{preferences.favorites.length}</span></button><button onClick={onWhy}>ⓘ 推薦依據說明</button></div>
  </>;
}

function ReminderSettings({ city, reminder, onChange }: { city: { name: string; latitude: number; longitude: number }; reminder: Preferences["reminder"]; onChange: (reminder: Preferences["reminder"]) => void }) {
  const [testStatus, setTestStatus] = useState("");
  async function syncReminder(nextReminder: Preferences["reminder"]) {
    onChange(nextReminder);
    if (!window.isSecureContext || !("serviceWorker" in navigator)) return;
    try { await savePushReminder(nextReminder, city); setTestStatus(nextReminder.enabled ? `已儲存 ${city.name} 的每日提醒並註冊這台裝置。` : "已關閉此裝置的每日提醒。"); }
    catch (error) { setTestStatus(error instanceof Error ? error.message : "提醒設定暫時無法儲存。"); }
  }
  async function sendTestNotification() {
    if (!("Notification" in window)) { setTestStatus("這個瀏覽器沒有提供通知功能；請改用 Safari、Chrome 或 Edge 開啟網站。"); return; }
    if (!("serviceWorker" in navigator)) { setTestStatus("目前是內嵌瀏覽器，無法測試通知。請改用 Safari、Chrome 或 Edge 直接開啟網站；iPhone 建議先加入主畫面再測試。"); return; }
    if (!window.isSecureContext) { setTestStatus("通知測試需要 HTTPS 網址或本機 localhost。部署後請再試。 "); return; }
    const permission = await Notification.requestPermission();
    if (permission !== "granted") { setTestStatus("尚未取得通知權限；請在瀏覽器設定中允許通知後再試。 "); return; }
    try { await sendServerPushTest(); setTestStatus("已送出依今日天氣生成的測試推播，請查看系統通知。 "); }
    catch {
      const registration = await navigator.serviceWorker.ready;
      if (registration.active) registration.active.postMessage({ type: "SHOW_TEST_NOTIFICATION" });
      else await registration.showNotification("Weather Outfit", { body: "本機通知測試成功。", icon: `${import.meta.env.BASE_URL}icon.svg`, tag: "weather-outfit-test" });
      setTestStatus("雲端推播暫時無法使用；已改用本機通知測試。 ");
    }
  }
  return <section className="settings-card"><h2>每日穿搭提醒</h2><p>啟用後會記住目前的 {city.name}，每天固定於台灣時間 07:30 依當日天氣推送簡短穿搭建議。</p><label className="reminder-control"><span><b>啟用每日提醒</b><small>{reminder.enabled ? `每天 07:30 · ${city.name}` : "尚未啟用"}</small></span><input type="checkbox" checked={reminder.enabled} onChange={(event) => void syncReminder({ ...reminder, enabled: event.target.checked, time: "07:30" })} /></label><p className="reminder-time"><span>提醒時間</span><b>每天 07:30（台灣時間）</b></p><div className="notification-test"><button type="button" onClick={() => void sendTestNotification()}>發送測試通知</button><small>{testStatus || "後端部署到 HTTPS 後，可用這裡確認真正的背景推播。"}</small></div></section>;
}

function PageHeader({ title, subtitle, onBack }: { title: string; subtitle: string; onBack: () => void }) { return <header className="page-header"><button onClick={onBack}>← 回到首頁</button><p className="eyebrow">WEATHER OUTFIT</p><h1>{title}</h1><p>{subtitle}</p></header>; }
function FavoritesView({ favorites, onBack, onRemove }: { favorites: Preferences["favorites"]; onBack: () => void; onRemove: (id: string) => void }) { return <><PageHeader title="我的收藏" subtitle="留住以後還想再穿的搭配。" onBack={onBack} />{favorites.length === 0 ? <section className="empty-card"><span>♡</span><h2>還沒有收藏</h2><p>回到首頁，從喜歡的穿搭按下「加入收藏」。</p></section> : <section className="favorites-list">{favorites.map((item) => <article key={item.id}><img src={item.image} alt="" /><div><h2>{item.title}</h2><p>{item.items.join(" · ")}</p><button onClick={() => onRemove(item.id)}>移除收藏</button></div></article>)}</section>}</> }
function WhyView({ onBack }: { onBack: () => void }) { return <><PageHeader title="推薦依據說明" subtitle="先確保你穿得舒服，再靠近你的風格。" onBack={onBack} /><section className="settings-card explanation"><h2>推薦順序</h2><ol><li><b>天氣適合度：</b>最低溫、最高溫、日夜溫差、降雨機率與紫外線會先評估。</li><li><b>首頁場合：</b>通勤、上課等情境會先篩出相符穿搭。</li><li><b>你的偏好：</b>衣物與色系會優先排序，不會因為缺少完全相同的照片而讓某個場合沒有建議。</li></ol><p>目前有 24 套實際穿著的示範造型；每套已建立天氣、場合與偏好標籤。後續只要新增照片與標籤，就能擴充推薦內容。</p></section></> }
