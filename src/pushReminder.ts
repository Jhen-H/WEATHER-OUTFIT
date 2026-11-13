const apiBase = (import.meta.env.VITE_API_BASE_URL ?? "http://127.0.0.1:5099").replace(/\/$/, "");
const deviceKey = "weather-outfit-device-id";

type ReminderInput = { enabled: boolean; time: string };
type ReminderCity = { name: string; latitude: number; longitude: number };

function base64ToUint8Array(value: string) {
  const padding = "=".repeat((4 - (value.length % 4)) % 4);
  const base64 = (value + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = window.atob(base64);
  return Uint8Array.from(raw, (character) => character.charCodeAt(0));
}

async function responseJson<T>(response: Response): Promise<T> {
  if (response.ok) return response.json() as Promise<T>;
  throw new Error(`提醒服務暫時無法使用（${response.status}）`);
}

async function getDeviceId() {
  const existing = localStorage.getItem(deviceKey);
  if (existing) return existing;
  const device = await responseJson<{ id: string }>(await fetch(`${apiBase}/api/devices`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ deviceName: navigator.userAgent.slice(0, 100) }),
  }));
  localStorage.setItem(deviceKey, device.id);
  return device.id;
}

export async function savePushReminder(reminder: ReminderInput, city: ReminderCity) {
  const deviceId = await getDeviceId();
  if (reminder.enabled) {
    const registration = await navigator.serviceWorker.ready;
    const key = await responseJson<{ publicKey: string }>(await fetch(`${apiBase}/api/push-public-key`));
    const subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64ToUint8Array(key.publicKey) });
    const json = subscription.toJSON();
    await responseJson(await fetch(`${apiBase}/api/push-subscriptions`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ deviceId, endpoint: json.endpoint, p256dh: json.keys?.p256dh, auth: json.keys?.auth }),
    }));
  }
  await responseJson(await fetch(`${apiBase}/api/reminders/${deviceId}`, {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ enabled: reminder.enabled, time: reminder.time, timeZoneId: "Asia/Taipei", cityName: city.name, latitude: city.latitude, longitude: city.longitude }),
  }));
}

export async function sendServerPushTest() {
  const deviceId = localStorage.getItem(deviceKey);
  if (!deviceId) throw new Error("請先啟用每日提醒。");
  await responseJson(await fetch(`${apiBase}/api/reminders/${deviceId}/test`, { method: "POST" }));
}
