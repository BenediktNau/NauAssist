import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { addDays, differenceInMinutes, format, parseISO, startOfDay } from "date-fns";
import { de } from "date-fns/locale";
import {
  getCalendarRange,
  NotConnectedError,
  updateEvent,
  type CalendarEvent,
} from "@/api/calendar";

interface MoveEventsModalProps {
  open: boolean;
  onClose: () => void;
  onMutated: () => void;
}

const DAYS = 7;
const START_HOUR = 6;
const END_HOUR = 22; // exklusiv: 22:00 ist letzte Stunde
const HOUR_PX = 44;
const ALL_DAY_HEIGHT = 28;

export function MoveEventsModal({ open, onClose, onMutated }: MoveEventsModalProps) {
  const [events, setEvents] = useState<CalendarEvent[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    setEvents(null);
    const from = startOfDay(new Date());
    const to = addDays(from, DAYS);
    getCalendarRange(from, to)
      .then((evs) => {
        if (!cancelled) setEvents(evs);
      })
      .catch((e) => {
        if (cancelled) return;
        if (e instanceof NotConnectedError) {
          setError("Google-Kalender ist nicht verbunden.");
        } else {
          setError(e instanceof Error ? e.message : "Laden fehlgeschlagen.");
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const today = useMemo(() => startOfDay(new Date()), []);
  const days = useMemo(
    () => Array.from({ length: DAYS }, (_, i) => addDays(today, i)),
    [today],
  );

  const onDragEnd = async (e: DragEndEvent) => {
    if (!e.over || !events) return;
    const eventId = String(e.active.id);
    const overId = String(e.over.id);
    const event = events.find((x) => x.id === eventId);
    if (!event) return;

    let newStart: Date;
    let newEnd: Date;

    const timed = /^day-(\d+)-hour-(\d+)$/.exec(overId);
    const allDay = /^day-(\d+)-allday$/.exec(overId);

    if (timed) {
      if (event.isAllDay) return; // All-Day-Event kann nicht ins Timed-Grid
      const dayIndex = parseInt(timed[1], 10);
      const hour = parseInt(timed[2], 10);
      const targetDay = days[dayIndex];
      const oldStart = parseISO(event.start);
      const oldEnd = parseISO(event.end);
      const durationMin = differenceInMinutes(oldEnd, oldStart);

      newStart = new Date(targetDay);
      newStart.setHours(hour, oldStart.getMinutes(), 0, 0);
      newEnd = new Date(newStart.getTime() + durationMin * 60 * 1000);
    } else if (allDay) {
      if (!event.isAllDay) return; // Timed-Event nicht ins All-Day-Strip
      const dayIndex = parseInt(allDay[1], 10);
      const targetDay = days[dayIndex];
      const oldStart = parseISO(event.start);
      const oldEnd = parseISO(event.end);
      const durationDays = Math.round(
        (oldEnd.getTime() - oldStart.getTime()) / (24 * 3600 * 1000),
      );
      newStart = targetDay;
      newEnd = addDays(targetDay, Math.max(1, durationDays));
    } else {
      return;
    }

    // Wenn nichts ändert, abbrechen
    const oldStart = parseISO(event.start);
    if (oldStart.getTime() === newStart.getTime()) return;

    const previous = events;
    setEvents((prev) =>
      prev?.map((x) =>
        x.id === eventId ? { ...x, start: newStart.toISOString(), end: newEnd.toISOString() } : x,
      ) ?? null,
    );

    try {
      await updateEvent(event.id, { start: newStart, end: newEnd }, "instance");
      onMutated();
    } catch (err) {
      setEvents(previous);
      setError(err instanceof Error ? err.message : "Verschieben fehlgeschlagen.");
    }
  };

  if (!open) return null;

  const allDayEvents = (events ?? []).filter((e) => e.isAllDay);
  const timedEvents = (events ?? []).filter((e) => !e.isAllDay);

  const content = (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-black/60 p-4"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="flex max-h-[92vh] w-full max-w-[1200px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)]"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // VERSCHIEBEN — DRAG AUF NEUEN TAG/UHRZEIT
          </span>
          <button
            type="button"
            onClick={onClose}
            className="cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent"
          >
            SCHLIESSEN
          </button>
        </header>

        <div className="flex-1 overflow-auto px-5 py-4">
          {loading && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">// LADE …</div>
          )}
          {error && (
            <div className="mb-3 font-mono text-[10px] tracking-mono text-nau-danger">
              // {error}
            </div>
          )}
          {!loading && events && (
            <DndContext sensors={sensors} onDragEnd={(e) => void onDragEnd(e)}>
              <Grid
                days={days}
                allDayEvents={allDayEvents}
                timedEvents={timedEvents}
              />
            </DndContext>
          )}
        </div>

        <footer className="border-t border-nau-line px-5 py-2 font-mono text-[10px] tracking-mono text-nau-fg-dim">
          ↻ ZIEHE EVENTS ZWISCHEN TAGEN ODER STUNDEN · SNAP AUF VOLLE STUNDE · 6–22 UHR
        </footer>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

interface GridProps {
  days: Date[];
  allDayEvents: CalendarEvent[];
  timedEvents: CalendarEvent[];
}

function Grid({ days, allDayEvents, timedEvents }: GridProps) {
  const hours = useMemo(
    () => Array.from({ length: END_HOUR - START_HOUR }, (_, i) => START_HOUR + i),
    [],
  );

  return (
    <div className="min-w-[840px]">
      {/* Day-Header */}
      <div className="grid grid-cols-[60px_repeat(7,1fr)] border-b border-nau-line">
        <div />
        {days.map((d, i) => (
          <div
            key={i}
            className="border-l border-nau-line px-2 py-1.5 font-mono text-[10px] tracking-mono text-nau-fg-dim"
          >
            {i === 0 ? "HEUTE" : i === 1 ? "MORGEN" : format(d, "EEE d.M.", { locale: de }).toUpperCase()}
          </div>
        ))}
      </div>

      {/* All-Day-Strip */}
      <div
        className="grid grid-cols-[60px_repeat(7,1fr)] border-b border-nau-line"
        style={{ minHeight: ALL_DAY_HEIGHT + 8 }}
      >
        <div className="px-2 py-1 font-mono text-[9px] tracking-mono text-nau-fg-dim">ALL-DAY</div>
        {days.map((d, i) => (
          <AllDayCell key={i} dayIndex={i} day={d} events={allDayEvents} />
        ))}
      </div>

      {/* Timed Grid */}
      <div
        className="grid grid-cols-[60px_repeat(7,1fr)] relative"
        style={{ height: hours.length * HOUR_PX }}
      >
        {/* Stundenlabels */}
        <div className="relative">
          {hours.map((h, i) => (
            <div
              key={h}
              className="absolute right-2 -translate-y-1/2 font-mono text-[9px] text-nau-fg-dim"
              style={{ top: i * HOUR_PX }}
            >
              {String(h).padStart(2, "0")}:00
            </div>
          ))}
        </div>

        {/* Tag-Spalten mit Hour-Droppables und absolut platzierten Events */}
        {days.map((d, di) => (
          <DayColumn
            key={di}
            dayIndex={di}
            day={d}
            timedEvents={timedEvents}
            hours={hours}
          />
        ))}
      </div>
    </div>
  );
}

interface AllDayCellProps {
  dayIndex: number;
  day: Date;
  events: CalendarEvent[];
}

function AllDayCell({ dayIndex, day, events }: AllDayCellProps) {
  const { setNodeRef, isOver } = useDroppable({ id: `day-${dayIndex}-allday` });
  const dayStr = format(day, "yyyy-MM-dd");
  const myEvents = events.filter((e) => format(parseISO(e.start), "yyyy-MM-dd") === dayStr);

  return (
    <div
      ref={setNodeRef}
      className={
        "flex flex-col gap-0.5 border-l border-nau-line px-1 py-1 transition-colors " +
        (isOver ? "bg-nau-accent/10" : "")
      }
    >
      {myEvents.map((e) => (
        <DraggableEvent key={e.id} event={e} kind="allday" />
      ))}
    </div>
  );
}

interface DayColumnProps {
  dayIndex: number;
  day: Date;
  timedEvents: CalendarEvent[];
  hours: number[];
}

function DayColumn({ dayIndex, day, timedEvents, hours }: DayColumnProps) {
  const dayStr = format(day, "yyyy-MM-dd");
  const myEvents = timedEvents.filter((e) => format(parseISO(e.start), "yyyy-MM-dd") === dayStr);

  return (
    <div className="relative border-l border-nau-line">
      {hours.map((h) => (
        <HourCell key={h} dayIndex={dayIndex} hour={h} />
      ))}
      {myEvents.map((e) => (
        <PositionedEvent key={e.id} event={e} />
      ))}
    </div>
  );
}

interface HourCellProps {
  dayIndex: number;
  hour: number;
}

function HourCell({ dayIndex, hour }: HourCellProps) {
  const { setNodeRef, isOver } = useDroppable({ id: `day-${dayIndex}-hour-${hour}` });
  return (
    <div
      ref={setNodeRef}
      className={
        "border-b border-dashed transition-colors " +
        (isOver ? "bg-nau-accent/10" : "")
      }
      style={{ height: HOUR_PX, borderColor: "rgba(255,255,255,0.04)" }}
    />
  );
}

interface PositionedEventProps {
  event: CalendarEvent;
}

function PositionedEvent({ event }: PositionedEventProps) {
  const start = parseISO(event.start);
  const end = parseISO(event.end);
  const startMin = start.getHours() * 60 + start.getMinutes() - START_HOUR * 60;
  const durationMin = differenceInMinutes(end, start);

  const top = (startMin / 60) * HOUR_PX;
  const height = Math.max(20, (durationMin / 60) * HOUR_PX - 2);

  return (
    <div
      className="absolute left-0.5 right-0.5"
      style={{ top, height }}
    >
      <DraggableEvent event={event} kind="timed" heightPx={height} />
    </div>
  );
}

interface DraggableEventProps {
  event: CalendarEvent;
  kind: "timed" | "allday";
  heightPx?: number;
}

function DraggableEvent({ event, kind, heightPx }: DraggableEventProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: event.id,
  });

  const style: React.CSSProperties = {
    transform: transform ? `translate3d(${transform.x}px, ${transform.y}px, 0)` : undefined,
    zIndex: isDragging ? 50 : undefined,
    boxShadow: isDragging ? "0 8px 24px rgba(0,0,0,0.6)" : undefined,
    height: kind === "timed" ? heightPx : undefined,
  };

  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      style={style}
      className={
        "relative cursor-grab touch-none select-none overflow-hidden active:cursor-grabbing " +
        (kind === "allday"
          ? "border-l-2 border-nau-blue bg-nau-blue/10 px-2 py-0.5"
          : "border-l-2 border-nau-line bg-nau-bg-alt px-2 py-1")
      }
    >
      {kind === "timed" ? (
        <>
          <div className="font-mono text-[9px] tracking-mono text-nau-fg-dim">
            {format(parseISO(event.start), "HH:mm")}–{format(parseISO(event.end), "HH:mm")}
          </div>
          <div className="truncate font-sans text-[12px] text-nau-fg">
            {event.isSeriesInstance && (
              <span className="mr-1 text-nau-fg-dim" aria-label="Serie">↻</span>
            )}
            {event.title}
          </div>
        </>
      ) : (
        <div className="truncate font-mono text-[10px] tracking-mono text-nau-fg">
          {event.isSeriesInstance && (
            <span className="mr-1 text-nau-fg-dim" aria-label="Serie">↻</span>
          )}
          {event.title}
        </div>
      )}
    </div>
  );
}
