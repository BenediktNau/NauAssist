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
  type AllDayEvent,
  type ParsedEvent,
  type TimedEvent,
} from "./utils";
import type { PopoverState } from "./EventPopover";

interface MonthViewProps {
  monthAnchor: Date;
  events: ParsedEvent[];
  onPickWeek: (weekStart: Date) => void;
  minCellHeight?: number;
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
}

const MAX_CHIPS = 3;

export function MonthView({
  monthAnchor,
  events,
  onPickWeek,
  minCellHeight = 110,
  onHoverEvent,
  onClickEvent,
}: MonthViewProps) {
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
      <div className="grid border-b border-nau-line" style={{ gridTemplateColumns: "repeat(7, minmax(0, 1fr))" }}>
        {weekdayHeaders.map((d) => (
          <div
            key={d}
            className="border-l border-nau-line px-2 py-2 font-mono text-[10px] tracking-mono text-nau-fg-dim first:border-l-0"
          >
            {d}
          </div>
        ))}
      </div>

      <div className="grid" style={{ gridTemplateColumns: "repeat(7, minmax(0, 1fr))" }}>
        {cells.map((day, i) => {
          const inMonth = isSameMonth(day, monthAnchor);
          const today = isToday(day);
          const timed: TimedEvent[] = timedEventsForDay(events, day).sort(
            (a, b) => a.startDate.getTime() - b.startDate.getTime(),
          );
          const allDay: AllDayEvent[] = allDayEventsForDay(events, day);
          const combined: ParsedEvent[] = [...allDay, ...timed];
          const visible = combined.slice(0, MAX_CHIPS);
          const more = combined.length - visible.length;
          const isWeekStart = i % 7 === 0;

          return (
            <div
              key={day.toISOString()}
              className="group relative flex flex-col gap-1 border-l border-t border-nau-line bg-transparent px-2 py-2 transition-colors first:border-l-0 hover:bg-white/[0.02]"
              style={{
                minHeight: minCellHeight,
                borderLeftWidth: isWeekStart ? 0 : undefined,
                opacity: inMonth ? 1 : 0.4,
              }}
            >
              <button
                type="button"
                onClick={() => onPickWeek(startOfWeek(day, { weekStartsOn: 1 }))}
                className="cursor-pointer border-none bg-transparent p-0 text-left font-mono text-[11px] hover:text-nau-accent"
                style={{ color: today ? "#facc15" : inMonth ? "#f5f5f4" : "#888885" }}
                aria-label={`Woche von ${format(day, "d. MMMM", { locale: de })} öffnen`}
              >
                {format(day, "d")}
              </button>
              <div className="flex min-w-0 flex-col gap-1">
                {visible.map((e) => (
                  <Chip
                    key={e.id}
                    event={e}
                    onHoverEvent={onHoverEvent}
                    onClickEvent={onClickEvent}
                  />
                ))}
                {more > 0 && (
                  <span className="font-mono text-[9px] tracking-mono text-nau-fg-dim">
                    +{more} MEHR
                  </span>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

interface ChipProps {
  event: ParsedEvent;
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
}

function Chip({ event, onHoverEvent, onClickEvent }: ChipProps) {
  const label = event.isAllDay
    ? event.title
    : `${pad(event.startDate.getHours())}:${pad(event.startDate.getMinutes())} ${event.title}`;
  return (
    <button
      type="button"
      onMouseEnter={(ev) =>
        onHoverEvent({
          event,
          anchor: ev.currentTarget.getBoundingClientRect(),
          pinned: false,
        })
      }
      onMouseLeave={() => onHoverEvent(null)}
      onClick={(ev) => {
        ev.stopPropagation();
        onClickEvent({
          event,
          anchor: ev.currentTarget.getBoundingClientRect(),
          pinned: true,
        });
      }}
      className="block w-full cursor-pointer truncate border-none border-l-2 bg-transparent px-1.5 py-0.5 text-left font-mono text-[9px] text-nau-fg"
      style={{
        borderLeftColor: event.isAllDay ? "#60a5fa" : "#f5f5f4",
        background: event.isAllDay
          ? "rgba(96,165,250,0.08)"
          : "rgba(255,255,255,0.05)",
      }}
      title={event.title}
    >
      {label}
    </button>
  );
}

function pad(n: number) {
  return String(n).padStart(2, "0");
}
