import { useCallback, useEffect, useState } from "react";
import { Header } from "@/components/nau/Header";
import type { AppPage } from "@/App";
import {
  dismissSuggestion,
  listSuggestions,
  pickSuggestionSlot,
  pollSuggestionsNow,
  type SuggestionDto,
  type SuggestionStatus,
} from "@/api/suggestions";

interface RecommendationsPageProps {
  onNavigate: (page: AppPage) => void;
}

const STATUS_TABS: { key: SuggestionStatus | "all"; label: string }[] = [
  { key: "pending", label: "OFFEN" },
  { key: "responded", label: "ERLEDIGT" },
  { key: "dismissed", label: "VERWORFEN" },
  { key: "all", label: "ALLE" },
];

export function RecommendationsPage({ onNavigate }: RecommendationsPageProps) {
  const [items, setItems] = useState<SuggestionDto[]>([]);
  const [filter, setFilter] = useState<SuggestionStatus | "all">("pending");
  const [loading, setLoading] = useState(false);
  const [polling, setPolling] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pollMessage, setPollMessage] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listSuggestions(filter === "all" ? undefined : filter);
      setItems(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const onPollNow = async () => {
    setPolling(true);
    setPollMessage(null);
    setError(null);
    try {
      const r = await pollSuggestionsNow();
      setPollMessage(
        r.skipped
          ? "Tick übersprungen — vorheriger Lauf noch aktiv."
          : `Tick ok · ${r.signalCount} Signale · ${r.expiredCount} expired · ${r.errorCount} Fehler`,
      );
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setPolling(false);
    }
  };

  const onPick = async (id: number, slotIndex: number) => {
    setError(null);
    try {
      const updated = await pickSuggestionSlot(id, slotIndex);
      setItems((prev) => prev.map((s) => (s.id === id ? updated : s)));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const onDismiss = async (id: number) => {
    setError(null);
    try {
      await dismissSuggestion(id);
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  return (
    <div className="flex min-h-screen flex-col bg-nau-bg text-nau-fg">
      <Header
        onOpenSettings={() => onNavigate("settings")}
        currentTab="recommendations"
        onSelectTab={onNavigate}
      />
      <div className="mx-auto w-full max-w-[1100px] flex-1 px-4 py-6 lg:px-8 lg:py-10">
        <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
          <div>
            <span className="font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
              // AUTONOMER AGENT
            </span>
            <h1 className="m-0 mt-1 font-sans text-3xl font-normal leading-tight text-nau-fg lg:text-4xl">
              Empfehlungen
            </h1>
            <p className="m-0 mt-2 max-w-xl font-sans text-sm leading-relaxed text-nau-fg-dim">
              Hier erscheinen Termin-Vorschläge, die der Agent aus deinen freigegebenen
              Quellen abgeleitet hat. Phase 1 zeigt noch nichts an — Backend-Foundation
              steht, Quellen folgen.
            </p>
          </div>
          <button
            type="button"
            onClick={onPollNow}
            disabled={polling}
            className="border border-nau-line px-4 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-50"
          >
            {polling ? "PRÜFE …" : "JETZT PRÜFEN"}
          </button>
        </div>

        <nav role="tablist" aria-label="Status-Filter" className="mb-5 flex gap-1">
          {STATUS_TABS.map((t) => (
            <button
              key={t.key}
              type="button"
              role="tab"
              aria-selected={filter === t.key}
              onClick={() => setFilter(t.key)}
              className={
                "min-h-10 cursor-pointer border-b-2 bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide transition-colors " +
                (filter === t.key
                  ? "border-nau-accent text-nau-accent"
                  : "border-transparent text-nau-fg-dim hover:text-nau-fg")
              }
            >
              {t.label}
            </button>
          ))}
        </nav>

        {pollMessage && (
          <div className="mb-4 border border-nau-line bg-white/[0.02] px-4 py-3 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
            {pollMessage}
          </div>
        )}
        {error && (
          <div className="mb-4 border border-nau-danger bg-white/[0.02] px-4 py-3 font-mono text-sm text-nau-danger">
            [ ! ] {error}
          </div>
        )}

        {loading && items.length === 0 ? (
          <EmptyState text="Lade …" />
        ) : items.length === 0 ? (
          <EmptyState
            text={
              filter === "pending"
                ? "Keine offenen Empfehlungen. Der Agent prüft alle 20 min."
                : "Nichts gefunden."
            }
          />
        ) : (
          <ul className="flex flex-col gap-3">
            {items.map((s) => (
              <SuggestionCard key={s.id} suggestion={s} onPick={onPick} onDismiss={onDismiss} />
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

interface SuggestionCardProps {
  suggestion: SuggestionDto;
  onPick: (id: number, slotIndex: number) => void;
  onDismiss: (id: number) => void;
}

function SuggestionCard({ suggestion, onPick, onDismiss }: SuggestionCardProps) {
  const isPending = suggestion.status === "pending";
  return (
    <li className="border border-nau-line bg-white/[0.02] p-4 lg:p-5">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="font-mono text-[11px] tracking-mono-wide text-nau-accent">
          {suggestion.source.toUpperCase()}
        </span>
        {suggestion.requester && (
          <span className="font-sans text-sm font-medium text-nau-fg">
            {suggestion.requester}
          </span>
        )}
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {formatRelative(suggestion.createdAt)}
        </span>
        <span className="ml-auto font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
          {suggestion.status.toUpperCase()}
        </span>
      </div>

      {suggestion.topic && (
        <div className="mt-2 font-sans text-base text-nau-fg">{suggestion.topic}</div>
      )}
      {suggestion.quotedText && (
        <blockquote className="mt-2 border-l-2 border-nau-line pl-3 font-sans text-sm italic leading-relaxed text-nau-fg-dim">
          {suggestion.quotedText}
        </blockquote>
      )}

      {suggestion.slots.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-2">
          {suggestion.slots.map((slot, idx) => (
            <button
              key={`${suggestion.id}-${idx}`}
              type="button"
              disabled={!isPending}
              onClick={() => onPick(suggestion.id, idx)}
              className={
                "flex flex-col items-start border px-3 py-2 text-left font-mono text-[12px] transition-colors " +
                (suggestion.pickedSlot === idx
                  ? "border-nau-accent bg-nau-accent/10 text-nau-accent"
                  : "border-nau-line text-nau-fg-dim hover:border-nau-accent hover:text-nau-fg") +
                (isPending ? "" : " opacity-60")
              }
            >
              <span>{formatSlotRange(slot.start, slot.end)}</span>
              {slot.note && <span className="mt-0.5 text-[11px] text-nau-fg-dim">{slot.note}</span>}
            </button>
          ))}
        </div>
      )}

      {isPending && (
        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={() => onDismiss(suggestion.id)}
            className="border border-nau-line px-3 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-danger hover:text-nau-danger"
          >
            VERWERFEN
          </button>
        </div>
      )}
    </li>
  );
}

function EmptyState({ text }: { text: string }) {
  return (
    <div className="border border-dashed border-nau-line px-6 py-10 text-center font-mono text-[12px] tracking-mono-wide text-nau-fg-dim">
      {text}
    </div>
  );
}

function formatSlotRange(startIso: string, endIso: string): string {
  const start = new Date(startIso);
  const end = new Date(endIso);
  const day = start.toLocaleDateString("de-DE", {
    weekday: "short",
    day: "2-digit",
    month: "2-digit",
  });
  const t = (d: Date) =>
    d.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });
  return `${day} ${t(start)}–${t(end)}`;
}

function formatRelative(iso: string): string {
  const then = new Date(iso).getTime();
  const diffSec = Math.max(0, (Date.now() - then) / 1000);
  if (diffSec < 60) return "gerade eben";
  if (diffSec < 3600) return `vor ${Math.floor(diffSec / 60)} min`;
  if (diffSec < 86400) return `vor ${Math.floor(diffSec / 3600)} h`;
  return new Date(iso).toLocaleDateString("de-DE");
}
