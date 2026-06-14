import { useEffect, useMemo, useRef, useState } from "react";
import {
  addDays,
  addMonths,
  addWeeks,
  addYears,
  endOfMonth,
  endOfWeek,
  endOfYear,
  format,
  parseISO,
  startOfMonth,
  startOfWeek,
  startOfYear,
} from "date-fns";
import { de } from "date-fns/locale";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { MonthView } from "./MonthView";
import { WeekView, type ParsedProposal } from "./WeekView";
import { DayView } from "./DayView";
import { DayStrip } from "./DayStrip";
import { YearView } from "./YearView";
import { WhatsNext } from "./WhatsNext";
import { NotConnected } from "./NotConnected";
import { EventPopover, type PopoverState } from "./EventPopover";
import { parseEvents } from "./utils";
import { NotConnectedError } from "@/api/calendar";
import { useIsMobile, isMobileViewport } from "@/hooks/useIsMobile";
import { PageLoader } from "@/components/nau/PageLoader";
import {
  useCalendarRangeQuery,
  useCalendarSettingsQuery,
  useTodayEventsQuery,
} from "@/hooks/queries";
import type { AppPage } from "@/App";
import type { ActiveProposals } from "@/hooks/useChat";
import type { SlotInfo } from "@/api/types";

export type CalendarBoardVariant = "full" | "compact";
type ViewMode = "day" | "week" | "month" | "year";

interface CalendarBoardProps {
  variant: CalendarBoardVariant;
  onNavigate: (page: AppPage) => void;
  /** Aktive Vorschläge aus dem Chat — werden im WeekView als Ghost-Events angezeigt. */
  proposals?: ActiveProposals | null;
  /** Klick auf einen Vorschlags-Ghost akzeptiert den Slot. */
  onPickProposal?: (slot: SlotInfo) => void;
}

const HOVER_HIDE_DELAY_MS = 180;

