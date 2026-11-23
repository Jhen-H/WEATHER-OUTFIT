# Weather Outfit｜每日天氣穿搭提醒

手機優先、也支援桌機瀏覽的 PWA。使用者選擇台灣縣市或目前位置、場合與個人偏好後，網站會依當日天氣提供可解釋的穿搭推薦；啟用提醒後，每天早上會收到一則簡短的天氣與穿搭通知。

- 網站 Demo：https://jhen-h.github.io/WEATHER-OUTFIT/
- 原始碼：https://github.com/Jhen-H/WEATHER-OUTFIT
- 專案文件（Notion）：https://www.notion.so/App-2a255e5883f780209ba5ecb150b59a33
- 設計原型（Figma）：https://www.figma.com/design/HymU0ZgztmfSzseY8shUHB/Weather-Outfit-App?node-id=12-73

## 畫面預覽

| 首頁與今日天氣 | 依天氣推薦穿搭 | 偏好設定 |
| :---: | :---: | :---: |
| <img src="docs/screenshots/home.jpg" alt="首頁：臺中市晴天 26 度、高低溫、降雨機率與 UV，以及場合選擇" width="240"> | <img src="docs/screenshots/outfit.jpg" alt="穿搭推薦：輕風衣戶外搭配，附推薦原因與日夜溫差提示" width="240"> | <img src="docs/screenshots/preferences.jpg" alt="偏好設定：穿搭呈現與常穿衣物選擇" width="240"> |

每天早上 07:30 收到的推播通知：

<img src="docs/screenshots/notification.jpg" alt="iPhone 通知：臺中市目前晴天，今天預報晴天，降雨 14%，16 到 27 度，日夜溫差大，建議分層並帶薄外套" width="420">

## 作品亮點

- 全台 22 個縣市、目前位置、今天／明天／未來 7 天氣象資訊。
- 今日與明日提供完整穿搭推薦；一週模式聚焦顯示天氣。
- 依最高／最低溫、日夜溫差、降雨機率、紫外線、風速、場合與使用者偏好篩選。
- 可選女裝／男裝呈現、常穿單品、最多三種色系與預設場合。
- 收藏穿搭、推薦原因、天氣情境背景與完整穿搭照片。
- 可加入主畫面使用，並支援基本離線 App Shell。
- 在 iOS 加入主畫面並允許通知後，可接收每日 Web Push 提醒；發送失敗會在 15 分鐘後自動補發。
- 穿搭照片使用 WebP 並依顯示尺寸縮圖，素材總大小從約 130 MB 降到約 4 MB。
- 推薦規則與偏好資料有單元測試，部署前自動執行。

## 系統架構

```mermaid
flowchart LR
  U[使用者／PWA] -->|讀取天氣預報| W[Open-Meteo]
  U -->|HTTPS API| A[ASP.NET Core Web API]
  A --> S[(Azure SQL Database)]
  A -->|取得通知天氣摘要| W
  F[Azure Function Timer<br/>台灣 07:30 發送、07:45 補發] -->|讀取提醒與通知紀錄| S
  F -->|取得通知文案| A
  F --> P[Web Push / VAPID]
  P --> U
  G[GitHub Actions] --> GH[GitHub Pages]
  G --> A
  G --> F
```

## 技術選擇

| 技術 | 用途 |
| --- | --- |
| React + TypeScript + Vite | 前端元件、互動、型別檢查與靜態建置。 |
| CSS | 手機優先的響應式介面與依天氣改變的背景。 |
| Open-Meteo API | 取得氣溫、降雨機率、UV、風速與天氣狀態。 |
| Web Manifest + Service Worker | PWA 安裝、基本離線畫面與接收 Push；圖片先顯示快取並在背景更新。 |
| ASP.NET Core Web API (.NET 10) | 裝置註冊、提醒設定、VAPID 公鑰與通知文案。 |
| Azure SQL Database | 儲存裝置、Push 訂閱與提醒城市／時間。 |
| Azure Functions Timer Trigger | 每天台灣時間 07:30 讀取提醒資料並送出 Web Push，07:45 補發失敗的裝置。 |
| Azure App Service | 部署 Web API。 |
| GitHub Pages + GitHub Actions | 部署前端與自動化部署 API／Function；前端部署前先跑測試。 |
| Vitest | 推薦規則與舊資料轉換的單元測試。 |

