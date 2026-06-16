import { useEffect, useMemo, useState } from "react";
import {
  differenceInMinutes,
  isToday,
  startOfDay,
} from "date-fns";
import {
  allDayEventsForDay,
  computeGridRange,
  formatTime,
  layoutDay,
  timedEventsForDay,
  type ParsedEvent,
} from "./utils";
import { eventColor } from "./eventColors";
import type { PopoverState } from "./EventPopover";
import type { ParsedProposal } from "./WeekView";
import type { SlotInfo } from "@/api/types";

interface DayViewProps {
  /** Anzuzeigender Tag. */
  day: Date;
  /** Termine (mind. dieses Tages — überzählige werden ignoriert). */
  events: ParsedEvent[];
  workingHoursStart: string;
  workingHoursEnd: string;
  rowHeight?: number;
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
  proposals?: ParsedProposal[];
  onPickProposal?: (slot: SlotInfo) => void;
}

const DEFAULT_ROW_HEIGHT = 56;
const HOUR_LABEL_WIDTH = 52;

export function DayView({
  day,
  events,
  workingHoursStart,
  workingHoursEnd,
  rowHeight = DEFAULT_ROW_HEIGHT,
  onHoverEvent,
  onClickEvent,
  proposals,
  onPickProposal,
}: DayViewProps) {
  const timed = useMemo(() => timedEventsForDay(events, day), [events, day]);
  const allDay = useMemo(() => allDayEventsForDay(events, day), [events, day]);
  const positioned = useMemo(() => layoutDay(timed), [timed]);

  const proposalsForDay = useMemo(
    () => (proposals ?? []).filter((p) => sameDay(p.startDate, day)),
    [proposals, day],
  );

  // Grid-Bereich an Working-Hours + Termine + Vorschläge dieses Tages anpassen.
  const rangeEvents = useMemo(() => {
    const synthetic = proposalsForDay.map((p, i) => ({
      id: `__proposal_${i}`,
      title: "",
      start: p.slot.start,
      end: p.slot.end,
      description: null,
      location: null,
      isAllDay: false as const,
      isSeriesInstance: false,
      startDate: p.startDate,
      endDate: p.endDate,
    }));
    return [...timed, ...synthetic];
  }, [timed, proposalsForDay]);

  const range = useMemo(
    () => computeGridRange(workingHoursStart, workingHoursEnd, rangeEvents),
    [workingHoursStart, workingHoursEnd, rangeEvents],
  );

  const hours = useMemo(
    () =>
      Array.from({ length: range.endHour - range.startHour }, (_, i) => range.startHour + i),
    [range],
  );

  const minutesInGrid = (range.endHour - range.startHour) * 60;
  const gridHeight = hours.length * rowHeight;
  const minuteToY = (m: number) =>
    ((m - range.startHour * 60) / minutesInGrid) * gridHeight;

  const nowY = useNowY(day, minuteToY, range);

  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt">
      {allDay.length > 0 && (
        <div className="flex flex-wrap gap-1.5 border-b border-nau-line p-2">
          <span className="self-center font-mono text-[9px] tracking-mono text-nau-fg-dim">
            GANZTAG
          </span>
          {allDay.map((e) => {
            const c = eventColor(e);
            return (
              <button
                key={e.id}
                type="button"
                onMouseEnter={(ev) =>
                  onHoverEvent({ event: e, anchor: ev.currentTarget.getBoundingClientRect(), pinned: false })
                }
                onMouseLeave={() => onHoverEvent(null)}
                onClick={(ev) =>
                  onClickEvent({ event: e, anchor: ev.currentTarget.getBoundingClientRect(), pinned: true })
                }
                className="cursor-pointer truncate rounded-[4px] border-none px-2 py-1 text-left font-sans text-[12px]"
                style={{ background: c.bg, color: c.fg }}
                title={e.isSeriesInstance ? `${e.title} (Serie)` : e.title}
              >
                {e.isSeriesInstance && <span className="mr-1 opacity-70">↻</span>}
                {e.title}
              </button>
            );
          })}
        </div>
      )}

      <div className="relative overflow-hidden">
        <div className="relative" style={{ height: gridHeight }}>
          {/* Stundenraster + Beschriftung */}
          {hours.map((h, i) => (
            <div
              key={h}
              className="absolute inset-x-0 border-t"
              style={{ top: i * rowHeight, borderColor: "rgba(255,255,255,0.06)" }}
            >
              <span className="absolute left-2 top-0.5 font-mono text-[10px] text-nau-fg-dim">
                {String(h).padStart(2, "0")}:00
              </span>
            </div>
          ))}

          {/* Spaltenfläche rechts der Achse */}
          <div className="absolute inset-y-0 right-0" style={{ left: HOUR_LABEL_WIDTH }}>
            {positioned.map((p) => {
              const top = minuteToY(p.topMinutes);
              const height = Math.max(
                26,
                minuteToY(p.topMinutes + p.durationMinutes) - top - 3,
              );
              const c = eventColor(p.event);
              return (
                <button
                  key={p.event.id}
                  type="button"
                  onMouseEnter={(ev) =>
                    onHoverEvent({ event: p.event, anchor: ev.currentTarget.getBoundingClientRect(), pinned: false })
                  }
                  onMouseLeave={() => onHoverEvent(null)}
                  onClick={(ev) =>
                    onClickEvent({ event: p.event, anchor: ev.currentTarget.getBoundingClientRect(), pinned: true })
                  }
                  className="absolute cursor-pointer overflow-hidden rounded-[5px] border-none px-2 py-1 text-left"
                  style={{
                    top,
                    left: `calc(${p.left * 100}% + 2px)`,
                    width: `calc(${p.width * 100}% - 6px)`,
                    height,
                    background: c.bg,
                    color: c.fg,
                    outline: p.hasConflict ? "1.5px solid #f472b6" : "none",
                  }}
                  title={p.event.isSeriesInstance ? `${p.event.title} (Serie)` : p.event.title}
                >
                  <div className="font-sans text-[13px] font-medium leading-tight line-clamp-2">
                    {p.event.isSeriesInstance && <span className="mr-1 opacity-70">↻</span>}
                    {p.event.title}
                  </div>
                  <div className="mt-0.5 font-mono text-[10px] opacity-75">
                    {formatTime(p.event.startDate)}–{formatTime(p.event.endDate)}
                  </div>
                </button>
              );
            })}

            {proposalsForDay.map((p, i) => {
              const dayStart = startOfDay(p.startDate);
              const topMin = differenceInMinutes(p.startDate, dayStart);
              const durationMin = Math.max(15, differenceInMinutes(p.endDate, p.startDate));
              const top = minuteToY(topMin);
              const height = Math.max(26, minuteToY(topMin + durationMin) - top - 3);
              const label = p.slot.note ?? "VORSCHLAG";
              return (
                <button
                  key={`proposal-${i}`}
                  type="button"
                  onClick={() => onPickProposal?.(p.slot)}
                  disabled={!onPickProposal}
                  className="absolute z-10 cursor-pointer overflow-hidden rounded-[5px] border-2 border-dashed px-2 py-1 text-left font-mono disabled:cursor-default"
                  style={{
                    top,
                    left: 2,
                    right: 4,
                    height,
                    background: "rgba(250,204,21,0.10)",
                    borderColor: "#facc15",
                    color: "#facc15",
                  }}
                  title={`Vorschlag · ${formatTime(p.startDate)}–${formatTime(p.endDate)}`}
                >
                  <div className="truncate text-[12px] font-bold tracking-mono">▸ {label}</div>
                  <div className="truncate text-[10px] opacity-80">
                    {formatTime(p.startDate)}–{formatTime(p.endDate)}
                  </div>
                </button>
              );
            })}

            {/* Jetzt-Linie */}
            {nowY !== null && (
              <div className="pointer-events-none absolute inset-x-0 z-20" style={{ top: nowY }}>
                <div className="absolute -left-1 -top-[3px] h-[7px] w-[7px] rounded-full bg-nau-accent" />
                <div className="h-px w-full bg-nau-accent" />
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function sameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  );
}

/** Y-Position der Jetzt-Linie — nur am heutigen, sichtbaren Tag. */
function useNowY(
  day: Date,
  minuteToY: (m: number) => number,
  range: { startHour: number; endHour: number },
): number | null {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 60_000);
    return () => window.clearInterval(id);
  }, []);

  if (!isToday(day)) return null;
  const minutes = now.getHours() * 60 + now.getMinutes();
  if (minutes < range.startHour * 60 || minutes > range.endHour * 60) return null;
  return minuteToY(minutes);
}
