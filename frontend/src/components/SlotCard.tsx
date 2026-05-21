import type { SlotInfo } from "@/api/types";

const FORMATTER = new Intl.DateTimeFormat("de-DE", {
  weekday: "short",
  day: "2-digit",
  month: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

const TIME_ONLY = new Intl.DateTimeFormat("de-DE", {
  hour: "2-digit",
  minute: "2-digit",
});

const DAY_LABEL = new Intl.DateTimeFormat("de-DE", {
  weekday: "short",
  day: "2-digit",
  month: "2-digit",
});

const HOURS_ONLY = new Intl.DateTimeFormat("de-DE", {
  hour: "2-digit",
  minute: "2-digit",
});

function formatSlot(slot: SlotInfo): string {
  const startDate = new Date(slot.start);
  const endDate = new Date(slot.end);
  return `${FORMATTER.format(startDate)}–${TIME_ONLY.format(endDate)}`;
}

function formatDay(slot: SlotInfo): string {
  return DAY_LABEL.format(new Date(slot.start)).toUpperCase();
}

function formatTimeRange(slot: SlotInfo): string {
  return `${HOURS_ONLY.format(new Date(slot.start))} – ${HOURS_ONLY.format(new Date(slot.end))}`;
}

function durationLabel(slot: SlotInfo): string {
  const ms = new Date(slot.end).getTime() - new Date(slot.start).getTime();
  const mins = Math.max(1, Math.round(ms / 60000));
  if (mins >= 60 && mins % 60 === 0) {
    return `${mins / 60}H`;
  }
  if (mins >= 60) {
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    return `${h}H ${m}MIN`;
  }
  return `${mins} MIN`;
}

interface SlotSuggestionsProps {
  slots: SlotInfo[];
  onPick: (slot: SlotInfo) => void;
  disabled?: boolean;
}

export function SlotSuggestions({ slots, onPick, disabled }: SlotSuggestionsProps) {
  if (slots.length === 0) return null;

  const label =
    slots.length === 1
      ? "// 1 FREIER SLOT · TAP TO BOOK"
      : `// ${slots.length} FREIE SLOTS · TAP TO BOOK`;

  return (
    <div>
      <div className="mb-2.5 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
        {label}
      </div>
      <div
        className="grid gap-2"
        style={{
          gridTemplateColumns:
            slots.length === 1
              ? "1fr"
              : `repeat(${Math.min(slots.length, 3)}, minmax(0, 1fr))`,
        }}
      >
        {slots.map((slot, i) => {
          const primary = i === 0;
          return (
            <button
              key={`${slot.start}-${i}`}
              type="button"
              onClick={() => !disabled && onPick(slot)}
              disabled={disabled}
              className={[
                "flex flex-col gap-1 px-3.5 py-3 text-left transition-colors",
                primary
                  ? "border border-nau-accent bg-nau-accent text-nau-bg hover:bg-yellow-300"
                  : "border border-nau-line bg-transparent text-nau-fg hover:bg-white/[0.04]",
                disabled ? "cursor-not-allowed opacity-50" : "cursor-pointer",
              ].join(" ")}
            >
              <span
                className="font-mono text-[10px] tracking-mono"
                style={{ opacity: primary ? 0.7 : 0.6 }}
              >
                {formatDay(slot)}
              </span>
              <span className="font-sans text-sm font-semibold">{formatTimeRange(slot)}</span>
              <span
                className="font-mono text-[9px] tracking-mono"
                style={{ opacity: primary ? 0.7 : 0.5 }}
              >
                {durationLabel(slot)}
                {slot.note ? ` · ${slot.note}` : ""}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export { formatSlot };
