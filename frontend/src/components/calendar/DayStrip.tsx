import { useMemo } from "react";
import { addDays, format, isSameDay, isToday } from "date-fns";
import { de } from "date-fns/locale";
import { allDayEventsForDay, timedEventsForDay, type ParsedEvent } from "./utils";

interface DayStripProps {
  /** Montag der angezeigten Woche. */
  weekStart: Date;
  /** Aktuell gewählter Tag. */
  selected: Date;
  /** Termine der Woche — für die Punkt-Marker unter den Tagen. */
  events: ParsedEvent[];
  onSelect: (day: Date) => void;
}

/**
 * Kompakte Wochenleiste zum schnellen Tageswechsel (Tagesansicht).
 * Ein Tipp auf einen Tag wählt ihn aus — ersetzt umständliches Vor-/Zurück.
 */
export function DayStrip({ weekStart, selected, events, onSelect }: DayStripProps) {
  const days = useMemo(
    () => Array.from({ length: 7 }, (_, i) => addDays(weekStart, i)),
    [weekStart],
  );

  return (
    <div className="grid grid-cols-7 gap-1">
      {days.map((d) => {
        const isSelected = isSameDay(d, selected);
        const today = isToday(d);
        const hasEvents =
          timedEventsForDay(events, d).length > 0 ||
          allDayEventsForDay(events, d).length > 0;

        return (
          <button
            key={d.toISOString()}
            type="button"
            onClick={() => onSelect(d)}
            aria-pressed={isSelected}
            className="flex cursor-pointer flex-col items-center gap-1 rounded-[4px] border-none bg-transparent py-1.5 transition-colors"
          >
            <span
              className="font-mono text-[9px] tracking-mono"
              style={{ color: today ? "#facc15" : "#888885" }}
            >
              {format(d, "EEEEEE", { locale: de }).toUpperCase()}
            </span>
            <span
              className="flex h-7 w-7 items-center justify-center rounded-full font-mono text-[13px] tabular-nums transition-colors"
              style={{
                background: isSelected ? "#facc15" : "transparent",
                color: isSelected ? "#0a0a0a" : today ? "#facc15" : "#f5f5f4",
                fontWeight: isSelected || today ? 600 : 400,
              }}
            >
              {format(d, "d")}
            </span>
            <span
              className="h-1 w-1 rounded-full"
              style={{
                background: hasEvents
                  ? isSelected
                    ? "#facc15"
                    : "#60a5fa"
                  : "transparent",
              }}
            />
          </button>
        );
      })}
    </div>
  );
}
