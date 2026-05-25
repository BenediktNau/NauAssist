export interface CalendarEvent {
  id: string;
  title: string;
  start: string;
  end: string;
  description: string | null;
  location: string | null;
  isAllDay: boolean;
  /** Instanz einer wiederkehrenden Serie (Google: recurringEventId gesetzt). */
  isSeriesInstance: boolean;
}

export class NotConnectedError extends Error {
  constructor() {
    super("Google-Kalender ist nicht verbunden.");
    this.name = "NotConnectedError";
  }
}

async function handleError(res: Response, url: string): Promise<never> {
  if (res.status === 409) {
    throw new NotConnectedError();
  }
  const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
  throw new Error(body.error ?? `${url} failed: ${res.status}`);
}

export async function getCalendarRange(
  from: Date,
  to: Date,
): Promise<CalendarEvent[]> {
  const url = `/api/calendar/range?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`;
  const res = await fetch(url);
  if (!res.ok) await handleError(res, url);
  const body = (await res.json()) as { events: CalendarEvent[] };
  return body.events;
}
