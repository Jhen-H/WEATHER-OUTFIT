// 變更安全性或 API 契約時必須遞增版本，避免已安裝的 PWA 持續執行舊版 JS。
const CACHE_NAME = "weather-outfit-v4";
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

  event.respondWith(
    caches.match(event.request).then((cached) => cached || fetch(event.request).then((response) => {
      if (response.ok) {
        const copy = response.clone();
        caches.open(CACHE_NAME).then((cache) => cache.put(event.request, copy));
      }
      return response;
    })),
  );
});

self.addEventListener("message", (event) => {
  if (event.data?.type !== "SHOW_TEST_NOTIFICATION") return;
  event.waitUntil(self.registration.showNotification("Weather Outfit", {
    body: "測試成功：每日穿搭提醒已可顯示。",
    icon: "./icon.svg",
    badge: "./icon.svg",
    tag: "weather-outfit-test",
    renotify: true,
  }));
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
      const existing = windows.find((client) => client.url.startsWith(self.registration.scope));
      return existing ? existing.focus() : clients.openWindow(target);
    }),
  );
});
