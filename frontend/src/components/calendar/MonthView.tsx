import { useMemo } from "react";
import {
  addDays,
  eachDayOfInterval,
  endOfMonth,
  endOfWeek,
  format,
  isSameMonth,
  isToday,
  startOfMonth,
  startOfWeek,
} from "date-fns";
import { de } from "date-fns/locale";
import {
  allDayEventsForDay,
  timedEventsForDay,
  type ParsedEvent,
} from "./utils";

interface MonthViewProps {
  monthAnchor: Date;
  events: ParsedEvent[];
  onPickWeek: (weekStart: Date) => void;
}

const MAX_CHIPS = 3;

export function MonthView({ monthAnchor, events, onPickWeek }: MonthViewProps) {
  const cells = useMemo(() => {
    const first = startOfWeek(startOfMonth(monthAnchor), { weekStartsOn: 1 });
    const last = endOfWeek(endOfMonth(monthAnchor), { weekStartsOn: 1 });
    return eachDayOfInterval({ start: first, end: last });
  }, [monthAnchor]);

  const weekdayHeaders = useMemo(() => {
    const base = startOfWeek(new Date(), { weekStartsOn: 1 });
    return Array.from({ length: 7 }, (_, i) =>
      format(addDays(base, i), "EEE", { locale: de }).toUpperCase(),
    );
  }, []);

  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt">
      <div className="grid border-b border-nau-line" style={{ gridTemplateColumns: "repeat(7, 1fr)" }}>
        {weekdayHeaders.map((d) => (
          <div
            key={d}
            className="border-l border-nau-line px-2 py-2 font-mono text-[10px] tracking-mono text-nau-fg-dim first:border-l-0"
          >
            {d}
          </div>
        ))}
      </div>

      <div className="grid" style={{ gridTemplateColumns: "repeat(7, 1fr)" }}>
        {cells.map((day, i) => {
          const inMonth = isSameMonth(day, monthAnchor);
          const today = isToday(day);
          const timed = timedEventsForDay(events, day).sort(
            (a, b) => a.startDate.getTime() - b.startDate.getTime(),
          );
          const allDay = allDayEventsForDay(events, day);
          const combined = [
            ...allDay.map((e) => ({ id: e.id, label: e.title, kind: "allday" as const })),
            ...timed.map((e) => ({
              id: e.id,
              label: `${pad(e.startDate.getHours())}:${pad(e.startDate.getMinutes())} ${e.title}`,
              kind: "timed" as const,
            })),
          ];
          const visible = combined.slice(0, MAX_CHIPS);
          const more = combined.length - visible.length;
          const isWeekStart = i % 7 === 0;

          return (
            <button
              key={day.toISOString()}
              type="button"
              onClick={() => onPickWeek(startOfWeek(day, { weekStartsOn: 1 }))}
              className="group relative flex min-h-[110px] cursor-pointer flex-col gap-1 border-l border-t border-nau-line bg-transparent px-2 py-2 text-left transition-colors first:border-l-0 hover:bg-white/[0.02]"
              style={{
                borderLeftWidth: isWeekStart ? 0 : undefined,
                opacity: inMonth ? 1 : 0.4,
              }}
              aria-label={`Woche von ${format(day, "d. MMMM", { locale: de })} öffnen`}
            >
              <div
                className="font-mono text-[11px]"
                style={{ color: today ? "#facc15" : inMonth ? "#f5f5f4" : "#888885" }}
              >
                {format(day, "d")}
              </div>
              <div className="flex flex-col gap-1">
                {visible.map((it) => (
                  <span
                    key={it.id}
                    className="truncate border-l-2 px-1.5 py-0.5 font-mono text-[9px] text-nau-fg"
                    style={{
                      borderLeftColor: it.kind === "allday" ? "#60a5fa" : "#f5f5f4",
                      background:
                        it.kind === "allday"
                          ? "rgba(96,165,250,0.08)"
                          : "rgba(255,255,255,0.05)",
                    }}
                  >
                    {it.label}
                  </span>
                ))}
                {more > 0 && (
                  <span className="font-mono text-[9px] tracking-mono text-nau-fg-dim">
                    +{more} MEHR
                  </span>
                )}
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}

function pad(n: number) {
  return String(n).padStart(2, "0");
}
