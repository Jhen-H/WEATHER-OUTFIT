const apiBase = (import.meta.env.VITE_API_BASE_URL ?? "http://127.0.0.1:5099").replace(/\/$/, "");
const deviceKey = "weather-outfit-device-id";
const deviceSecretKey = "weather-outfit-device-secret";

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
  let detail = "";
  try {
    const body = await response.json() as { message?: string; detail?: string; title?: string };
    detail = body.message ?? body.detail ?? body.title ?? "";
  } catch { /* API 回應可能沒有 JSON 內容。 */ }
  throw new Error(`提醒服務暫時無法使用（${response.status}）${detail ? `：${detail}` : ""}`);
}

async function getDeviceCredentials() {
  const existing = localStorage.getItem(deviceKey);
  const existingSecret = localStorage.getItem(deviceSecretKey);
  if (existing && existingSecret) return { id: existing, secret: existingSecret };
  // 舊版 App 沒有 deviceSecret；重新註冊後會由後端清掉相同 Push endpoint 的舊紀錄。
  localStorage.removeItem(deviceKey);
  localStorage.removeItem(deviceSecretKey);
  const device = await responseJson<{ id: string; secret: string }>(await fetch(`${apiBase}/api/devices`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ deviceName: navigator.userAgent.slice(0, 100) }),
  }));
  localStorage.setItem(deviceKey, device.id);
  localStorage.setItem(deviceSecretKey, device.secret);
  return device;
}

export async function savePushReminder(reminder: ReminderInput, city: ReminderCity) {
  const device = await getDeviceCredentials();
  if (reminder.enabled) {
    const registration = await navigator.serviceWorker.ready;
    const key = await responseJson<{ publicKey: string }>(await fetch(`${apiBase}/api/push-public-key`));
    const subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64ToUint8Array(key.publicKey) });
    const json = subscription.toJSON();
    await responseJson(await fetch(`${apiBase}/api/push-subscriptions`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ deviceId: device.id, deviceSecret: device.secret, endpoint: json.endpoint, p256dh: json.keys?.p256dh, auth: json.keys?.auth }),
    }));
  }
  await responseJson(await fetch(`${apiBase}/api/reminders/${device.id}`, {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ enabled: reminder.enabled, time: reminder.time, deviceSecret: device.secret, timeZoneId: "Asia/Taipei", cityName: city.name, latitude: city.latitude, longitude: city.longitude }),
  }));
}
