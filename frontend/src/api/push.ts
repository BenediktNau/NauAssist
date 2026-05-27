export interface VapidPublicKeyResponse {
  publicKey: string;
  configured: boolean;
}

export async function getVapidPublicKey(): Promise<VapidPublicKeyResponse> {
  const res = await fetch("/api/push/vapid-public-key");
  if (!res.ok) throw new Error(`GET /api/push/vapid-public-key failed: ${res.status}`);
  return res.json();
}

export interface SubscribePayload {
  endpoint: string;
  keys: { p256dh: string; auth: string };
}

export async function postSubscription(payload: SubscribePayload): Promise<void> {
  const res = await fetch("/api/push/subscribe", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `POST /api/push/subscribe failed: ${res.status}`);
  }
}

export async function deleteSubscription(endpoint: string): Promise<void> {
  const res = await fetch(
    `/api/push/subscribe?endpoint=${encodeURIComponent(endpoint)}`,
    { method: "DELETE" },
  );
  if (!res.ok && res.status !== 404) {
    throw new Error(`DELETE /api/push/subscribe failed: ${res.status}`);
  }
}

export async function sendTestPush(): Promise<{ sent: number }> {
  const res = await fetch("/api/push/test", { method: "POST" });
  if (!res.ok) {
    throw new Error(`POST /api/push/test failed: ${res.status}`);
  }
  return res.json();
}

// --- Browser-Helpers ---

export function isPushSupported(): boolean {
  return "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
}

export async function getCurrentSubscription(): Promise<PushSubscription | null> {
  if (!isPushSupported()) return null;
  const reg = await navigator.serviceWorker.ready;
  return reg.pushManager.getSubscription();
}

export async function subscribeBrowser(vapidPublicKey: string): Promise<PushSubscription> {
  const reg = await navigator.serviceWorker.ready;
  return reg.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
  });
}

export async function unsubscribeBrowser(): Promise<string | null> {
  const sub = await getCurrentSubscription();
  if (!sub) return null;
  const endpoint = sub.endpoint;
  await sub.unsubscribe();
  return endpoint;
}

function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const rawData = atob(base64);
  const output = new Uint8Array(rawData.length);
  for (let i = 0; i < rawData.length; ++i) output[i] = rawData.charCodeAt(i);
  return output;
}

export function subscriptionToPayload(sub: PushSubscription): SubscribePayload {
  const json = sub.toJSON();
  return {
    endpoint: json.endpoint!,
    keys: {
      p256dh: json.keys!.p256dh,
      auth: json.keys!.auth,
    },
  };
}
