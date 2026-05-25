import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from "@dnd-kit/core";
import { addDays, differenceInCalendarDays, format, parseISO, startOfDay } from "date-fns";
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

export function MoveEventsModal({ open, onClose, onMutated }: MoveEventsModalProps) {
  const [events, setEvents] = useState<CalendarEvent[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activeId, setActiveId] = useState<string | null>(null);

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

  const grouped = useMemo(() => groupByDay(events ?? [], DAYS), [events]);
  const activeEvent = useMemo(
    () => events?.find((e) => e.id === activeId) ?? null,
    [events, activeId],
  );

  const onDragStart = (e: DragStartEvent) => {
    setActiveId(String(e.active.id));
  };

  const onDragEnd = async (e: DragEndEvent) => {
    setActiveId(null);
    if (!e.over || !events) return;
    const eventId = String(e.active.id);
    const overId = String(e.over.id);
    const event = events.find((x) => x.id === eventId);
    if (!event) return;

    // overId-Format: "day-N" wobei N der Tagesindex (0..6) ab heute ist
    const dayMatch = /^day-(\d+)$/.exec(overId);
    if (!dayMatch) return;
    const dayIndex = parseInt(dayMatch[1], 10);
    const targetDay = addDays(startOfDay(new Date()), dayIndex);

    const oldStart = parseISO(event.start);
    const oldStartDay = startOfDay(oldStart);
    if (differenceInCalendarDays(targetDay, oldStartDay) === 0) {
      // Selber Tag — kein Update
      return;
    }

    const { newStart, newEnd } = shiftToDay(event, targetDay);

    // Optimistic Update
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
      // Rollback
      setEvents(previous);
      setError(err instanceof Error ? err.message : "Verschieben fehlgeschlagen.");
    }
  };

  if (!open) return null;

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
        className="flex max-h-[90vh] w-full max-w-[1200px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // VERSCHIEBEN — DRAG ZWISCHEN TAGEN
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
            <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={(e) => void onDragEnd(e)}>
              <div className="grid grid-cols-7 gap-2 min-w-[840px]">
                {grouped.map((g, i) => (
                  <DayColumn key={i} dayIndex={i} group={g} />
                ))}
              </div>
              <DragOverlay>
                {activeEvent ? <EventChip event={activeEvent} dragging /> : null}
              </DragOverlay>
            </DndContext>
          )}
        </div>

        <footer className="border-t border-nau-line px-5 py-2 font-mono text-[10px] tracking-mono text-nau-fg-dim">
          ↻ ZIEHE EVENTS ZWISCHEN TAGEN · UHRZEIT BLEIBT ERHALTEN
        </footer>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

interface DayColumnProps {
  dayIndex: number;
  group: DayGroup;
}

function DayColumn({ dayIndex, group }: DayColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id: `day-${dayIndex}` });
  return (
    <div
      ref={setNodeRef}
      className={
        "flex min-h-[260px] flex-col gap-1.5 border p-2 transition-colors " +
        (isOver
          ? "border-nau-accent bg-nau-accent/5"
          : "border-nau-line bg-white/[0.02]")
      }
    >
      <div className="mb-1 font-mono text-[10px] tracking-mono text-nau-fg-dim">
        {group.dayLabel}
      </div>
      {group.events.length === 0 ? (
        <div className="font-mono text-[9px] tracking-mono text-nau-fg-dim/40">—</div>
      ) : (
        group.events.map((ev) => <DraggableEvent key={ev.id} event={ev} />)
      )}
    </div>
  );
}

interface DraggableEventProps {
  event: CalendarEvent;
}

function DraggableEvent({ event }: DraggableEventProps) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: event.id });
  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      className={
        "cursor-grab touch-none active:cursor-grabbing " + (isDragging ? "opacity-30" : "")
      }
    >
      <EventChip event={event} />
    </div>
  );
}

interface EventChipProps {
  event: CalendarEvent;
  dragging?: boolean;
}

function EventChip({ event, dragging }: EventChipProps) {
  const start = parseISO(event.start);
  const end = parseISO(event.end);
  const time = event.isAllDay
    ? "GANZTAG"
    : `${format(start, "HH:mm")}–${format(end, "HH:mm")}`;
  return (
    <div
      className={
        "border-l-2 border-nau-line bg-nau-bg-alt px-2 py-1.5 " +
        (dragging ? "shadow-[0_8px_24px_rgba(0,0,0,0.6)]" : "")
      }
    >
      <div className="font-mono text-[9px] tracking-mono text-nau-fg-dim">{time}</div>
      <div className="truncate font-sans text-[12px] text-nau-fg">
        {event.isSeriesInstance && (
          <span className="mr-1 text-nau-fg-dim" aria-label="Serie">↻</span>
        )}
        {event.title}
      </div>
    </div>
  );
}

function shiftToDay(event: CalendarEvent, targetDay: Date): { newStart: Date; newEnd: Date } {
  const oldStart = parseISO(event.start);
  const oldEnd = parseISO(event.end);
  const duration = oldEnd.getTime() - oldStart.getTime();

  if (event.isAllDay) {
    // All-Day: einfach um die Tages-Differenz verschieben (end exklusiv bleibt erhalten)
    const dayDiff = differenceInCalendarDays(startOfDay(targetDay), startOfDay(oldStart));
    return {
      newStart: addDays(oldStart, dayDiff),
      newEnd: addDays(oldEnd, dayDiff),
    };
  }

  // Timed: Uhrzeit beibehalten, Datum auf Ziel-Tag setzen.
  const newStart = new Date(targetDay);
  newStart.setHours(oldStart.getHours(), oldStart.getMinutes(), oldStart.getSeconds(), 0);
  const newEnd = new Date(newStart.getTime() + duration);
  return { newStart, newEnd };
}

interface DayGroup {
  dayLabel: string;
  events: CalendarEvent[];
}

function groupByDay(events: CalendarEvent[], dayCount: number): DayGroup[] {
  const today = startOfDay(new Date());
  const groups: DayGroup[] = [];
  for (let i = 0; i < dayCount; i++) {
    const day = addDays(today, i);
    const dayStr = format(day, "yyyy-MM-dd");
    const dayEvents = events
      .filter((e) => format(parseISO(e.start), "yyyy-MM-dd") === dayStr)
      .sort((a, b) => parseISO(a.start).getTime() - parseISO(b.start).getTime());
    const label =
      i === 0
        ? "HEUTE"
        : i === 1
          ? "MORGEN"
          : format(day, "EEE d.M.", { locale: de }).toUpperCase();
    groups.push({ dayLabel: label, events: dayEvents });
  }
  return groups;
}
