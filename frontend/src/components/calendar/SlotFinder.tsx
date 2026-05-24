import { useState } from "react";
import { addDays, format, parseISO } from "date-fns";
import { de } from "date-fns/locale";
import { findFreeSlots, NotConnectedError, type FreeSlot } from "@/api/calendar";
import { formatTime } from "./utils";

interface SlotFinderProps {
  defaultDurationMinutes: number;
  searchHorizonDays: number;
  onPickSlot: (slot: FreeSlot | null) => void;
  selectedSlotKey: string | null;
}

function toDateInputValue(d: Date): string {
  return format(d, "yyyy-MM-dd");
}

export function SlotFinder({
  defaultDurationMinutes,
  searchHorizonDays,
  onPickSlot,
  selectedSlotKey,
}: SlotFinderProps) {
  const today = new Date();
  const horizonEnd = addDays(today, Math.max(1, searchHorizonDays));

  const [from, setFrom] = useState(toDateInputValue(today));
  const [to, setTo] = useState(toDateInputValue(horizonEnd));
  const [duration, setDuration] = useState(String(defaultDurationMinutes));
  const [slots, setSlots] = useState<FreeSlot[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    setLoading(true);
    setError(null);
    setSlots(null);
    try {
      const fromDate = new Date(`${from}T00:00:00`);
      const toDate = new Date(`${to}T23:59:59`);
      const durMin = parseInt(duration, 10);
      if (!Number.isFinite(durMin) || durMin <= 0) {
        throw new Error("Dauer muss eine positive Zahl sein.");
      }
      const result = await findFreeSlots(fromDate, toDate, durMin);
      setSlots(result);
    } catch (e) {
      if (e instanceof NotConnectedError) {
        setError("Google-Kalender ist nicht verbunden.");
      } else {
        setError(String((e as Error).message ?? e));
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex flex-col gap-3 rounded-[4px] border border-nau-line bg-nau-bg-alt p-4">
      <div className="font-mono text-[11px] tracking-mono-wide text-nau-accent">
        // SLOT-FINDER
      </div>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">VON</span>
        <input
          type="date"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
          className="border border-nau-line bg-white/[0.03] px-2.5 py-2 font-sans text-sm text-nau-fg"
        />
      </label>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">BIS</span>
        <input
          type="date"
          value={to}
          onChange={(e) => setTo(e.target.value)}
          className="border border-nau-line bg-white/[0.03] px-2.5 py-2 font-sans text-sm text-nau-fg"
        />
      </label>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">DAUER (MIN)</span>
        <input
          type="number"
          min={5}
          step={5}
          value={duration}
          onChange={(e) => setDuration(e.target.value)}
          className="border border-nau-line bg-white/[0.03] px-2.5 py-2 font-sans text-sm text-nau-fg"
        />
      </label>

      <button
        type="button"
        onClick={run}
        disabled={loading}
        className="min-h-10 cursor-pointer border-none bg-nau-accent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-50"
      >
        {loading ? "SUCHE …" : "SLOTS SUCHEN ↵"}
      </button>

      {error && (
        <div className="font-mono text-[10px] tracking-mono-wide text-nau-danger">
          // {error}
        </div>
      )}

      {slots && (
        <div className="flex flex-col gap-1.5 border-t border-nau-line pt-3">
          <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
            {slots.length === 0 ? "KEINE TREFFER" : `${slots.length} TREFFER`}
          </div>
          <ul className="flex max-h-[420px] flex-col gap-1 overflow-y-auto">
            {slots.map((s) => {
              const startD = parseISO(s.start);
              const endD = parseISO(s.end);
              const key = `${s.start}|${s.end}`;
              const selected = selectedSlotKey === key;
              return (
                <li key={key}>
                  <button
                    type="button"
                    onClick={() => onPickSlot(selected ? null : s)}
                    className="flex w-full cursor-pointer flex-col items-start gap-0.5 border bg-transparent px-2 py-1.5 text-left transition-colors"
                    style={{
                      borderColor: selected ? "#facc15" : "rgba(255,255,255,0.10)",
                      background: selected ? "rgba(250,204,21,0.08)" : "transparent",
                    }}
                  >
                    <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
                      {format(startD, "EEE d.M.", { locale: de }).toUpperCase()}
                    </span>
                    <span className="font-sans text-[13px] text-nau-fg">
                      {formatTime(startD)} – {formatTime(endD)}
                    </span>
                    {s.status !== "passes" && (
                      <span
                        className="font-mono text-[9px] tracking-mono"
                        style={{
                          color: s.status === "hard" ? "#f472b6" : "#facc15",
                        }}
                      >
                        {s.status === "hard" ? "⚠ HARTE REGEL" : "⚠ WEICHE REGEL"}
                        {s.violatedBy && ` · ${s.violatedBy}`}
                      </span>
                    )}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