export function CalendarBoard({
  variant,
  onNavigate,
  proposals,
  onPickProposal,
}: CalendarBoardProps) {
  const isMobile = useIsMobile();
  // Auf dem Handy ist die Tagesansicht die Voreinstellung (volle Breite, lesbar).
  const [view, setView] = useState<ViewMode>(() =>
    isMobileViewport() ? "day" : "week",
  );
  const [anchor, setAnchor] = useState<Date>(() => new Date());

  const [popover, setPopover] = useState<PopoverState | null>(null);
  const hideTimer = useRef<number | null>(null);

  const cancelHide = () => {
    if (hideTimer.current !== null) {
      window.clearTimeout(hideTimer.current);
      hideTimer.current = null;
    }
  };

  const scheduleHide = () => {
    cancelHide();
    hideTimer.current = window.setTimeout(() => {
      setPopover((p) => (p?.pinned ? p : null));
      hideTimer.current = null;
    }, HOVER_HIDE_DELAY_MS);
  };

  const handleHoverEvent = (next: PopoverState | null) => {
    if (popover?.pinned) return;
    if (next === null) {
      scheduleHide();
    } else {
      cancelHide();
      setPopover(next);
    }
  };

  const handleClickEvent = (next: PopoverState) => {
    cancelHide();
    setPopover(next);
  };

  const closePopover = () => {
    cancelHide();
    setPopover(null);
  };

  const range = useMemo(() => computeRange(view, anchor), [view, anchor]);

  const settingsQuery = useCalendarSettingsQuery();
  const settings = settingsQuery.data ?? null;
  const connected = settings?.isConnected === true;

  const eventsQuery = useCalendarRangeQuery(range.from, range.to, connected);
  const todayQuery = useTodayEventsQuery();

  const rawEvents = useMemo(() => eventsQuery.data ?? [], [eventsQuery.data]);
  const notConnected =
    (settings !== null && !settings.isConnected) ||
    eventsQuery.error instanceof NotConnectedError;
  const initialPending =
    settingsQuery.isPending ||
    (connected && (eventsQuery.isPending || todayQuery.isPending));
  const errorMessage = settingsQuery.error
    ? settingsQuery.error.message
    : eventsQuery.error && !(eventsQuery.error instanceof NotConnectedError)
      ? eventsQuery.error.message
      : null;

  const events = useMemo(() => parseEvents(rawEvents), [rawEvents]);
  const titleLabel = useMemo(() => formatTitle(view, anchor), [view, anchor]);
  const subtitle = useMemo(() => formatSubtitle(view, range), [view, range]);

  const parsedProposals = useMemo<ParsedProposal[]>(() => {
    if (!proposals) return [];
    return proposals.slots
      .map((s) => {
        const start = parseISO(s.start);
        const end = parseISO(s.end);
        if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
          return null;
        }
        return { slot: s, startDate: start, endDate: end };
      })
      .filter((p): p is ParsedProposal => p !== null);
  }, [proposals]);

  // Auto-Jump: bei neuen Proposals (messageId-Wechsel) Anchor auf ersten Slot setzen
  // und in die Woche-Ansicht wechseln, damit der Nutzer sie sofort einschätzen kann.
  const lastProposalsIdRef = useRef<number | null>(null);
  useEffect(() => {
    const id = proposals?.messageId ?? null;
    if (id === null) {
      lastProposalsIdRef.current = null;
      return;
    }
    if (id === lastProposalsIdRef.current) return;
    lastProposalsIdRef.current = id;
    if (parsedProposals.length === 0) return;
    setAnchor(parsedProposals[0].startDate);
    // Auf dem Handy in die Tagesansicht des Vorschlags springen, sonst Woche.
    setView(isMobileViewport() ? "day" : "week");
  }, [proposals?.messageId, parsedProposals]);

  const navigate = (delta: number) => {
    setAnchor((d) => {
      if (view === "day") return addDays(d, delta);
      if (view === "week") return addWeeks(d, delta);
      if (view === "month") return addMonths(d, delta);
      return addYears(d, delta);
    });
  };

  const compact = variant === "compact";

  if (initialPending) {
    if (compact) {
      return (
        <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-10 text-center font-mono text-[11px] tracking-mono text-nau-fg-dim">
          // LADE KALENDER …
        </div>
      );
    }
    return <PageLoader label="LADE KALENDER" />;
  }

  if (notConnected) {
    return (
      <NotConnected
        onNavigate={onNavigate}
        hasGoogleCredentials={settings?.hasGoogleCredentials ?? false}
      />
    );
  }

  const header = (
    <div className={compact
      ? "flex flex-col gap-2"
      : "mb-5 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between"}
    >
      <div>
        <div className="font-mono text-[11px] tracking-mono text-nau-accent">
          {titleLabel}
        </div>
        <div className="mt-0.5 font-mono text-[10px] tracking-mono text-nau-fg-dim">
          {subtitle}
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <ViewSwitcher value={view} onChange={setView} compact={compact || isMobile} />
        <Nav
          onPrev={() => navigate(-1)}
          onToday={() => setAnchor(new Date())}
          onNext={() => navigate(1)}
        />
      </div>
    </div>
  );

  const grid = errorMessage ? (
    <div className="border border-nau-danger bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
      // {errorMessage}
    </div>
  ) : view === "day" ? (
    <div className="flex flex-col gap-2">
      <DayStrip
        weekStart={startOfWeek(anchor, { weekStartsOn: 1 })}
        selected={anchor}
        events={events}
        onSelect={setAnchor}
      />
      <DayView
        day={anchor}
        events={events}
        workingHoursStart={settings?.workingHoursStart ?? "09:00"}
        workingHoursEnd={settings?.workingHoursEnd ?? "18:00"}
        rowHeight={compact ? 44 : 56}
        onHoverEvent={handleHoverEvent}
        onClickEvent={handleClickEvent}
        proposals={parsedProposals}
        onPickProposal={onPickProposal}
      />
    </div>
  ) : view === "week" ? (
    <WeekView
      weekStart={range.from}
      events={events}
      workingHoursStart={settings?.workingHoursStart ?? "09:00"}
      workingHoursEnd={settings?.workingHoursEnd ?? "18:00"}
      rowHeight={compact ? 30 : 44}
      onHoverEvent={handleHoverEvent}
      onClickEvent={handleClickEvent}
      proposals={parsedProposals}
      onPickProposal={onPickProposal}
    />
  ) : view === "month" ? (
    <MonthView
      monthAnchor={anchor}
      events={events}
      onPickWeek={(ws) => { setAnchor(ws); setView("week"); }}
      minCellHeight={compact ? 80 : 110}
      onHoverEvent={handleHoverEvent}
      onClickEvent={handleClickEvent}
    />
  ) : (
    <YearView
      yearAnchor={anchor}
      events={events}
      onPickMonth={(m) => { setAnchor(m); setView("month"); }}
      columns={compact ? 2 : 4}
    />
  );

  const whatsNext = (
    <WhatsNext
      onHoverEvent={handleHoverEvent}
      onClickEvent={handleClickEvent}
    />
  );

  const body = compact ? (
    <div className="flex flex-col gap-3">
      {header}
      {grid}
      {whatsNext}
    </div>
  ) : (
    <div className="flex flex-col gap-4">
      {header}
      <div className="grid gap-5 lg:grid-cols-[1fr_320px]">
        <div className="min-w-0">{grid}</div>
        <aside className="min-w-0">{whatsNext}</aside>
      </div>
    </div>
  );

  return (
    <>
      {body}
      {popover && (
        <EventPopover
          state={popover}
          onClose={closePopover}
          onMouseEnter={cancelHide}
          onMouseLeave={scheduleHide}
        />
      )}
    </>
  );
}

