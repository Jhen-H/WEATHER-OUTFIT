# Weather Outfit Web App

手機優先、也支援桌機瀏覽的響應式 PWA。使用者選擇城市、場合與個人穿搭偏好後，網站會根據天氣推薦一套有完整照片的穿搭。

## Demo 重點

- 全台 22 個縣市、目前位置與 7 天預報
- 今日與明日提供單日天氣與穿搭推薦；一週頁只顯示天氣
- 顯示目前溫度、體感、高低溫、降雨機率、UV、風速與日夜溫差
- 可切換通勤、上課、休閒、約會、戶外、正式
- 可設定女裝／男裝呈現、常穿單品、最多三種色系與預設場合
- 24 套完整穿搭照片，以溫度、降雨、UV、日夜溫差、場合與偏好推薦
- 收藏、穿搭詳情、PWA 安裝、離線基本頁面與通知測試

## 推薦邏輯

1. **天氣安全性**：雨天限定造型只在降雨機率達 50% 時出現；怕雨造型在雨天排除；並同時檢查最低溫、最高溫與日夜溫差。
2. **情境一致性**：場合與女裝／男裝呈現是硬篩選。
3. **個人偏好**：若有符合勾選衣物與色系的候選，會優先只從其中推薦；若目前照片沒有完全對應，才降級為最接近且天氣安全的造型，並在畫面明確說明。

## 技術地圖

| 技術 | 在專案中的用途 |
| --- | --- |
| React + TypeScript | 元件化互動與型別約束。 |
| Vite | 本機開發伺服器與正式靜態檔建置。 |
| Open-Meteo API | 取得天氣預報、溫度、降雨機率、UV 與風速。 |
| CSS | 手機優先的響應式介面與不同天氣背景。 |
| Web Manifest + Service Worker | 支援安裝成 PWA、離線基本頁與通知測試。 |

## 本機啟動

```bash
pnpm install
pnpm dev
```

開啟終端機顯示的網址，通常是 `http://127.0.0.1:5173/`。

正式建置：

```bash
pnpm build
```

## 後端提醒 API（開發中）

`server/WeatherOutfit.Api` 是第二階段的 ASP.NET Core Web API 骨架，已提供裝置註冊、提醒時間與 Push 訂閱資料的 API 路由，以及每分鐘檢查到期提醒的背景服務。

```bash
cd server/WeatherOutfit.Api
dotnet run --urls http://127.0.0.1:5099
```

目前資料只存於記憶體，重啟後會清除；下一步會改接資料庫、VAPID 金鑰與真正的 Web Push 發送。

## 專案結構

```text
src/
  App.tsx             首頁、偏好、收藏與 PWA 互動
  weather.ts          台灣城市座標與 Open-Meteo 資料轉換
  outfitCatalog.ts    每張照片的場合、偏好與天氣標籤
  recommendation.ts   推薦規則與候選排序
  preferences.ts      瀏覽器本機偏好儲存
  styles.css          手機優先與桌機響應式樣式
public/
  outfits/            完整穿搭照片
  preference-clothing/偏好設定的服飾圖卡
```

## 已知限制與下一階段

- 偏好、收藏與提醒時間目前只存在該裝置的瀏覽器，尚未有登入帳號。
- 「發送測試通知」可確認瀏覽器通知權限；真正的每日背景提醒仍需要後端儲存訂閱資料並發送 Web Push。
- 下一階段預計以 ASP.NET Core Web API + SQL Server 或 PostgreSQL 保存使用者、偏好、收藏與提醒設定。
- 穿搭照片是目前專案內的示範素材；擴充時需要一起新增照片與正確標籤。

## GitHub Pages 部署

專案已包含 GitHub Actions 工作流程。推送到 `main` 後會自動建置並部署 `dist/`：

1. 建立 GitHub repository，例如 `weather-outfit-pwa`。
2. Push 專案到 `main` 分支。
3. 到 **Settings → Pages**，將 Source 選為 **GitHub Actions**。
4. 網址會是 `https://<你的帳號>.github.io/weather-outfit-pwa/`。

> GitHub Pages 只能部署靜態前端；真實每日推播需要另外部署後端服務。
