import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { addDays, differenceInCalendarDays, format } from "date-fns";
import { de } from "date-fns/locale";
import type { ParsedEvent } from "./utils";
import {
  positionPopover,
  POPOVER_GAP,
  POPOVER_MARGIN,
  type PopoverPosition,
} from "./positionPopover";

export interface PopoverState {
  event: ParsedEvent;
  anchor: DOMRect;
  pinned: boolean;
}

interface EventPopoverProps {
  state: PopoverState;
  onClose: () => void;
  onMouseEnter: () => void;
  onMouseLeave: () => void;
}

const MAX_WIDTH = 320;

export function EventPopover({ state, onClose, onMouseEnter, onMouseLeave }: EventPopoverProps) {
  const ref = useRef<HTMLDivElement | null>(null);
  const [position, setPosition] = useState<PopoverPosition | null>(null);

  // Breite nie breiter als der Viewport (schmales Handy): so passt das Popover
  // immer mit Rand neben den Termin und kann sauber geklemmt werden.
  const maxWidth = Math.min(MAX_WIDTH, window.innerWidth - 2 * POPOVER_MARGIN);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setPosition(
      positionPopover(
        state.anchor,
        { width: rect.width, height: rect.height },
        { width: window.innerWidth, height: window.innerHeight },
      ),
    );
  }, [state.anchor]);

  useEffect(() => {
    if (!state.pinned) return;
    const onDocClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        onClose();
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("mousedown", onDocClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDocClick);
      document.removeEventListener("keydown", onKey);
    };
  }, [state.pinned, onClose]);

  const { event } = state;
  const dateLine = formatRange(event);

  const content = (
    <div
      ref={ref}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      className="pointer-events-auto fixed z-[80] border border-nau-line-strong bg-nau-bg p-4 shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open will-change-transform"
      style={{
        top: position?.top ?? state.anchor.top,
        left: position?.left ?? state.anchor.right + POPOVER_GAP,
        maxWidth,
        visibility: position ? "visible" : "hidden",
        transformOrigin: position?.origin ?? "top left",
      }}
    >
      <div className="animate-nau-mech-fade">
        <div className="font-sans text-sm font-medium leading-snug text-nau-fg">
          {event.isSeriesInstance && (
            <span className="mr-1 text-nau-fg-dim" aria-label="Serie">↻</span>
          )}
          {event.title}
        </div>
        <div className="mt-1 font-mono text-[10px] tracking-mono text-nau-fg-dim">
          {dateLine}
        </div>
        {event.isSeriesInstance && (
          <div className="mt-1 font-mono text-[9px] tracking-mono text-nau-fg-dim">
            ↻ TEIL EINER SERIE
          </div>
        )}
        {event.location && (
          <div className="mt-2 font-sans text-[12px] text-nau-fg-dim">
            @ {event.location}
          </div>
        )}
        {event.description && (
          <div className="mt-2 max-h-[180px] overflow-y-auto font-sans text-[12px] leading-relaxed text-nau-fg whitespace-pre-wrap">
            {event.description}
          </div>
        )}
        {state.pinned && (
          <button
            type="button"
            onClick={onClose}
            className="mt-3 cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim"
          >
            SCHLIESSEN
          </button>
        )}
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

function formatRange(event: ParsedEvent): string {
  if (event.isAllDay) {
    const inclusiveEnd = addDays(event.endDate, -1);
    const days = differenceInCalendarDays(inclusiveEnd, event.startDate) + 1;
    if (days <= 1) {
      return `${format(event.startDate, "EEE d. MMM yyyy", { locale: de }).toUpperCase()} · GANZTAG`;
    }
    return `${format(event.startDate, "d. MMM", { locale: de })} – ${format(inclusiveEnd, "d. MMM yyyy", { locale: de })} · ${days} TAGE`;
  }
  const sameDay =
    event.startDate.toDateString() === event.endDate.toDateString();
  if (sameDay) {
    return `${format(event.startDate, "EEE d. MMM", { locale: de }).toUpperCase()} · ${format(event.startDate, "HH:mm")}–${format(event.endDate, "HH:mm")}`;
  }
  return `${format(event.startDate, "d.M. HH:mm")} – ${format(event.endDate, "d.M. HH:mm")}`;
}