## 推薦規則

推薦邏輯刻意採用可說明、可測試的規則，而非黑箱模型：

1. **天氣安全性優先**：雨天限定造型只在降雨機率高時出現；怕雨造型在雨天排除；同時檢查高低溫、UV 與日夜溫差。
2. **情境一致性**：場合與女裝／男裝呈現先做硬篩選。
3. **個人偏好排序**：符合勾選衣物與色系的候選優先；素材不足時才降級為最接近、但仍符合天氣安全條件的推薦，並在 UI 說明原因。
4. **通知與完整推薦分工**：通知只提供當日預報與簡短穿搭方向；點開後由前端呈現完整照片與搭配清單。

## 每日提醒如何運作

1. 使用者在偏好頁啟用每日提醒；App 先詢問通知權限，再由瀏覽器建立 Push 訂閱。
2. 前端把裝置與目前選擇的城市傳到 API，API 寫入 Azure SQL。
3. Azure Function 每天 **台灣時間 07:30** 執行（固定使用 UTC 23:30，避免雲端時區設定造成偏差），07:45 再執行一次，只補發第一次失敗的裝置。
4. Function 讀取已啟用的裝置，向 API 取得該城市的當日預報摘要（同城市只查一次），再以 VAPID 發出 Web Push。
5. 每台裝置每天在 `NotificationLogs` 留下一筆紀錄（`Sending`／`Sent`／`Failed`），以「裝置＋日期」唯一鍵確保不會重複推播；SQL 冷啟動與 API 冷啟動都有重試。

需要立即驗證時，可呼叫受 Function 金鑰保護的 `POST /api/RunDailyReminderNow`。它與排程共用同一段發送邏輯，並回傳「符合條件、已發送、已處理、失敗」的台數與失敗原因；同一天重複呼叫不會重複推播。

通知中的「目前晴天；今天預報可能有雨」並不衝突：前半段是即時天氣，後半段是全天的最高降雨機率預報。

> 目前提醒時間固定為每天 07:30（台灣時間）。資料庫的時間欄位是日後支援自訂提醒時間的預留設計，現階段不提供其他時間。

## 本機啟動

### 前端

```bash
pnpm install
pnpm dev
```

通常會啟動在 `http://127.0.0.1:5173/`。此網址只存在於本機；對外請使用 GitHub Pages Demo。

正式建置與測試：

```bash
pnpm build
pnpm test
```

### API 與提醒 Function

API 與 Function App 都需要在 Azure Application Settings 設定下列機密，**不可提交到 Git**：

- `ConnectionStrings__WeatherOutfit`：Azure SQL 連線字串
- `Vapid__Subject`
- `Vapid__PublicKey`
- `Vapid__PrivateKey`

Function App 另外需要 `AzureWebJobsStorage`（Azure 儲存體帳戶的連接字串）。Timer 觸發會用它記錄排程狀態；少了它，主機仍會顯示正常運作，但排程永遠不會觸發。

本機啟動 API：

```bash
dotnet run --project server/WeatherOutfit.Api --urls http://127.0.0.1:5099
```

正式環境的值放在 Azure App Settings；GitHub Actions 使用 Azure OIDC 身分部署，不使用 publish profile。

## API 與資料設計

| 路由 | 用途 |
| --- | --- |
| `POST /api/devices` | 為此瀏覽器／主畫面 App 建立裝置識別碼。 |
| `PUT /api/reminders/{deviceId}` | 儲存是否啟用提醒與選定縣市。 |
| `POST /api/push-subscriptions` | 儲存瀏覽器建立的 Web Push 訂閱。 |

