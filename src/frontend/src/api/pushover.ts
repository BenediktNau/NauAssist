export interface PushoverSettings {
  hasToken: boolean;
  hasUserKey: boolean;
}

export async function getPushoverSettings(): Promise<PushoverSettings> {
  const res = await fetch("/api/settings/pushover");
  if (!res.ok) throw new Error(`Pushover-Settings-Load fehlgeschlagen: HTTP ${res.status}`);
  return (await res.json()) as PushoverSettings;
}

export async function updatePushoverSettings(token: string, userKey: string): Promise<void> {
  const res = await fetch("/api/settings/pushover", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token, userKey }),
  });
  if (!res.ok) throw new Error(`Pushover-Settings-Save fehlgeschlagen: HTTP ${res.status}`);
}