interface ViewSwitcherProps {
  value: ViewMode;
  onChange: (v: ViewMode) => void;
  compact: boolean;
}

function ViewSwitcher({ value, onChange, compact }: ViewSwitcherProps) {
  const opts: { v: ViewMode; label: string }[] = compact
    ? [
        { v: "day", label: "T" },
        { v: "week", label: "W" },
        { v: "month", label: "M" },
        { v: "year", label: "J" },
      ]
    : [
        { v: "day", label: "TAG" },
        { v: "week", label: "WOCHE" },
        { v: "month", label: "MONAT" },
        { v: "year", label: "JAHR" },
      ];

  return (
    <div className="inline-flex border border-nau-line">
      {opts.map((o) => {
        const active = value === o.v;
        return (
          <button
            key={o.v}
            type="button"
            onClick={() => onChange(o.v)}
            className="cursor-pointer border-none px-2.5 py-1.5 font-mono text-[10px] tracking-mono-wide"
            style={{
              background: active ? "#facc15" : "transparent",
              color: active ? "#0a0a0a" : "#f5f5f4",
            }}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}

function Nav({
  onPrev, onToday, onNext,
}: { onPrev: () => void; onToday: () => void; onNext: () => void }) {
  return (
    <div className="inline-flex border border-nau-line">
      <button type="button" onClick={onPrev} aria-label="Zurück"
        className="cursor-pointer border-none bg-transparent px-2 py-1.5 text-nau-fg">
        <ChevronLeft size={14} strokeWidth={1.5} />
      </button>
      <button type="button" onClick={onToday}
        className="cursor-pointer border-none border-l border-r border-nau-line bg-transparent px-3 py-1.5 font-mono text-[10px] tracking-mono-wide text-nau-fg">
        HEUTE
      </button>
      <button type="button" onClick={onNext} aria-label="Vor"
        className="cursor-pointer border-none bg-transparent px-2 py-1.5 text-nau-fg">
        <ChevronRight size={14} strokeWidth={1.5} />
      </button>
    </div>
  );
}

interface Range { from: Date; to: Date }

function computeRange(view: ViewMode, anchor: Date): Range {
  // Tagesansicht lädt die ganze Woche, damit die Wochenleiste Marker zeigen
  // und der Tageswechsel ohne Nachladen funktioniert.
  if (view === "day" || view === "week") {
    return {
      from: startOfWeek(anchor, { weekStartsOn: 1 }),
      to: endOfWeek(anchor, { weekStartsOn: 1 }),
    };
  }
  if (view === "month") {
    return {
      from: startOfWeek(startOfMonth(anchor), { weekStartsOn: 1 }),
      to: endOfWeek(endOfMonth(anchor), { weekStartsOn: 1 }),
    };
  }
  return { from: startOfYear(anchor), to: endOfYear(anchor) };
}

function formatTitle(view: ViewMode, anchor: Date): string {
  if (view === "day") {
    return format(anchor, "EEEE, d. MMMM", { locale: de }).toUpperCase();
  }
  if (view === "week") {
    const ws = startOfWeek(anchor, { weekStartsOn: 1 });
    return `KW ${format(ws, "I", { locale: de })} · ${format(ws, "yyyy")}`;
  }
  if (view === "month") return format(anchor, "MMMM yyyy", { locale: de }).toUpperCase();
  return format(anchor, "yyyy");
}

function formatSubtitle(view: ViewMode, range: Range): string {
  if (view === "day") {
    return `KW ${format(range.from, "I", { locale: de })} · ${format(range.from, "yyyy")}`;
  }
  if (view === "week") {
    return `${format(range.from, "d. MMM", { locale: de })} – ${format(range.to, "d. MMM", { locale: de })}`;
  }
  if (view === "month") {
    return `${format(range.from, "d.M.")} – ${format(range.to, "d.M.yyyy")}`;
  }
  return `${format(range.from, "d. MMM")} – ${format(range.to, "d. MMM yyyy")}`;
}
