import {
  addDays,
  differenceInMinutes,
  isSameDay,
  isWithinInterval,
  parseISO,
} from "date-fns";
import type { CalendarEvent } from "@/api/calendar";

export interface TimedEvent extends CalendarEvent {
  startDate: Date;
  endDate: Date;
  isAllDay: false;
}

export interface AllDayEvent extends CalendarEvent {
  startDate: Date;
  endDate: Date;
  isAllDay: true;
}

export type ParsedEvent = TimedEvent | AllDayEvent;

export function parseEvents(events: CalendarEvent[]): ParsedEvent[] {
  return events.map((e) => ({
    ...e,
    startDate: parseISO(e.start),
    endDate: parseISO(e.end),
  })) as ParsedEvent[];
}

export function timedEventsForDay(events: ParsedEvent[], day: Date): TimedEvent[] {
  return events.filter(
    (e): e is TimedEvent =>
      !e.isAllDay && isSameDay(e.startDate, day),
  );
}

export function allDayEventsForDay(events: ParsedEvent[], day: Date): AllDayEvent[] {
  return events.filter(
    (e): e is AllDayEvent =>
      e.isAllDay &&
      isWithinInterval(day, {
        start: e.startDate,
        end: addDays(e.endDate, -1),
      }),
  );
}

export interface PositionedEvent {
  event: TimedEvent;
  /** 0..1 — fraction of day-column */
  left: number;
  /** 0..1 */
  width: number;
  /** Minutes from grid top */
  topMinutes: number;
  /** Minutes height */
  durationMinutes: number;
  /** true wenn dieses Event mit einem anderen am selben Tag überlappt */
  hasConflict: boolean;
}

/**
 * Greedy lane allocation: gruppiert überlappende Events in Cluster,
 * verteilt sie auf parallele Spalten innerhalb des Clusters.
 */
export function layoutDay(events: TimedEvent[]): PositionedEvent[] {
  if (events.length === 0) return [];
  const sorted = [...events].sort(
    (a, b) => a.startDate.getTime() - b.startDate.getTime(),
  );

  type Lane = { endTime: number };
  type Cluster = { events: TimedEvent[]; lanes: Lane[]; eventLanes: number[] };

  const clusters: Cluster[] = [];
  let current: Cluster | null = null;
  let clusterMaxEnd = -Infinity;

  for (const e of sorted) {
    const start = e.startDate.getTime();
    const end = e.endDate.getTime();
    if (current === null || start >= clusterMaxEnd) {
      current = { events: [], lanes: [], eventLanes: [] };
      clusters.push(current);
      clusterMaxEnd = -Infinity;
    }
    let laneIdx = current.lanes.findIndex((l) => l.endTime <= start);
    if (laneIdx === -1) {
      laneIdx = current.lanes.length;
      current.lanes.push({ endTime: end });
    } else {
      current.lanes[laneIdx].endTime = end;
    }
    current.events.push(e);
    current.eventLanes.push(laneIdx);
    if (end > clusterMaxEnd) clusterMaxEnd = end;
  }

  const result: PositionedEvent[] = [];
  for (const c of clusters) {
    const cols = c.lanes.length;
    const hasConflict = cols > 1;
    for (let i = 0; i < c.events.length; i++) {
      const e = c.events[i];
      const col = c.eventLanes[i];
      const dayStart = new Date(e.startDate);
      dayStart.setHours(0, 0, 0, 0);
      result.push({
        event: e,
        left: col / cols,
        width: 1 / cols,
        topMinutes: differenceInMinutes(e.startDate, dayStart),
        durationMinutes: Math.max(15, differenceInMinutes(e.endDate, e.startDate)),
        hasConflict,
      });
    }
  }
  return result;
}

export function parseTimeOfDay(hhmm: string): { hour: number; minute: number } {
  const [h, m] = hhmm.split(":").map((n) => parseInt(n, 10));
  return {
    hour: Number.isFinite(h) ? h : 9,
    minute: Number.isFinite(m) ? m : 0,
  };
}

export interface GridRange {
  /** Erste Stunde, die im Grid angezeigt wird (0–23) */
  startHour: number;
  /** Letzte Stunde + 1 (1–24) */
  endHour: number;
}

/** Working-Hours plus Puffer und Events außerhalb berücksichtigen. */
export function computeGridRange(
  workingStart: string,
  workingEnd: string,
  events: TimedEvent[],
): GridRange {
  const ws = parseTimeOfDay(workingStart);
  const we = parseTimeOfDay(workingEnd);
  let startHour = Math.max(0, ws.hour - 1);
  let endHour = Math.min(24, we.minute > 0 ? we.hour + 2 : we.hour + 1);

  for (const e of events) {
    const sh = e.startDate.getHours();
    const eh = e.endDate.getHours() + (e.endDate.getMinutes() > 0 ? 1 : 0);
    if (sh < startHour) startHour = Math.max(0, sh);
    if (eh > endHour) endHour = Math.min(24, eh);
  }

  if (endHour <= startHour) {
    return { startHour: 8, endHour: 20 };
  }
  return { startHour, endHour };
}

export function hexAlpha(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

export function formatTime(d: Date): string {
  return d.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });
}
