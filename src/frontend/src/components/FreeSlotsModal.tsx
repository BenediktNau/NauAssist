import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { addDays, format, parseISO, startOfDay } from "date-fns";
import { de } from "date-fns/locale";
import { findFreeSlots, NotConnectedError, type FreeSlot } from "@/api/calendar";

interface FreeSlotsModalProps {
  open: boolean;
  onClose: () => void;
}

const DURATIONS: { label: string; minutes: number }[] = [
  { label: "15min", minutes: 15 },
  { label: "30min", minutes: 30 },
  { label: "45min", minutes: 45 },
  { label: "1h", minutes: 60 },
  { label: "1,5h", minutes: 90 },
  { label: "2h", minutes: 120 },
];

const STATUS_LABEL: Record<FreeSlot["status"], string> = {
  passes: "PASST",
  soft: "WEICH",
  hard: "HART",
};

export function FreeSlotsModal({ open, onClose }: FreeSlotsModalProps) {
  const [duration, setDuration] = useState(60);
  const [rangeDays, setRangeDays] = useState(7);
  const [slots, setSlots] = useState<FreeSlot[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setSlots(null);
    setError(null);
    setLoading(false);
    setDuration(60);
    setRangeDays(7);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const search = async () => {
    setLoading(true);
    setError(null);
    setSlots(null);
    const from = startOfDay(new Date());
    const to = addDays(from, rangeDays);
    try {
      const result = await findFreeSlots(from, to, duration);
      setSlots(result);
    } catch (e) {
      if (e instanceof NotConnectedError) {
        setError("Google-Kalender ist nicht verbunden.");
      } else {
        setError(e instanceof Error ? e.message : "Suche fehlgeschlagen.");
      }
    } finally {
      setLoading(false);
    }
  };

  const grouped = useMemo(() => groupByDay(slots), [slots]);

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
        className="flex max-h-[85vh] w-full max-w-[640px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // FREIE SLOTS
          </span>
          <button
            type="button"
            onClick={onClose}
            className="cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent"
          >
            SCHLIESSEN
          </button>
        </header>

        <div className="border-b border-nau-line px-5 py-4">
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <span className="mr-1 font-mono text-[10px] tracking-mono text-nau-fg-dim">DAUER</span>
            {DURATIONS.map((d) => (
              <button
                key={d.minutes}
                type="button"
                onClick={() => setDuration(d.minutes)}
                className={
                  "cursor-pointer border px-2.5 py-1 font-mono text-[10px] tracking-mono transition-colors " +
                  (duration === d.minutes
                    ? "border-nau-accent text-nau-accent"
                    : "border-nau-line text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent")
                }
              >
                {d.label}
              </button>
            ))}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="mr-1 font-mono text-[10px] tracking-mono text-nau-fg-dim">RANGE</span>
            {[3, 7, 14].map((days) => (
              <button
                key={days}
                type="button"
                onClick={() => setRangeDays(days)}
                className={
                  "cursor-pointer border px-2.5 py-1 font-mono text-[10px] tracking-mono transition-colors " +
                  (rangeDays === days
                    ? "border-nau-accent text-nau-accent"
                    : "border-nau-line text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent")
                }
              >
                {days}T
              </button>
            ))}
            <button
              type="button"
              onClick={() => void search()}
              disabled={loading}
              className="ml-auto cursor-pointer border-none bg-nau-accent px-4 py-1.5 font-mono text-[11px] uppercase tracking-mono-wide text-nau-bg hover:bg-yellow-300 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {loading ? "SUCHE …" : "SUCHEN →"}
            </button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          {error && (
            <div className="font-mono text-[10px] tracking-mono text-nau-danger">// {error}</div>
          )}
          {!error && slots === null && !loading && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
              // KLICK SUCHEN
            </div>
          )}
          {!error && slots !== null && slots.length === 0 && !loading && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
              // KEINE PASSENDEN SLOTS IM ZEITRAUM
            </div>
          )}
          {!error && grouped.map((g) => (
            <div key={g.dayLabel} className="mb-4">
              <div className="mb-1.5 font-mono text-[10px] tracking-mono text-nau-fg-dim">
                {g.dayLabel}
              </div>
              <ul className="flex flex-col gap-1">
                {g.slots.map((s, i) => (
                  <li key={`${g.dayLabel}-${i}`}>
                    <SlotRow slot={s} />
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

interface SlotRowProps {
  slot: FreeSlot;
}

function SlotRow({ slot }: SlotRowProps) {
  const start = parseISO(slot.start);
  const end = parseISO(slot.end);
  const time = `${format(start, "HH:mm")}–${format(end, "HH:mm")}`;
  const statusClass =
    slot.status === "passes"
      ? "border-nau-accent/60 text-nau-accent"
      : slot.status === "soft"
        ? "border-nau-line-strong text-nau-fg-dim"
        : "border-nau-danger/60 text-nau-danger";

  return (
    <div className="grid grid-cols-[1fr_auto_auto] items-center gap-3 border-l-2 border-nau-line bg-white/[0.02] px-3 py-1.5">
      <span className="font-mono text-[11px] tracking-mono text-nau-fg">{time}</span>
      <span className={`border px-1.5 py-0.5 font-mono text-[9px] tracking-mono ${statusClass}`}>
        {STATUS_LABEL[slot.status]}
      </span>
      <span className="font-mono text-[9px] tracking-mono text-nau-fg-dim">
        {slot.violatedBy ?? ""}
      </span>
    </div>
  );
}

interface DayGroup {
  dayLabel: string;
  slots: FreeSlot[];
}

function groupByDay(slots: FreeSlot[] | null): DayGroup[] {
  if (!slots) return [];
  const groups = new Map<string, FreeSlot[]>();
  for (const s of slots) {
    const start = parseISO(s.start);
    const key = format(start, "yyyy-MM-dd");
    const arr = groups.get(key) ?? [];
    arr.push(s);
    groups.set(key, arr);
  }
  const result: DayGroup[] = [];
  for (const [key, arr] of groups) {
    const date = parseISO(`${key}T00:00:00`);
    result.push({
      dayLabel: format(date, "EEEE d. MMM", { locale: de }).toUpperCase(),
      slots: arr,
    });
  }
  return result;
}
