const CACHE_NAME = "weather-outfit-v1";
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
      if (new URL(event.request.url).origin === self.location.origin) {
        caches.open(CACHE_NAME).then((cache) => cache.put(event.request, response.clone()));
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
  const payload = event.data?.json?.() ?? {};
  event.waitUntil(self.registration.showNotification(payload.title || "Weather Outfit", {
    body: payload.body || "今日穿搭建議已準備好。",
    icon: "./icon.svg",
    badge: "./icon.svg",
    tag: "weather-outfit-daily",
    data: { url: payload.url || "./" },
  }));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(clients.openWindow(event.notification.data?.url || "./"));
});
