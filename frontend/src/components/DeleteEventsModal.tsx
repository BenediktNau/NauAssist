import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { addDays, format, parseISO, startOfDay } from "date-fns";
import { de } from "date-fns/locale";
import {
  deleteEvent,
  getCalendarRange,
  NotConnectedError,
  type CalendarEvent,
} from "@/api/calendar";

interface DeleteEventsModalProps {
  open: boolean;
  onClose: () => void;
  /** Wird aufgerufen, wenn mindestens ein Event tatsächlich gelöscht wurde. */
  onMutated: () => void;
}

const DAYS = 7;

export function DeleteEventsModal({ open, onClose, onMutated }: DeleteEventsModalProps) {
  const [events, setEvents] = useState<CalendarEvent[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    setEvents(null);
    setSelected(new Set());
    setConfirming(false);
    setDeleting(false);
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

  const toggle = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const doDelete = async () => {
    if (selected.size === 0 || deleting) return;
    setDeleting(true);
    setError(null);
    const ids = Array.from(selected);
    const results = await Promise.allSettled(ids.map((id) => deleteEvent(id, "instance")));
    const failed = results.filter((r) => r.status === "rejected").length;
    const ok = ids.length - failed;
    if (ok > 0) onMutated();
    if (failed > 0) {
      setError(`${failed} von ${ids.length} konnten nicht gelöscht werden.`);
      setDeleting(false);
      setConfirming(false);
      // Liste neu laden, damit Erfolgs-Items verschwinden
      reloadEvents().catch(() => {});
      setSelected(new Set());
    } else {
      onClose();
    }
  };

  const reloadEvents = async () => {
    const from = startOfDay(new Date());
    const to = addDays(from, DAYS);
    const evs = await getCalendarRange(from, to);
    setEvents(evs);
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
        className="flex max-h-[90vh] w-full max-w-[720px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // TERMINE LÖSCHEN — NÄCHSTE 7 TAGE
          </span>
          <button
            type="button"
            onClick={onClose}
            className="cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent"
          >
            SCHLIESSEN
          </button>
        </header>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          {loading && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">// LADE …</div>
          )}
          {error && (
            <div className="mb-3 font-mono text-[10px] tracking-mono text-nau-danger">
              // {error}
            </div>
          )}
          {!loading && !error && events && events.length === 0 && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
              // KEINE TERMINE IN DEN NÄCHSTEN 7 TAGEN
            </div>
          )}
          {!loading && events && grouped.map((g) => (
            <div key={g.dayLabel} className="mb-4">
              <div className="mb-1.5 font-mono text-[10px] tracking-mono text-nau-fg-dim">
                {g.dayLabel}
              </div>
              {g.events.length === 0 ? (
                <div className="ml-2 font-mono text-[9px] tracking-mono text-nau-fg-dim/60">
                  —
                </div>
              ) : (
                <ul className="flex flex-col gap-1">
                  {g.events.map((ev) => (
                    <li key={ev.id}>
                      <EventRow
                        event={ev}
                        checked={selected.has(ev.id)}
                        onToggle={() => toggle(ev.id)}
                      />
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ))}
        </div>

        <footer className="flex items-center justify-between border-t border-nau-line px-5 py-3">
          <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
            {selected.size} AUSGEWÄHLT
          </span>
          {confirming ? (
            <div className="flex items-center gap-2">
              <span className="font-mono text-[10px] tracking-mono text-nau-fg">
                {selected.size} LÖSCHEN?
              </span>
              <button
                type="button"
                onClick={() => setConfirming(false)}
                disabled={deleting}
                className="cursor-pointer border border-nau-line bg-transparent px-3 py-1.5 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent disabled:opacity-50"
              >
                ABBRECHEN
              </button>
              <button
                type="button"
                onClick={() => void doDelete()}
                disabled={deleting}
                className="cursor-pointer border-none bg-nau-danger px-3 py-1.5 font-mono text-[10px] uppercase tracking-mono-wide text-nau-bg hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {deleting ? "LÖSCHE …" : "JA, LÖSCHEN"}
              </button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setConfirming(true)}
              disabled={selected.size === 0 || deleting}
              className="cursor-pointer border-none bg-nau-danger px-4 py-2 font-mono text-[11px] uppercase tracking-mono-wide text-nau-bg hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-30"
            >
              LÖSCHEN →
            </button>
          )}
        </footer>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

interface EventRowProps {
  event: CalendarEvent;
  checked: boolean;
  onToggle: () => void;
}

function EventRow({ event, checked, onToggle }: EventRowProps) {
  const start = parseISO(event.start);
  const end = parseISO(event.end);
  const time = event.isAllDay
    ? "GANZTAG"
    : `${format(start, "HH:mm")}–${format(end, "HH:mm")}`;

  return (
    <label
      className={
        "flex cursor-pointer items-center gap-3 border-l-2 px-3 py-2 transition-colors " +
        (checked
          ? "border-nau-accent bg-white/[0.04]"
          : "border-nau-line bg-white/[0.02] hover:bg-white/[0.04]")
      }
    >
      <input
        type="checkbox"
        checked={checked}
        onChange={onToggle}
        className="h-3.5 w-3.5 accent-nau-accent"
      />
      <span className="w-[110px] font-mono text-[10px] tracking-mono text-nau-fg-dim">
        {time}
      </span>
      <span className="flex-1 truncate font-sans text-[13px] text-nau-fg">
        {event.isSeriesInstance && (
          <span className="mr-1 text-nau-fg-dim" aria-label="Serie">↻</span>
        )}
        {event.title}
      </span>
    </label>
  );
}

interface DayGroup {
  dayLabel: string;
  events: CalendarEvent[];
}

/** Gruppiert Events nach Tag und füllt leere Tage über den DAYS-Range. */
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
        ? `HEUTE · ${format(day, "EEE d. MMM", { locale: de }).toUpperCase()}`
        : i === 1
          ? `MORGEN · ${format(day, "EEE d. MMM", { locale: de }).toUpperCase()}`
          : format(day, "EEEE d. MMM", { locale: de }).toUpperCase();
    groups.push({ dayLabel: label, events: dayEvents });
  }
  return groups;
}
