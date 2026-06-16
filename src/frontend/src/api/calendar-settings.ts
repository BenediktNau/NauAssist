export interface CalendarSettings {
  calendarId: string;
  workingHoursStart: string;
  workingHoursEnd: string;
  defaultDurationMinutes: number;
  searchHorizonDays: number;
  hasGoogleCredentials: boolean;
  isConnected: boolean;
}

export interface UpdateCalendarSettingsPayload {
  calendarId: string;
  workingHoursStart: string;
  workingHoursEnd: string;
  defaultDurationMinutes: number;
  searchHorizonDays: number;
  googleClientId: string | null;
  googleClientSecret: string | null;
}

export async function getCalendarSettings(): Promise<CalendarSettings> {
  const res = await fetch("/api/settings/calendar");
  if (!res.ok) throw new Error(`GET /api/settings/calendar failed: ${res.status}`);
  return res.json();
}

export async function updateCalendarSettings(
  payload: UpdateCalendarSettingsPayload,
): Promise<void> {
  const res = await fetch("/api/settings/calendar", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `PUT /api/settings/calendar failed: ${res.status}`);
  }
}

export interface StartAuthResponse {
  authUrl: string;
  sessionId: string;
}

export async function startGoogleAuth(): Promise<StartAuthResponse> {
  const res = await fetch("/api/calendar/auth/start", { method: "POST" });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Auth nicht startbar" }));
    throw new Error(body.error ?? `POST /api/calendar/auth/start failed: ${res.status}`);
  }
  return res.json();
}

export async function completeGoogleAuth(
  sessionId: string,
  code: string,
): Promise<void> {
  const res = await fetch("/api/calendar/auth/complete", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ sessionId, code }),
  });
  if (!res.ok) {
    if (res.status === 410) {
      throw new Error("Sitzung abgelaufen, bitte neu starten.");
    }
    const body = await res.json().catch(() => ({ error: "Auth fehlgeschlagen" }));
    throw new Error(body.error ?? `POST /api/calendar/auth/complete failed: ${res.status}`);
  }
}

export async function disconnectGoogle(): Promise<void> {
  const res = await fetch("/api/calendar/auth/disconnect", { method: "POST" });
  if (!res.ok) throw new Error(`POST /api/calendar/auth/disconnect failed: ${res.status}`);
}
