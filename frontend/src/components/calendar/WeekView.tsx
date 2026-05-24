import { useMemo } from "react";
import { addDays, format, isToday, startOfDay } from "date-fns";
import { de } from "date-fns/locale";
import {
  allDayEventsForDay,
  computeGridRange,
  formatTime,
  layoutDay,
  timedEventsForDay,
  type ParsedEvent,
  type TimedEvent,
} from "./utils";

interface WeekViewProps {
  weekStart: Date;
  events: ParsedEvent[];
  workingHoursStart: string;
  workingHoursEnd: string;
  highlightedSlot: { start: Date; end: Date } | null;
  rowHeight?: number;
}

const DEFAULT_ROW_HEIGHT = 44;
const HOUR_LABEL_WIDTH = 48;

export function WeekView({
  weekStart,
  events,
  workingHoursStart,
  workingHoursEnd,
  highlightedSlot,
  rowHeight = DEFAULT_ROW_HEIGHT,
}: WeekViewProps) {
  const days = useMemo(
    () => Array.from({ length: 7 }, (_, i) => addDays(weekStart, i)),
    [weekStart],
  );

  const timedEvents = useMemo(
    () => events.filter((e): e is TimedEvent => !e.isAllDay),
    [events],
  );

  const range = useMemo(
    () => computeGridRange(workingHoursStart, workingHoursEnd, timedEvents),
    [workingHoursStart, workingHoursEnd, timedEvents],
  );

  const hours = useMemo(
    () => Array.from({ length: range.endHour - range.startHour }, (_, i) => range.startHour + i),
    [range],
  );

  const minutesInGrid = (range.endHour - range.startHour) * 60;
  const gridHeight = hours.length * rowHeight;
  const minuteToY = (m: number) => ((m - range.startHour * 60) / minutesInGrid) * gridHeight;

  const allDayPerDay = useMemo(
    () => days.map((d) => allDayEventsForDay(events, d)),
    [days, events],
  );
  const hasAnyAllDay = allDayPerDay.some((arr) => arr.length > 0);

  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt">
      <div
        className="grid border-b border-nau-line"
        style={{ gridTemplateColumns: `${HOUR_LABEL_WIDTH}px repeat(7, 1fr)` }}
      >
        <span />
        {days.map((d) => {
          const today = isToday(d);
          return (
            <div
              key={d.toISOString()}
              className="border-l border-nau-line px-2 py-2 text-left font-mono text-[10px] tracking-mono"
              style={{ color: today ? "#facc15" : "#888885" }}
            >
              <div>{format(d, "EEE", { locale: de }).toUpperCase()}</div>
              <div className="text-nau-fg" style={{ color: today ? "#facc15" : undefined }}>
                {format(d, "d.M.")}
              </div>
            </div>
          );
        })}
      </div>

      {hasAnyAllDay && (
        <div
          className="grid border-b border-nau-line"
          style={{ gridTemplateColumns: `${HOUR_LABEL_WIDTH}px repeat(7, 1fr)` }}
        >
          <div className="px-2 py-1.5 text-right font-mono text-[9px] tracking-mono text-nau-fg-dim">
            ALL-DAY
          </div>
          {allDayPerDay.map((items, di) => (
            <div key={di} className="border-l border-nau-line px-1.5 py-1.5">
              <div className="flex flex-col gap-1">
                {items.map((e) => (
                  <div
                    key={e.id}
                    className="truncate border-l-2 px-1.5 py-0.5 font-mono text-[10px] text-nau-fg"
                    style={{
                      borderLeftColor: "#60a5fa",
                      background: "rgba(96,165,250,0.08)",
                    }}
                    title={e.title}
                  >
                    {e.title}
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="relative overflow-x-auto">
        <div
          className="relative grid"
          style={{
            gridTemplateColumns: `${HOUR_LABEL_WIDTH}px repeat(7, 1fr)`,
            height: gridHeight,
          }}
        >
          <div className="relative">
            {hours.map((h, i) => (
              <div
                key={h}
                className="absolute right-2 -translate-y-1/2 font-mono text-[9px] text-nau-fg-dim"
                style={{ top: i * rowHeight }}
              >
                {String(h).padStart(2, "0")}:00
              </div>
            ))}
          </div>

          {days.map((day, dayIdx) => (
            <DayColumn
              key={dayIdx}
              day={day}
              events={events}
              hours={hours}
              minuteToY={minuteToY}
              highlightedSlot={highlightedSlot}
              rowHeight={rowHeight}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

interface DayColumnProps {
  day: Date;
  events: ParsedEvent[];
  hours: number[];
  minuteToY: (m: number) => number;
  highlightedSlot: { start: Date; end: Date } | null;
  rowHeight: number;
}

function DayColumn({
  day,
  events,
  hours,
  minuteToY,
  highlightedSlot,
  rowHeight,
}: DayColumnProps) {
  const timed = timedEventsForDay(events, day);
  const positioned = useMemo(() => layoutDay(timed), [timed]);
  const startOfThisDay = startOfDay(day);

  const slotInDay = useMemo(() => {
    if (!highlightedSlot) return null;
    const slotDay = startOfDay(highlightedSlot.start);
    if (slotDay.getTime() !== startOfThisDay.getTime()) return null;
    const startMin = highlightedSlot.start.getHours() * 60 + highlightedSlot.start.getMinutes();
    const endMin = highlightedSlot.end.getHours() * 60 + highlightedSlot.end.getMinutes();
    return { startMin, endMin };
  }, [highlightedSlot, startOfThisDay]);

  return (
    <div className="relative border-l border-nau-line">
      {hours.map((_h, i) => (
        <div
          key={i}
          className="border-b border-dashed"
          style={{ height: rowHeight, borderColor: "rgba(255,255,255,0.04)" }}
        />
      ))}

      {slotInDay && (
        <div
          className="pointer-events-none absolute left-1 right-1 font-mono text-[9px] tracking-mono text-nau-accent"
          style={{
            top: minuteToY(slotInDay.startMin),
            height: minuteToY(slotInDay.endMin) - minuteToY(slotInDay.startMin),
            background: "rgba(250,204,21,0.14)",
            border: "1px solid #facc15",
            padding: "3px 5px",
          }}
        >
          → VORSCHLAG
        </div>
      )}

      {positioned.map((p) => {
        const top = minuteToY(p.topMinutes);
        const height = Math.max(
          18,
          minuteToY(p.topMinutes + p.durationMinutes) - top - 2,
        );
        return (
          <div
            key={p.event.id}
            className="absolute overflow-hidden font-mono text-[9px] text-nau-fg"
            style={{
              top,
              left: `calc(${p.left * 100}% + 3px)`,
              width: `calc(${p.width * 100}% - 6px)`,
              height,
              background: "rgba(255,255,255,0.05)",
              borderLeft: `2px solid ${p.hasConflict ? "#f472b6" : "#f5f5f4"}`,
              padding: "3px 5px",
              outline: p.hasConflict ? "1px solid rgba(244,114,182,0.6)" : "none",
            }}
            title={`${p.event.title}\n${formatTime(p.event.startDate)}–${formatTime(p.event.endDate)}${p.event.location ? "\n@ " + p.event.location : ""}`}
          >
            <div className="truncate">{p.event.title}</div>
            <div className="truncate text-[8px] text-nau-fg-dim">
              {formatTime(p.event.startDate)}–{formatTime(p.event.endDate)}
            </div>
            {p.hasConflict && (
              <div
                className="absolute right-1 top-0.5 font-mono text-[8px] tracking-mono"
                style={{ color: "#f472b6" }}
              >
                ⚠
              </div>
            )}
          </div>
        );
      })}

    </div>
  );
}