| 資料表 | 儲存內容 |
| --- | --- |
| `Devices` | 裝置識別碼、簡短裝置名稱、建立時間。 |
| `Reminders` | 是否啟用、固定 07:30、縣市、縣市座標與 Push 訂閱資訊。 |
| `NotificationLogs` | 每日發送狀態；以「裝置＋日期」唯一鍵避免重複提醒。 |

提醒設定 API 會對每台裝置建立一次性 `deviceSecret`，前端只保存在該裝置，伺服器僅保存其雜湊。更新提醒與寫入 Push 訂閱都必須驗證此憑證；提醒寫入 API 另有每 IP 每分鐘 10 次的 rate limit，Push endpoint 僅接受受支援服務的 HTTPS 網址。

## 排錯紀錄：每日推播從未送出

部署後早上一直沒有收到通知，排查過程如下：

1. **先看資料，不先猜**：`NotificationLogs` 整張表是空的，而符合條件的裝置有 3 台，代表 Function 從未跑到發送步驟，問題不在推播或手機。
2. **排除被停用**：Function App 首頁顯示正常運作；手動觸發回 403 是因為管理端點被隔離，不是 App 停止。
3. **查主機日誌**：Application Insights 反覆出現 `Retrying to start listener for Functions.DailyReminder`，例外訊息是 `Could not create BlobContainerClient for ScheduleMonitor`。
4. **找到根因**：Function App 缺少 `AzureWebJobsStorage`，Timer 的排程監視器無法建立，監聽器從未啟動。
5. **修正與驗證**：補上儲存體連接字串並重新啟動，儲存體出現 `azure-webjobs-hosts` 容器；再新增手動觸發端點，立即確認推播送達。

同一輪排查也修正了另一個隱性問題：Function 原本只撈提醒時間「剛好等於 07:30」的裝置，而前端會把舊版留下的其他時間寫回資料庫，讓那些裝置每天都被跳過。現在提醒時間由前端、API 與資料庫三處一致固定為 07:30，Function 改為只依「已啟用且有訂閱」篩選。

## 專案結構

```text
src/
  App.tsx                         首頁、偏好、收藏與互動
  weather.ts                      台灣城市座標與 Open-Meteo 資料轉換
  outfitCatalog.ts                照片的場合、偏好與天氣標籤
  recommendation.ts               穿搭推薦規則與候選排序
  pushReminder.ts                 瀏覽器訂閱與提醒 API 呼叫
  preferences.ts                  瀏覽器本機偏好與收藏、舊資料轉換
  *.test.ts                       推薦規則與偏好資料的單元測試（Vitest）
  styles.css                      手機優先與桌機響應式樣式
server/
  WeatherOutfit.Api/              ASP.NET Core API、Azure SQL、Web Push
  WeatherOutfit.Reminders/        Azure Function 每日定時推播
public/
  outfits/                        完整穿搭照片素材
  preference-clothing/            偏好設定的服飾圖卡
```

## 已知限制與取捨

- iOS 必須使用 Safari 將網站「加入主畫面」，並允許通知，才能在背景收到 Web Push。
- 偏好與收藏存在瀏覽器 localStorage，尚未提供跨裝置同步。提醒訂閱與提醒城市則存於 Azure SQL，且每台裝置需各自開啟提醒。
- 使用「目前位置」時，原始 GPS 座標只在瀏覽器內用來找出最近縣市；送至後端的是該縣市的固定座標，不會儲存使用者的精確位置。
- 免費或低階 App Service／Azure SQL 可能有冷啟動，UI 會保留載入與失敗提示。
- 目前照片為示範素材；新增服飾類型時，需要同時建立素材與正確的推薦標籤。
- 定時提醒以雲端服務執行，不依賴開發者電腦是否開機。
- Web Push 訂閱失效時，Function 會在收到 404／410 後自動移除該訂閱；使用者可再次啟用提醒重新訂閱。

## 後續改善方向

1. 監控：在 Application Insights 設定「排程未執行」告警；目前以 `NotificationLogs` 與手動觸發端點確認。
2. 跨裝置同步：以帳號系統同步偏好與收藏。
3. UX：加強離線、定位失敗與服務冷啟動的提示。
4. 測試：為 API 與 Function 補上整合測試。
