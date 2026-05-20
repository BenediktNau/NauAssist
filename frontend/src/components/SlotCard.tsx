import { Card } from "@/components/ui/card";
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

function formatSlot(slot: SlotInfo): string {
  const startDate = new Date(slot.start);
  const endDate = new Date(slot.end);
  return `${FORMATTER.format(startDate)}–${TIME_ONLY.format(endDate)}`;
}

interface SlotCardProps {
  slot: SlotInfo;
  onPick: (slot: SlotInfo) => void;
  disabled?: boolean;
}

export function SlotCard({ slot, onPick, disabled }: SlotCardProps) {
  return (
    <Card
      role="button"
      tabIndex={0}
      aria-disabled={disabled}
      className={
        "p-3 cursor-pointer hover:bg-slate-100 transition-colors " +
        (disabled ? "opacity-50 cursor-not-allowed" : "")
      }
      onClick={() => !disabled && onPick(slot)}
      onKeyDown={(e) => {
        if (!disabled && (e.key === "Enter" || e.key === " ")) {
          e.preventDefault();
          onPick(slot);
        }
      }}
    >
      <div className="font-medium">{formatSlot(slot)}</div>
      {slot.note && <div className="text-sm text-slate-500 mt-1">{slot.note}</div>}
    </Card>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export { formatSlot };
