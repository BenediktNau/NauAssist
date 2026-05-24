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
import { Header } from "@/components/nau/Header";
import { MonthView } from "@/components/calendar/MonthView";
import { WeekView } from "@/components/calendar/WeekView";
import { YearView } from "@/components/calendar/YearView";
import { SlotFinder } from "@/components/calendar/SlotFinder";
import { NotConnected } from "@/components/calendar/NotConnected";
import {
  getCalendarRange,
  NotConnectedError,
  type CalendarEvent,
  type FreeSlot,
} from "@/api/calendar";
import { getCalendarSettings, type CalendarSettings } from "@/api/calendar-settings";
import { parseEvents } from "@/components/calendar/utils";
import type { AppPage } from "@/App";

type ViewMode = "week" | "month" | "year";

interface CalendarPageProps {
  onNavigate: (page: AppPage) => void;
}

export function CalendarPage({ onNavigate }: CalendarPageProps) {
  const [view, setView] = useState<ViewMode>("week");
  const [anchor, setAnchor] = useState<Date>(() => new Date());
  const [settings, setSettings] = useState<CalendarSettings | null>(null);
  const [rawEvents, setRawEvents] = useState<CalendarEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notConnected, setNotConnected] = useState(false);
  const [selectedSlot, setSelectedSlot] = useState<FreeSlot | null>(null);

  const range = useMemo(() => computeRange(view, anchor), [view, anchor]);

  useEffect(() => {
    let cancelled = false;
    getCalendarSettings()
      .then((s) => {
        if (!cancelled) setSettings(s);
      })
      .catch((e) => {
        if (!cancelled) setError(String((e as Error).message ?? e));
      });
    return () => {
      cancelled = true;
    };
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
      .then((events) => {
        if (!cancelled) setRawEvents(events);
      })
      .catch((e) => {
        if (cancelled) return;
        if (e instanceof NotConnectedError) {
          setNotConnected(true);
        } else {
          setError(String((e as Error).message ?? e));
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
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

  return (
    <div className="flex min-h-screen flex-col bg-nau-bg text-nau-fg">
      <Header
        onOpenSettings={() => onNavigate("settings")}
        meta={loading ? "// LADE …" : `${rawEvents.length} EVT`}
        currentTab="calendar"
        onSelectTab={onNavigate}
      />

      <div className="mx-auto w-full max-w-[1400px] flex-1 px-4 py-6 lg:px-8 lg:py-8">
        <div className="mb-5 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <div className="mb-1 hidden font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim lg:block">
              — KALENDER —
            </div>
            <h1 className="m-0 font-sans text-2xl font-normal leading-tight tracking-tight text-nau-fg lg:text-3xl">
              <span className="font-mono font-bold text-nau-accent">{titleLabel}</span>
            </h1>
            <div className="mt-1 font-mono text-[11px] tracking-mono text-nau-fg-dim">
              {subtitle}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <ViewSwitcher value={view} onChange={setView} />
            <Nav onPrev={() => navigate(-1)} onToday={() => setAnchor(new Date())} onNext={() => navigate(1)} />
          </div>
        </div>

        {error && (
          <div className="mb-4 border border-nau-danger bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
            // {error}
          </div>
        )}

        {notConnected ? (
          <NotConnected onNavigate={onNavigate} hasGoogleCredentials={settings?.hasGoogleCredentials ?? false} />
        ) : (
          <div className="grid gap-5 lg:grid-cols-[1fr_300px]">
            <div className="min-w-0">
              {loading && rawEvents.length === 0 ? (
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
                />
              ) : view === "month" ? (
                <MonthView
                  monthAnchor={anchor}
                  events={events}
                  onPickWeek={(ws) => {
                    setAnchor(ws);
                    setView("week");
                  }}
                />
              ) : (
                <YearView
                  yearAnchor={anchor}
                  events={events}
                  onPickMonth={(m) => {
                    setAnchor(m);
                    setView("month");
                  }}
                />
              )}
            </div>

            {settings && (
              <aside className="min-w-0">
                <SlotFinder
                  defaultDurationMinutes={settings.defaultDurationMinutes}
                  searchHorizonDays={settings.searchHorizonDays}
                  onPickSlot={setSelectedSlot}
                  selectedSlotKey={
                    selectedSlot ? `${selectedSlot.start}|${selectedSlot.end}` : null
                  }
                />
              </aside>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

interface ViewSwitcherProps {
  value: ViewMode;
  onChange: (v: ViewMode) => void;
}

function ViewSwitcher({ value, onChange }: ViewSwitcherProps) {
  const opts: { v: ViewMode; label: string }[] = [
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
            className="cursor-pointer border-none px-3 py-2 font-mono text-[10px] tracking-mono-wide"
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
      <button
        type="button"
        onClick={onPrev}
        className="cursor-pointer border-none bg-transparent px-2 py-2 text-nau-fg"
        aria-label="Zurück"
      >
        <ChevronLeft size={16} strokeWidth={1.5} />
      </button>
      <button
        type="button"
        onClick={onToday}
        className="cursor-pointer border-none border-l border-r border-nau-line bg-transparent px-3 py-2 font-mono text-[10px] tracking-mono-wide text-nau-fg"
      >
        HEUTE
      </button>
      <button
        type="button"
        onClick={onNext}
        className="cursor-pointer border-none bg-transparent px-2 py-2 text-nau-fg"
        aria-label="Vor"
      >
        <ChevronRight size={16} strokeWidth={1.5} />
      </button>
    </div>
  );
}

interface Range {
  from: Date;
  to: Date;
}

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
    return `KW ${format(ws, "I", { locale: de })}`;
  }
  if (view === "month") {
    return format(anchor, "MMMM yyyy", { locale: de }).toUpperCase();
  }
  return format(anchor, "yyyy");
}

function formatSubtitle(view: ViewMode, range: Range): string {
  if (view === "week") {
    return `${format(range.from, "EEE d. MMM", { locale: de }).toUpperCase()} – ${format(range.to, "EEE d. MMM", { locale: de }).toUpperCase()}`;
  }
  if (view === "month") {
    return `${format(range.from, "d. MMM", { locale: de })} – ${format(range.to, "d. MMM yyyy", { locale: de })}`;
  }
  return `${format(range.from, "d. MMM")} – ${format(range.to, "d. MMM yyyy")}`;
}
