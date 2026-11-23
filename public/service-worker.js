// 變更安全性或 API 契約時必須遞增版本，避免已安裝的 PWA 持續執行舊版 JS。
// v6：清掉 v5 時期快取的舊百褶裙圖示，並改用「先顯示快取、背景更新」策略。
const CACHE_NAME = "weather-outfit-v6";
const APP_SHELL = ["./", "./index.html", "./manifest.webmanifest", "./icon.svg", "./offline.html"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CACHE_NAME).then((cache) => cache.addAll(APP_SHELL)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key)))).then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") return;

  // 不攔截外部的天氣/API 請求，避免資料被快取成舊天氣。
  if (new URL(event.request.url).origin !== self.location.origin) return;

  if (event.request.mode === "navigate") {
    event.respondWith(
      fetch(event.request)
        .then((response) => {
          const copy = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put("./", copy));
          return response;
        })
        .catch(() => caches.match("./").then((response) => response || caches.match("./offline.html"))),
    );
    return;
  }

  // 先回傳快取讓畫面立刻出現，同時在背景下載最新版本存回快取。
  // 這樣替換同名圖片或檔案後，最晚下次開啟就會更新，不會一直卡在舊版。
  event.respondWith(
    caches.open(CACHE_NAME).then(async (cache) => {
      const cached = await cache.match(event.request);
      // no-cache：略過瀏覽器的 HTTP 快取，向伺服器確認是否有新版本（沒變時只回 304，很省流量）。
      const network = fetch(event.request, { cache: "no-cache" }).then((response) => {
        if (response.ok) cache.put(event.request, response.clone());
        return response;
      });
      if (cached) {
        event.waitUntil(network.catch(() => undefined));
        return cached;
      }
      return network;
    }),
  );
});

self.addEventListener("push", (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    payload = { body: event.data?.text?.() || "" };
  }
  event.waitUntil(self.registration.showNotification(payload.title || "Weather Outfit", {
    body: payload.body || "今日穿搭建議已準備好。",
    icon: "./icon.svg",
    badge: "./icon.svg",
    tag: "weather-outfit-daily",
    renotify: true,
    data: { url: payload.url || "./" },
  }));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const target = new URL(event.notification.data?.url || "./", self.location.href).href;
  event.waitUntil(
    clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      // App 已經開著時，切到通知指定的頁面（例如晚上通知的「明天」），而不是另開視窗。
      const existing = windows.find((client) => client.url.startsWith(self.registration.scope));
      if (!existing) return clients.openWindow(target);
      return existing.navigate(target).then((client) => (client ?? existing).focus()).catch(() => existing.focus());
    }),
  );
});
