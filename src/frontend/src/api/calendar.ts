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

export interface CreateEventInput {
  title: string;
  start: Date;
  end: Date;
  description?: string | null;
  location?: string | null;
  isAllDay: boolean;
}

export async function createEvent(input: CreateEventInput): Promise<{ id: string }> {
  const res = await fetch("/api/calendar/events", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      title: input.title,
      start: input.start.toISOString(),
      end: input.end.toISOString(),
      description: input.description ?? null,
      location: input.location ?? null,
      isAllDay: input.isAllDay,
    }),
  });
  if (!res.ok) await handleError(res, "/api/calendar/events");
  return (await res.json()) as { id: string };
}

export interface FreeSlot {
  start: string;
  end: string;
  status: "passes" | "soft" | "hard";
  violatedBy: string | null;
}

export async function findFreeSlots(
  from: Date,
  to: Date,
  durationMinutes: number,
): Promise<FreeSlot[]> {
  const res = await fetch("/api/calendar/free-slots", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      from: from.toISOString(),
      to: to.toISOString(),
      durationMinutes,
    }),
  });
  if (!res.ok) await handleError(res, "/api/calendar/free-slots");
  const body = (await res.json()) as { slots: FreeSlot[] };
  return body.slots;
}

export type EventScope = "instance" | "series";

export async function deleteEvent(
  eventId: string,
  scope: EventScope = "instance",
): Promise<void> {
  const url = `/api/calendar/events/${encodeURIComponent(eventId)}?scope=${scope}`;
  const res = await fetch(url, { method: "DELETE" });
  if (!res.ok) await handleError(res, url);
}

export interface UpdateEventInput {
  title?: string;
  start?: Date;
  end?: Date;
  description?: string | null;
  location?: string | null;
  isAllDay?: boolean;
}

export async function updateEvent(
  eventId: string,
  update: UpdateEventInput,
  scope: EventScope = "instance",
): Promise<void> {
  const url = `/api/calendar/events/${encodeURIComponent(eventId)}?scope=${scope}`;
  const body: Record<string, unknown> = {};
  if (update.title !== undefined) body.title = update.title;
  if (update.start !== undefined) body.start = update.start.toISOString();
  if (update.end !== undefined) body.end = update.end.toISOString();
  if (update.description !== undefined) body.description = update.description;
  if (update.location !== undefined) body.location = update.location;
  if (update.isAllDay !== undefined) body.isAllDay = update.isAllDay;

  const res = await fetch(url, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) await handleError(res, url);
}
