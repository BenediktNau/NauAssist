import { useMemo } from "react";
import {
  addDays,
  differenceInCalendarDays,
  differenceInMinutes,
  format,
  isSameDay,
  isToday,
  startOfDay,
} from "date-fns";
import { de } from "date-fns/locale";
import {
  computeGridRange,
  formatTime,
  layoutDay,
  timedEventsForDay,
  type AllDayEvent,
  type ParsedEvent,
  type TimedEvent,
} from "./utils";
import type { PopoverState } from "./EventPopover";
import type { SlotInfo } from "@/api/types";

export interface ParsedProposal {
  slot: SlotInfo;
  startDate: Date;
  endDate: Date;
}

interface WeekViewProps {
  weekStart: Date;
  events: ParsedEvent[];
  workingHoursStart: string;
  workingHoursEnd: string;
  rowHeight?: number;
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
  proposals?: ParsedProposal[];
  onPickProposal?: (slot: SlotInfo) => void;
}

const DEFAULT_ROW_HEIGHT = 44;
const HOUR_LABEL_WIDTH = 48;
const ALL_DAY_LANE_HEIGHT = 22;
const ALL_DAY_PAD = 6;

export function WeekView({
  weekStart,
  events,
  workingHoursStart,
  workingHoursEnd,
  rowHeight = DEFAULT_ROW_HEIGHT,
  onHoverEvent,
  onClickEvent,
  proposals,
  onPickProposal,
}: WeekViewProps) {
  const days = useMemo(
    () => Array.from({ length: 7 }, (_, i) => addDays(weekStart, i)),
    [weekStart],
  );

  const weekEnd = useMemo(() => addDays(weekStart, 7), [weekStart]);

  const timedEvents = useMemo(
    () => events.filter((e): e is TimedEvent => !e.isAllDay),
    [events],
  );

  const allDayInWeek = useMemo(
    () =>
      events.filter(
        (e): e is AllDayEvent =>
          e.isAllDay && e.startDate < weekEnd && e.endDate > weekStart,
      ),
    [events, weekStart, weekEnd],
  );

  const allDayLayout = useMemo(
    () => layoutAllDay(allDayInWeek, weekStart, weekEnd),
    [allDayInWeek, weekStart, weekEnd],
  );

  // Proposals werden in die Grid-Range einbezogen, damit der Sichtbereich sie umfasst
  // auch wenn sie außerhalb der Working-Hours liegen.
  const eventsForRange = useMemo(() => {
    if (!proposals || proposals.length === 0) return timedEvents;
    const synthetic: TimedEvent[] = proposals.map((p, i) => ({
      id: `__proposal_${i}`,
      title: "",
      start: p.slot.start,
      end: p.slot.end,
      description: null,
      location: null,
      isAllDay: false,
      startDate: p.startDate,
      endDate: p.endDate,
    }));
    return [...timedEvents, ...synthetic];
  }, [timedEvents, proposals]);

  const range = useMemo(
    () => computeGridRange(workingHoursStart, workingHoursEnd, eventsForRange),
    [workingHoursStart, workingHoursEnd, eventsForRange],
  );

  const hours = useMemo(
    () => Array.from({ length: range.endHour - range.startHour }, (_, i) => range.startHour + i),
    [range],
  );

  const minutesInGrid = (range.endHour - range.startHour) * 60;
  const gridHeight = hours.length * rowHeight;
  const minuteToY = (m: number) => ((m - range.startHour * 60) / minutesInGrid) * gridHeight;

  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt">
      <div
        className="grid border-b border-nau-line"
        style={{ gridTemplateColumns: `${HOUR_LABEL_WIDTH}px repeat(7, minmax(0, 1fr))` }}
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
              <div style={{ color: today ? "#facc15" : "#f5f5f4" }}>
                {format(d, "d.M.")}
              </div>
            </div>
          );
        })}
      </div>

      {allDayLayout.lanes > 0 && (
        <div
          className="relative grid border-b border-nau-line"
          style={{
            gridTemplateColumns: `${HOUR_LABEL_WIDTH}px repeat(7, minmax(0, 1fr))`,
            gridTemplateRows: `repeat(${allDayLayout.lanes}, ${ALL_DAY_LANE_HEIGHT}px)`,
            paddingTop: ALL_DAY_PAD,
            paddingBottom: ALL_DAY_PAD,
          }}
        >
          <div
            className="self-start px-2 pt-0.5 font-mono text-[9px] tracking-mono text-nau-fg-dim"
            style={{
              gridColumn: "1 / 2",
              gridRow: `1 / span ${allDayLayout.lanes}`,
            }}
          >
            ALL-DAY
          </div>

          {/* day-column dividers so the borders run through the all-day area */}
          {days.map((_d, di) => (
            <div
              key={`div-${di}`}
              className="border-l border-nau-line"
              style={{
                gridColumn: `${di + 2} / span 1`,
                gridRow: `1 / span ${allDayLayout.lanes}`,
              }}
            />
          ))}

          {allDayLayout.items.map((it) => (
            <button
              key={it.event.id}
              type="button"
              onMouseEnter={(ev) =>
                onHoverEvent({
                  event: it.event,
                  anchor: ev.currentTarget.getBoundingClientRect(),
                  pinned: false,
                })
              }
              onMouseLeave={() => onHoverEvent(null)}
              onClick={(ev) =>
                onClickEvent({
                  event: it.event,
                  anchor: ev.currentTarget.getBoundingClientRect(),
                  pinned: true,
                })
              }
              className="mx-0.5 cursor-pointer truncate border-none border-l-[2px] px-1.5 py-0 text-left font-mono text-[10px] leading-[20px] text-nau-fg"
              style={{
                gridColumn: `${it.colStart} / ${it.colEnd}`,
                gridRow: it.lane + 1,
                background: "rgba(96,165,250,0.10)",
                borderLeftColor: "#60a5fa",
              }}
              title={it.event.title}
            >
              {it.event.title}
            </button>
          ))}
        </div>
      )}

      <div className="relative overflow-x-auto">
        <div
          className="relative grid"
          style={{
            gridTemplateColumns: `${HOUR_LABEL_WIDTH}px repeat(7, minmax(0, 1fr))`,
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
              rowHeight={rowHeight}
              onHoverEvent={onHoverEvent}
              onClickEvent={onClickEvent}
              proposals={proposals}
              onPickProposal={onPickProposal}
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
  rowHeight: number;
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
  proposals?: ParsedProposal[];
  onPickProposal?: (slot: SlotInfo) => void;
}

function DayColumn({
  day,
  events,
  hours,
  minuteToY,
  rowHeight,
  onHoverEvent,
  onClickEvent,
  proposals,
  onPickProposal,
}: DayColumnProps) {
  const timed = timedEventsForDay(events, day);
  const positioned = useMemo(() => layoutDay(timed), [timed]);
  const proposalsForDay = useMemo(
    () => (proposals ?? []).filter((p) => isSameDay(p.startDate, day)),
    [proposals, day],
  );

  return (
    <div className="relative border-l border-nau-line">
      {hours.map((_h, i) => (
        <div
          key={i}
          className="border-b border-dashed"
          style={{ height: rowHeight, borderColor: "rgba(255,255,255,0.04)" }}
        />
      ))}

      {positioned.map((p) => {
        const top = minuteToY(p.topMinutes);
        const height = Math.max(
          18,
          minuteToY(p.topMinutes + p.durationMinutes) - top - 2,
        );
        return (
          <button
            key={p.event.id}
            type="button"
            onMouseEnter={(ev) =>
              onHoverEvent({
                event: p.event,
                anchor: ev.currentTarget.getBoundingClientRect(),
                pinned: false,
              })
            }
            onMouseLeave={() => onHoverEvent(null)}
            onClick={(ev) =>
              onClickEvent({
                event: p.event,
                anchor: ev.currentTarget.getBoundingClientRect(),
                pinned: true,
              })
            }
            className="absolute cursor-pointer overflow-hidden border-none p-0 text-left font-mono text-[9px] text-nau-fg"
            style={{
              top,
              left: `calc(${p.left * 100}% + 3px)`,
              width: `calc(${p.width * 100}% - 6px)`,
              height,
              background: "rgba(255,255,255,0.05)",
              borderLeft: `2px solid ${p.hasConflict ? "#f472b6" : "#f5f5f4"}`,
              outline: p.hasConflict ? "1px solid rgba(244,114,182,0.6)" : "none",
              padding: "3px 5px",
            }}
            title={p.event.title}
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
          </button>
        );
      })}

      {proposalsForDay.map((p, i) => {
        const dayStart = startOfDay(p.startDate);
        const topMin = differenceInMinutes(p.startDate, dayStart);
        const durationMin = Math.max(15, differenceInMinutes(p.endDate, p.startDate));
        const top = minuteToY(topMin);
        const height = Math.max(20, minuteToY(topMin + durationMin) - top - 2);
        const label = p.slot.note ?? "VORSCHLAG";
        const titleText = `Vorschlag · ${formatTime(p.startDate)}–${formatTime(p.endDate)}${
          p.slot.note ? ` · ${p.slot.note}` : ""
        }${onPickProposal ? "\n(Klicken zum Annehmen)" : ""}`;
        return (
          <button
            key={`proposal-${i}`}
            type="button"
            onClick={() => onPickProposal?.(p.slot)}
            disabled={!onPickProposal}
            className="absolute z-10 cursor-pointer overflow-hidden border-2 border-dashed p-0 text-left font-mono text-[9px] disabled:cursor-default"
            style={{
              top,
              left: "3px",
              right: "3px",
              height,
              background: "rgba(250,204,21,0.10)",
              borderColor: "#facc15",
              color: "#facc15",
              padding: "2px 5px",
            }}
            title={titleText}
          >
            <div className="truncate font-bold tracking-mono">▸ {label}</div>
            <div className="truncate text-[8px] opacity-80">
              {formatTime(p.startDate)}–{formatTime(p.endDate)}
            </div>
          </button>
        );
      })}
    </div>
  );
}

interface AllDayPositioned {
  event: AllDayEvent;
  lane: number;
  /** CSS grid column-start (1-indexed; col 1 = label, col 2 = Mo) */
  colStart: number;
  /** CSS grid column-end (exclusive) */
  colEnd: number;
}

interface AllDayLayout {
  lanes: number;
  items: AllDayPositioned[];
}

function layoutAllDay(
  events: AllDayEvent[],
  weekStart: Date,
  weekEnd: Date,
): AllDayLayout {
  if (events.length === 0) return { lanes: 0, items: [] };

  const sorted = [...events].sort(
    (a, b) => a.startDate.getTime() - b.startDate.getTime(),
  );
  const laneEnds: number[] = [];
  const items: AllDayPositioned[] = [];

  for (const e of sorted) {
    const start = e.startDate.getTime();
    const end = e.endDate.getTime();
    let lane = laneEnds.findIndex((t) => t <= start);
    if (lane === -1) {
      lane = laneEnds.length;
      laneEnds.push(end);
    } else {
      laneEnds[lane] = end;
    }
    const { colStart, colEnd } = spanColumns(e, weekStart, weekEnd);
    items.push({ event: e, lane, colStart, colEnd });
  }
  return { lanes: laneEnds.length, items };
}

/**
 * CSS-Grid-Spalten: 1 = ALL-DAY-Label, 2..8 = Mo..So.
 */
function spanColumns(
  event: AllDayEvent,
  weekStart: Date,
  weekEnd: Date,
): { colStart: number; colEnd: number } {
  const inclusiveEnd = addDays(event.endDate, -1);
  const startDayIdx =
    event.startDate < weekStart
      ? 0
      : differenceInCalendarDays(startOfDay(event.startDate), weekStart);
  const endDayIdx =
    inclusiveEnd >= weekEnd
      ? 6
      : differenceInCalendarDays(startOfDay(inclusiveEnd), weekStart);

  return {
    colStart: Math.max(0, startDayIdx) + 2,
    colEnd: Math.min(6, endDayIdx) + 3,
  };
}
