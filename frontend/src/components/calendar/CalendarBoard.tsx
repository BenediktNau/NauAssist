import { useEffect, useMemo, useState } from "react";
import {
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
import { WeekView } from "./WeekView";
import { YearView } from "./YearView";
import { SlotFinder } from "./SlotFinder";
import { NotConnected } from "./NotConnected";
import { parseEvents } from "./utils";
import {
  getCalendarRange,
  NotConnectedError,
  type CalendarEvent,
  type FreeSlot,
} from "@/api/calendar";
import { getCalendarSettings, type CalendarSettings } from "@/api/calendar-settings";
import type { AppPage } from "@/App";

export type CalendarBoardVariant = "full" | "compact";
type ViewMode = "week" | "month" | "year";

interface CalendarBoardProps {
  variant: CalendarBoardVariant;
  onNavigate: (page: AppPage) => void;
}

export function CalendarBoard({ variant, onNavigate }: CalendarBoardProps) {
  const [view, setView] = useState<ViewMode>("week");
  const [anchor, setAnchor] = useState<Date>(() => new Date());
  const [settings, setSettings] = useState<CalendarSettings | null>(null);
  const [rawEvents, setRawEvents] = useState<CalendarEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notConnected, setNotConnected] = useState(false);
  const [selectedSlot, setSelectedSlot] = useState<FreeSlot | null>(null);
  const [slotsOpen, setSlotsOpen] = useState(variant === "full");

  const range = useMemo(() => computeRange(view, anchor), [view, anchor]);

  useEffect(() => {
    let cancelled = false;
    getCalendarSettings()
      .then((s) => { if (!cancelled) setSettings(s); })
      .catch((e) => { if (!cancelled) setError(String((e as Error).message ?? e)); });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (settings === null) return;
    if (!settings.isConnected) {
      setNotConnected(true);
      setRawEvents([]);
      setLoading(false);
      return;
    }
    setNotConnected(false);
    setLoading(true);
    setError(null);
    let cancelled = false;
    getCalendarRange(range.from, range.to)
      .then((events) => { if (!cancelled) setRawEvents(events); })
      .catch((e) => {
        if (cancelled) return;
        if (e instanceof NotConnectedError) setNotConnected(true);
        else setError(String((e as Error).message ?? e));
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [settings, range.from, range.to]);

  const events = useMemo(() => parseEvents(rawEvents), [rawEvents]);
  const titleLabel = useMemo(() => formatTitle(view, anchor), [view, anchor]);
  const subtitle = useMemo(() => formatSubtitle(view, range), [view, range]);

  const navigate = (delta: number) => {
    setAnchor((d) => {
      if (view === "week") return addWeeks(d, delta);
      if (view === "month") return addMonths(d, delta);
      return addYears(d, delta);
    });
  };

  const selectedSlotForGrid = useMemo(() => {
    if (!selectedSlot) return null;
    return { start: parseISO(selectedSlot.start), end: parseISO(selectedSlot.end) };
  }, [selectedSlot]);

  const compact = variant === "compact";

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
        <ViewSwitcher value={view} onChange={setView} compact={compact} />
        <Nav
          onPrev={() => navigate(-1)}
          onToday={() => setAnchor(new Date())}
          onNext={() => navigate(1)}
        />
      </div>
    </div>
  );

  const grid = error ? (
    <div className="border border-nau-danger bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
      // {error}
    </div>
  ) : loading && rawEvents.length === 0 ? (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-10 text-center font-mono text-[11px] tracking-mono text-nau-fg-dim">
      // LADE EVENTS …
    </div>
  ) : view === "week" ? (
    <WeekView
      weekStart={range.from}
      events={events}
      workingHoursStart={settings?.workingHoursStart ?? "09:00"}
      workingHoursEnd={settings?.workingHoursEnd ?? "18:00"}
      highlightedSlot={selectedSlotForGrid}
      rowHeight={compact ? 30 : 44}
    />
  ) : view === "month" ? (
    <MonthView
      monthAnchor={anchor}
      events={events}
      onPickWeek={(ws) => { setAnchor(ws); setView("week"); }}
      minCellHeight={compact ? 80 : 110}
    />
  ) : (
    <YearView
      yearAnchor={anchor}
      events={events}
      onPickMonth={(m) => { setAnchor(m); setView("month"); }}
      columns={compact ? 2 : 4}
    />
  );

  const slotFinderPanel = settings && (
    <SlotFinder
      defaultDurationMinutes={settings.defaultDurationMinutes}
      searchHorizonDays={settings.searchHorizonDays}
      onPickSlot={setSelectedSlot}
      selectedSlotKey={selectedSlot ? `${selectedSlot.start}|${selectedSlot.end}` : null}
    />
  );

  if (compact) {
    return (
      <div className="flex flex-col gap-3">
        {header}
        {grid}
        {settings && (
          <div className="flex flex-col gap-2">
            <button
              type="button"
              onClick={() => setSlotsOpen((v) => !v)}
              className="cursor-pointer border border-nau-line bg-transparent px-3 py-2 text-left font-mono text-[10px] tracking-mono-wide text-nau-fg-dim transition-colors hover:text-nau-accent"
            >
              {slotsOpen ? "▼ SLOT-EMPFEHLUNGEN" : "▶ SLOT-EMPFEHLUNGEN"}
            </button>
            {slotsOpen && slotFinderPanel}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {header}
      <div className="grid gap-5 lg:grid-cols-[1fr_300px]">
        <div className="min-w-0">{grid}</div>
        {settings && (
          <aside className="min-w-0">{slotFinderPanel}</aside>
        )}
      </div>
    </div>
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
        { v: "week", label: "W" },
        { v: "month", label: "M" },
        { v: "year", label: "J" },
      ]
    : [
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
  if (view === "week") {
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
  if (view === "week") {
    const ws = startOfWeek(anchor, { weekStartsOn: 1 });
    return `KW ${format(ws, "I", { locale: de })} · ${format(ws, "yyyy")}`;
  }
  if (view === "month") return format(anchor, "MMMM yyyy", { locale: de }).toUpperCase();
  return format(anchor, "yyyy");
}

function formatSubtitle(view: ViewMode, range: Range): string {
  if (view === "week") {
    return `${format(range.from, "d. MMM", { locale: de })} – ${format(range.to, "d. MMM", { locale: de })}`;
  }
  if (view === "month") {
    return `${format(range.from, "d.M.")} – ${format(range.to, "d.M.yyyy")}`;
  }
  return `${format(range.from, "d. MMM")} – ${format(range.to, "d. MMM yyyy")}`;
}
