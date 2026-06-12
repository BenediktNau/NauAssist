import { useEffect, useMemo, useRef, useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/hooks/queries";
import { PageLoader } from "@/components/nau/PageLoader";
import { Header } from "@/components/nau/Header";
import { MobileTabBar } from "@/components/nau/MobileTabBar";
import type { AppPage } from "@/App";
import {
  dismissSuggestion,
  listSuggestions,
  pickSuggestionSlot,
  pollSuggestionsNow,
  sendSuggestion,
  updateSuggestionDraft,
  type SuggestionDto,
  type SuggestionStatus,
} from "@/api/suggestions";

interface RecommendationsPageProps {
  onNavigate: (page: AppPage) => void;
  /** Wenn gesetzt, scrollt die Seite nach Laden zu dieser Suggestion-ID (Deep-Link). */
  focusSuggestionId?: number | null;
  /** Wird gerufen, sobald gescrollt wurde — Parent kann den Focus zurücksetzen. */
  onFocusHandled?: () => void;
}

const STATUS_TABS: { key: SuggestionStatus | "all"; label: string }[] = [
  { key: "pending", label: "OFFEN" },
  { key: "responded", label: "ERLEDIGT" },
  { key: "dismissed", label: "VERWORFEN" },
  { key: "all", label: "ALLE" },
];

export function RecommendationsPage({
  onNavigate,
  focusSuggestionId,
  onFocusHandled,
}: RecommendationsPageProps) {
  const [filter, setFilter] = useState<SuggestionStatus | "all">("pending");
  const [polling, setPolling] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pollMessage, setPollMessage] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const suggestionsQuery = useQuery({
    queryKey: queryKeys.suggestions(filter),
    queryFn: () => listSuggestions(filter === "all" ? undefined : filter),
    placeholderData: keepPreviousData,
  });
  const items = useMemo(() => suggestionsQuery.data ?? [], [suggestionsQuery.data]);
  const reloadSuggestions = () =>
    queryClient.invalidateQueries({ queryKey: queryKeys.suggestionsPrefix });

  // Wenn ein Deep-Link-Ziel auch in "responded"/"dismissed" liegt, müssen wir den
  // Status-Filter ggf. lockern, damit der Eintrag in der Liste ist.
  useEffect(() => {
    if (focusSuggestionId && filter === "pending") {
      const inList = items.some((s) => s.id === focusSuggestionId);
      if (!inList && items.length > 0) setFilter("all");
    }
  }, [focusSuggestionId, items, filter]);

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
      await reloadSuggestions();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setPolling(false);
    }
  };

  const replaceItem = (updated: SuggestionDto) =>
    queryClient.setQueryData<SuggestionDto[]>(queryKeys.suggestions(filter), (prev) =>
      prev?.map((s) => (s.id === updated.id ? updated : s)),
    );

  const onPick = async (id: number, slotIndex: number) => {
    setError(null);
    try {
      const updated = await pickSuggestionSlot(id, slotIndex);
      replaceItem(updated);
      void reloadSuggestions();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const onDismiss = async (id: number) => {
    setError(null);
    try {
      await dismissSuggestion(id);
      await reloadSuggestions();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const onSaveDraft = async (id: number, text: string) => {
    setError(null);
    try {
      const updated = await updateSuggestionDraft(id, text);
      replaceItem(updated);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const onSend = async (id: number, text: string) => {
    setError(null);
    try {
      const updated = await sendSuggestion(id, text);
      replaceItem(updated);
      void reloadSuggestions();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const displayError =
    error ?? (suggestionsQuery.error ? suggestionsQuery.error.message : null);

  return (
    <div className="flex min-h-screen flex-col bg-nau-bg text-nau-fg pb-[calc(3.5rem+env(safe-area-inset-bottom))] lg:pb-0">
      <Header onOpenSettings={() => onNavigate("settings")} />
      <div className="mx-auto w-full max-w-[1100px] flex-1 px-4 py-6 lg:px-8 lg:py-10">
        {suggestionsQuery.isPending ? (
          <PageLoader label="LADE EMPFEHLUNGEN" />
        ) : (
          <>
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
            {displayError && (
              <div className="mb-4 border border-nau-danger bg-white/[0.02] px-4 py-3 font-mono text-sm text-nau-danger">
                [ ! ] {displayError}
              </div>
            )}

            {items.length === 0 ? (
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
                  <SuggestionCard
                    key={s.id}
                    suggestion={s}
                    onPick={onPick}
                    onDismiss={onDismiss}
                    onSaveDraft={onSaveDraft}
                    onSend={onSend}
                    focused={focusSuggestionId === s.id}
                    onFocusHandled={onFocusHandled}
                  />
                ))}
              </ul>
            )}
          </>
        )}
      </div>

      <MobileTabBar current="recommendations" onSelect={onNavigate} />
    </div>
  );
}

interface SuggestionCardProps {
  suggestion: SuggestionDto;
  onPick: (id: number, slotIndex: number) => void;
  onDismiss: (id: number) => void;
  onSaveDraft: (id: number, text: string) => Promise<void> | void;
  onSend: (id: number, text: string) => Promise<void> | void;
  focused?: boolean;
  onFocusHandled?: () => void;
}

function SuggestionCard({
  suggestion,
  onPick,
  onDismiss,
  onSaveDraft,
  onSend,
  focused,
  onFocusHandled,
}: SuggestionCardProps) {
  const isPending = suggestion.status === "pending";
  const hasPick = suggestion.pickedSlot !== null && suggestion.pickedSlot !== undefined;

  const [draft, setDraft] = useState(suggestion.draftReply);
  const [serverDraft, setServerDraft] = useState(suggestion.draftReply);
  const [saving, setSaving] = useState(false);
  const [sending, setSending] = useState(false);
  const [copied, setCopied] = useState(false);
  const debounceRef = useRef<number | null>(null);
  const liRef = useRef<HTMLLIElement | null>(null);

  useEffect(() => {
    if (focused && liRef.current) {
      liRef.current.scrollIntoView({ behavior: "smooth", block: "center" });
      onFocusHandled?.();
    }
  }, [focused, onFocusHandled]);

  useEffect(() => {
    // Backend hat den Draft aktualisiert (z.B. nach Slot-Pick) — übernehmen,
    // aber nur wenn der User nicht gerade selbst editiert.
    if (serverDraft !== suggestion.draftReply && draft === serverDraft) {
      setDraft(suggestion.draftReply);
    }
    setServerDraft(suggestion.draftReply);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [suggestion.draftReply]);

  const scheduleSave = (text: string) => {
    setDraft(text);
    if (debounceRef.current !== null) {
      window.clearTimeout(debounceRef.current);
    }
    debounceRef.current = window.setTimeout(async () => {
      if (text === serverDraft) return;
      setSaving(true);
      try {
        await onSaveDraft(suggestion.id, text);
      } finally {
        setSaving(false);
      }
    }, 600);
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(draft);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard kann ohne https blockt sein — fallback ohne UI-Hint
    }
  };

  const send = async () => {
    const sourceLabel = suggestion.source === "matrix" ? "Matrix" : suggestion.source;
    const requester = suggestion.requester ? ` an ${suggestion.requester}` : "";
    if (!confirm(`Antwort jetzt via ${sourceLabel}${requester} senden?`)) return;
    setSending(true);
    try {
      await onSend(suggestion.id, draft);
    } finally {
      setSending(false);
    }
  };

  return (
    <li
      ref={liRef}
      className={
        "border bg-white/[0.02] p-4 lg:p-5 transition-colors " +
        (focused ? "border-nau-accent" : "border-nau-line")
      }
    >
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
        <span
          className={
            "ml-auto font-mono text-[10px] tracking-mono-wide " +
            (suggestion.status === "responded" ? "text-nau-accent" : "text-nau-fg-dim")
          }
        >
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

      {(hasPick || draft) && (
        <div className="mt-4">
          <div className="mb-1 flex items-center gap-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
            <span>// ANTWORT-ENTWURF</span>
            {saving && <span className="text-nau-accent">SPEICHERE …</span>}
          </div>
          <textarea
            value={draft}
            onChange={(e) => scheduleSave(e.target.value)}
            disabled={!isPending}
            rows={3}
            className="w-full resize-y border border-nau-line bg-white/[0.03] px-3.5 py-2.5 font-sans text-sm leading-relaxed text-nau-fg disabled:opacity-60"
          />
        </div>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        {(hasPick || draft) && (
          <button
            type="button"
            onClick={copy}
            disabled={!draft}
            className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-40"
          >
            {copied ? "KOPIERT ✓" : "KOPIEREN"}
          </button>
        )}
        {isPending && hasPick && (
          <button
            type="button"
            onClick={send}
            disabled={sending || !draft}
            className="min-h-10 cursor-pointer border-none bg-nau-accent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40"
          >
            {sending ? "SENDE …" : `IN ${suggestion.source.toUpperCase()} SENDEN`}
          </button>
        )}
        {isPending && (
          <button
            type="button"
            onClick={() => onDismiss(suggestion.id)}
            className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-danger hover:text-nau-danger"
          >
            VERWERFEN
          </button>
        )}
      </div>
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
