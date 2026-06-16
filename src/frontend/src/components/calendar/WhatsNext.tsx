import { useMemo } from "react";
import { format } from "date-fns";
import { de } from "date-fns/locale";
import { NotConnectedError } from "@/api/calendar";
import { useTodayEventsQuery } from "@/hooks/queries";
import { parseEvents, type ParsedEvent } from "./utils";
import type { PopoverState } from "./EventPopover";

interface WhatsNextProps {
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
}

export function WhatsNext({ onHoverEvent, onClickEvent }: WhatsNextProps) {
  const query = useTodayEventsQuery();
  // Nicht verbunden → wie "keine Termine" behandeln (Board zeigt den Hinweis).
  const notConnected = query.error instanceof NotConnectedError;
  const raw = useMemo(
    () => (notConnected ? [] : (query.data ?? [])),
    [notConnected, query.data],
  );
  const errorMessage =
    query.error && !notConnected ? query.error.message : null;

  const items = useMemo(() => {
    const now = new Date();
    return parseEvents(raw)
      .filter((e) => e.endDate > now)
      .sort((a, b) => a.startDate.getTime() - b.startDate.getTime());
  }, [raw]);

  const dateLabel = format(new Date(), "EEEE · d. MMMM", { locale: de }).toUpperCase();

  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-4">
      <div className="mb-3 flex items-center justify-between">
        <span className="font-mono text-[11px] tracking-mono text-nau-accent">
          // HEUTE
        </span>
        <span className="font-mono text-[9px] tracking-mono text-nau-fg-dim">
          {dateLabel}
        </span>
      </div>

      {query.isPending ? (
        <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
          // LADE …
        </div>
      ) : errorMessage ? (
        <div className="font-mono text-[10px] tracking-mono text-nau-danger">
          // {errorMessage}
        </div>
      ) : items.length === 0 ? (
        <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
          // NICHTS MEHR HEUTE
        </div>
      ) : (
        <ul className="flex flex-col gap-1.5">
          {items.map((e) => (
            <li key={e.id}>
              <WhatsNextItem
                event={e}
                onHoverEvent={onHoverEvent}
                onClickEvent={onClickEvent}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

interface WhatsNextItemProps {
  event: ParsedEvent;
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
}

function WhatsNextItem({ event, onHoverEvent, onClickEvent }: WhatsNextItemProps) {
  const timeLabel = event.isAllDay
    ? "GANZTAG"
    : `${format(event.startDate, "HH:mm")}–${format(event.endDate, "HH:mm")}`;

  return (
    <button
      type="button"
      onMouseEnter={(ev) =>
        onHoverEvent({
          event,
          anchor: ev.currentTarget.getBoundingClientRect(),
          pinned: false,
        })
      }
      onMouseLeave={() => onHoverEvent(null)}
      onClick={(ev) =>
        onClickEvent({
          event,
          anchor: ev.currentTarget.getBoundingClientRect(),
          pinned: true,
        })
      }
      className="grid w-full cursor-pointer grid-cols-[96px_1fr] items-baseline gap-3 border-l-2 bg-transparent px-2 py-1.5 text-left transition-colors hover:bg-white/[0.03]"
      style={{
        borderLeftColor: event.isAllDay ? "#60a5fa" : "#f5f5f4",
      }}
    >
      <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
        {timeLabel}
      </span>
      <div className="min-w-0">
        <div className="truncate font-sans text-[13px] text-nau-fg">
          {event.isSeriesInstance && (
            <span className="mr-1 text-nau-fg-dim" aria-label="Serie">↻</span>
          )}
          {event.title}
        </div>
        {event.location && (
          <div className="truncate font-sans text-[11px] text-nau-fg-dim">
            @ {event.location}
          </div>
        )}
      </div>
    </button>
  );
}
