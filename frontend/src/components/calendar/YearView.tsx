import { useMemo } from "react";
import {
  addMonths,
  eachDayOfInterval,
  endOfMonth,
  endOfWeek,
  format,
  isSameMonth,
  isToday,
  startOfMonth,
  startOfWeek,
  startOfYear,
} from "date-fns";
import { de } from "date-fns/locale";
import type { ParsedEvent } from "./utils";

interface YearViewProps {
  yearAnchor: Date;
  events: ParsedEvent[];
  onPickMonth: (monthAnchor: Date) => void;
  columns?: number;
}

export function YearView({
  yearAnchor,
  events,
  onPickMonth,
  columns = 4,
}: YearViewProps) {
  const months = useMemo(() => {
    const yStart = startOfYear(yearAnchor);
    return Array.from({ length: 12 }, (_, i) => addMonths(yStart, i));
  }, [yearAnchor]);

  const weekdayHeaders = useMemo(() => ["M", "D", "M", "D", "F", "S", "S"], []);

  const density = useMemo(() => {
    const map = new Map<string, number>();
    for (const e of events) {
      const k = e.startDate.toISOString().slice(0, 10);
      map.set(k, (map.get(k) ?? 0) + 1);
    }
    return map;
  }, [events]);

  return (
    <div className="grid gap-3" style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}>
      {months.map((m) => (
        <button
          key={m.toISOString()}
          type="button"
          onClick={() => onPickMonth(m)}
          className="group cursor-pointer rounded-[4px] border border-nau-line bg-nau-bg-alt p-3 text-left transition-colors hover:border-nau-line-strong"
        >
          <div className="mb-2 flex items-center justify-between">
            <span className="font-mono text-[11px] tracking-mono text-nau-fg">
              {format(m, "MMMM", { locale: de }).toUpperCase()}
            </span>
            <span className="font-mono text-[9px] tracking-mono text-nau-fg-dim">
              {format(m, "yyyy")}
            </span>
          </div>
          <MiniMonth
            month={m}
            density={density}
            weekdayHeaders={weekdayHeaders}
          />
        </button>
      ))}
    </div>
  );
}

interface MiniMonthProps {
  month: Date;
  density: Map<string, number>;
  weekdayHeaders: string[];
}

function MiniMonth({ month, density, weekdayHeaders }: MiniMonthProps) {
  const cells = useMemo(() => {
    const first = startOfWeek(startOfMonth(month), { weekStartsOn: 1 });
    const last = endOfWeek(endOfMonth(month), { weekStartsOn: 1 });
    return eachDayOfInterval({ start: first, end: last });
  }, [month]);

  return (
    <div>
      <div
        className="mb-1 grid"
        style={{ gridTemplateColumns: "repeat(7, 1fr)" }}
      >
        {weekdayHeaders.map((d, i) => (
          <span
            key={i}
            className="text-center font-mono text-[8px] tracking-mono text-nau-fg-dim"
          >
            {d}
          </span>
        ))}
      </div>
      <div
        className="grid gap-px"
        style={{ gridTemplateColumns: "repeat(7, 1fr)" }}
      >
        {cells.map((day) => {
          const inMonth = isSameMonth(day, month);
          const key = day.toISOString().slice(0, 10);
          const n = density.get(key) ?? 0;
          const intensity = Math.min(1, n / 4);
          const today = isToday(day);
          const bg = inMonth && n > 0
            ? `rgba(250,204,21,${0.12 + intensity * 0.45})`
            : inMonth
              ? "rgba(255,255,255,0.025)"
              : "transparent";
          return (
            <div
              key={day.toISOString()}
              className="aspect-square text-center font-mono text-[8px] leading-none"
              style={{
                background: bg,
                color: today ? "#facc15" : inMonth ? "#f5f5f4" : "#444444",
                paddingTop: 3,
                border: today ? "1px solid #facc15" : "none",
              }}
            >
              {format(day, "d")}
            </div>
          );
        })}
      </div>
    </div>
  );
}
