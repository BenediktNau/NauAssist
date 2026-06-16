import { useEffect, useMemo, useRef, useState } from "react";
import type { ComponentType, ReactNode } from "react";
import {
  ArrowLeft,
  ChevronRight,
  Cpu,
  CalendarDays,
  User,
  Bell,
  Mail,
  MessageCircle,
  LogOut,
} from "lucide-react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys, useCalendarSettingsQuery } from "@/hooks/queries";
import { PageLoader } from "@/components/nau/PageLoader";
import type { AppPage } from "@/App";
import {
  getLlmSettings,
  updateLlmSettings,
  getOllamaSettings,
  updateOllamaSettings,
  testOllamaConnection,
  OLLAMA_MODELS,
  type LlmSettings,
  type OllamaSettings,
} from "@/api/settings";
import {
  getCalendarSettings,
  updateCalendarSettings,
  startGoogleAuth,
  completeGoogleAuth,
  disconnectGoogle,
  type CalendarSettings,
} from "@/api/calendar-settings";
import { ImapSection } from "@/components/settings/ImapSection";
import { PersonaSection } from "@/components/settings/PersonaSection";
import { PushSection } from "@/components/settings/PushSection";
import { WhatsAppSection } from "@/components/settings/WhatsAppSection";
import { getCapabilities } from "@/api/capabilities";
import { useAuth } from "@/lib/authContext";

interface SettingsPageProps {
  onNavigate: (page: AppPage) => void;
}

interface RowProps {
  label: string;
  hint?: string;
  children: ReactNode;
}

function Row({ label, hint, children }: RowProps) {
  return (
    <div className="grid grid-cols-1 items-start gap-2 border-b border-nau-line py-4 lg:grid-cols-[260px_1fr] lg:gap-8">
      <div>
        <div
          className="font-sans text-sm font-medium text-nau-fg"
          style={{ marginBottom: hint ? 6 : 0 }}
        >
          {label}
        </div>
        {hint && (
          <div className="max-w-none font-sans text-[13px] leading-relaxed text-nau-fg-dim lg:max-w-[240px]">
            {hint}
          </div>
        )}
      </div>
      <div className="pt-0.5">{children}</div>
    </div>
  );
}

interface SectionHeadProps {
  n: number;
  label: string;
  title: ReactNode;
  kicker?: string;
}

function SectionHead({ n, label, title, kicker }: SectionHeadProps) {
  return (
    <div className="mb-6 pt-2">
      <div className="mb-4 flex items-center gap-3.5">
        <span className="font-mono text-[13px] font-bold text-nau-accent">
          {String(n).padStart(2, "0")}
        </span>
        <span className="h-px w-8 bg-nau-line" />
        <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">{label}</span>
      </div>
      <h2 className="m-0 mb-2 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg">
        {title}
      </h2>
      {kicker && (
        <p className="m-0 max-w-[600px] font-sans text-sm leading-relaxed text-nau-fg-dim">
          {kicker}
        </p>
      )}
    </div>
  );
}

function TextInput({
  value,
  onChange,
  placeholder,
  type = "text",
  disabled = false,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  type?: "text" | "password" | "number";
  disabled?: boolean;
}) {
  return (
    <input
      type={type}
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      onChange={(e) => onChange(e.target.value)}
      className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg disabled:opacity-50 lg:max-w-[480px]"
    />
  );
}

function PrimaryButton({
  children,
  onClick,
  disabled,
}: {
  children: ReactNode;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="min-h-11 cursor-pointer border-none bg-nau-accent px-4 py-3 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40 lg:min-h-0 lg:py-2.5"
    >
      {children}
    </button>
  );
}

function SecondaryButton({
  children,
  onClick,
  disabled,
}: {
  children: ReactNode;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-3 font-mono text-[11px] tracking-mono-wide text-nau-fg disabled:opacity-40 lg:min-h-0 lg:py-2.5"
    >
      {children}
    </button>
  );
}

function ModelCombobox({
  value,
  onCommit,
  suggestions,
  placeholder,
}: {
  value: string;
  onCommit: (v: string) => void;
  suggestions: readonly string[];
  placeholder?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState(value);
  const [lastValue, setLastValue] = useState(value);
  const [open, setOpen] = useState(false);

  if (value !== lastValue) {
    setLastValue(value);
    setDraft(value);
  }

  const trimmedDraft = draft.trim();
  const isBrowseMode = trimmedDraft === "" || trimmedDraft === value;
  const filtered = isBrowseMode
    ? [...suggestions]
    : suggestions.filter((s) => s.toLowerCase().includes(trimmedDraft.toLowerCase()));

  const commit = (v: string) => {
    const trimmed = v.trim();
    if (trimmed === "" || trimmed === value) {
      setDraft(value);
      setOpen(false);
      return;
    }
    setDraft(trimmed);
    setOpen(false);
    onCommit(trimmed);
  };

  return (
    <div className="relative w-full lg:max-w-[480px]">
      <div className="flex">
        <input
          ref={inputRef}
          value={draft}
          onChange={(e) => {
            setDraft(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => {
            setOpen(false);
            commit(draft);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              commit(draft);
              (e.currentTarget as HTMLInputElement).blur();
            } else if (e.key === "Escape") {
              setDraft(value);
              setOpen(false);
            }
          }}
          placeholder={placeholder}
          className="min-h-11 flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
        />
        <button
          type="button"
          onMouseDown={(e) => {
            e.preventDefault();
            const wasFocused = document.activeElement === inputRef.current;
            if (wasFocused) {
              setOpen((o) => !o);
            } else {
              inputRef.current?.focus();
            }
          }}
          aria-label="Vorschläge anzeigen"
          className="min-h-11 cursor-pointer border border-l-0 border-nau-line bg-white/[0.03] px-3 font-mono text-[11px] text-nau-fg-dim"
        >
          ▾
        </button>
      </div>
      {open && filtered.length > 0 && (
        <ul className="absolute left-0 right-0 z-10 mt-1 max-h-60 list-none overflow-auto border border-nau-line bg-nau-bg p-0 shadow-lg">
          {filtered.map((s) => (
            <li key={s}>
              <button
                type="button"
                onMouseDown={(e) => {
                  e.preventDefault();
                  commit(s);
                }}
                className="block w-full cursor-pointer border-none bg-transparent px-3.5 py-2 text-left font-sans text-sm text-nau-fg hover:bg-white/[0.06]"
              >
                {s}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

type SectionKey = "llm" | "calendar" | "persona" | "push" | "imap" | "whatsapp" | "konto";

interface NavMeta {
  label: string;
  hint: string;
  Icon: ComponentType<{ size?: number; strokeWidth?: number }>;
}

const NAV_META: Record<SectionKey, NavMeta> = {
  llm: { label: "Sprachmodell", hint: "Modell · Prompt · Ollama", Icon: Cpu },
  calendar: { label: "Kalender", hint: "Google · Arbeitszeiten", Icon: CalendarDays },
  persona: { label: "Persona", hint: "Was Nau über dich weiß", Icon: User },
  push: { label: "Push", hint: "Benachrichtigungen", Icon: Bell },
  imap: { label: "E-Mail", hint: "IMAP / SMTP Postfächer", Icon: Mail },
  whatsapp: { label: "WhatsApp", hint: "Agent-Nummer", Icon: MessageCircle },
  konto: { label: "Konto", hint: "Anmeldung · Abmelden", Icon: LogOut },
};

interface NavGroup {
  id: string;
  label: string;
  keys: SectionKey[];
}

function buildGroups(caps: Capabilities | null, authEnabled: boolean): NavGroup[] {
  const channels: SectionKey[] = ["push", "imap"];
  if (caps?.whatsApp) channels.push("whatsapp");

  const groups: NavGroup[] = [
    { id: "agent", label: "AGENT", keys: ["llm", "calendar", "persona"] },
    { id: "channels", label: "KANÄLE", keys: channels },
  ];
  if (authEnabled) groups.push({ id: "account", label: "KONTO", keys: ["konto"] });
  return groups;
}

function SettingsLoading() {
  return (
    <div className="border border-nau-line bg-nau-bg-alt p-10 text-center font-mono text-[11px] tracking-mono text-nau-fg-dim">
      // LADE …
    </div>
  );
}

export function SettingsPage({ onNavigate }: SettingsPageProps) {
  const queryClient = useQueryClient();
  const llmQuery = useQuery({ queryKey: queryKeys.llmSettings, queryFn: getLlmSettings });
  const ollamaQuery = useQuery({
    queryKey: queryKeys.ollamaSettings,
    queryFn: getOllamaSettings,
  });
  const calendarQuery = useCalendarSettingsQuery();
  const capsQuery = useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: getCapabilities,
  });
  const auth = useAuth();

  // null = (nur Mobile) Kategorie-Liste sichtbar. Gesetzt = Detail.
  // Auf Desktop wird immer ein Detail gezeigt (`current` fällt auf "llm" zurück).
  const [active, setActive] = useState<SectionKey | null>(null);

  // Lokale Kopien gewinnen nach Saves; Query-Daten füllen initial.
  // Die Setter spiegeln Saves in den Cache, damit andere Seiten
  // (z. B. CalendarBoard via calendar-settings) frische Werte sehen.
  const [llmLocal, setLlmLocal] = useState<LlmSettings | null>(null);
  const [ollamaLocal, setOllamaLocal] = useState<OllamaSettings | null>(null);
  const [calendarLocal, setCalendarLocal] = useState<CalendarSettings | null>(null);

  const llm = llmLocal ?? llmQuery.data ?? null;
  const ollama = ollamaLocal ?? ollamaQuery.data ?? null;
  const calendar = calendarLocal ?? calendarQuery.data ?? null;

  const setLlm = (l: LlmSettings) => {
    setLlmLocal(l);
    queryClient.setQueryData(queryKeys.llmSettings, l);
  };
  const setOllama = (o: OllamaSettings) => {
    setOllamaLocal(o);
    queryClient.setQueryData(queryKeys.ollamaSettings, o);
  };
  const setCalendar = (c: CalendarSettings) => {
    setCalendarLocal(c);
    queryClient.setQueryData(queryKeys.calendarSettings, c);
  };

  // Capabilities-Fehler → konservative Defaults (wie bisheriger catch-Fallback).
  const caps =
    capsQuery.data ??
    (capsQuery.isError
      ? { whatsApp: false, auth: { enabled: false, loginUrl: "/auth/login" } }
      : null);

  const topError =
    llmQuery.error?.message ?? ollamaQuery.error?.message ?? calendarQuery.error?.message ?? null;

  const initialPending =
    llmQuery.isPending || ollamaQuery.isPending || calendarQuery.isPending || capsQuery.isPending;

  const groups = useMemo(() => buildGroups(caps, auth.enabled), [caps, auth.enabled]);
  const current: SectionKey = active ?? "llm";

  const sectionContent = (key: SectionKey): ReactNode => {
    switch (key) {
      case "llm":
        return llm && ollama ? (
          <LlmSection llm={llm} setLlm={setLlm} ollama={ollama} setOllama={setOllama} />
        ) : (
          <SettingsLoading />
        );
      case "calendar":
        return calendar ? (
          <CalendarSection calendar={calendar} setCalendar={setCalendar} />
        ) : (
          <SettingsLoading />
        );
      case "persona":
        return <PersonaSection anchor="section-persona" />;
      case "push":
        return <PushSection anchor="section-push" />;
      case "imap":
        return <ImapSection anchor="section-imap" />;
      case "whatsapp":
        return <WhatsAppSection anchor="section-whatsapp" />;
      case "konto":
        return <AccountFooter />;
    }
  };

  return (
    <div className="min-h-screen bg-nau-bg text-nau-fg lg:grid lg:grid-cols-[260px_1fr]">
      {/* ── Desktop-Sidebar (gruppiert) ─────────────────────────── */}
      <aside className="sticky top-0 hidden h-screen flex-col gap-6 overflow-y-auto border-r border-nau-line px-5 py-7 lg:flex">
        <button
          type="button"
          onClick={() => onNavigate("chat")}
          className="flex cursor-pointer items-center gap-3 bg-transparent"
          aria-label="Zurück zum Chat"
        >
          <span className="inline-flex h-7 w-7 items-center justify-center bg-nau-accent font-mono text-[13px] font-bold text-nau-bg">
            N
          </span>
          <span className="font-sans text-[15px] font-semibold text-nau-fg">NauAssist</span>
        </button>

        <nav className="flex flex-col gap-5">
          {groups.map((g) => (
            <div key={g.id} className="flex flex-col gap-0.5">
              <div className="px-2 pb-1.5 font-mono text-[9.5px] tracking-mono-xwide text-nau-fg-dim">
                {g.label}
              </div>
              {g.keys.map((k) => {
                const m = NAV_META[k];
                const on = current === k;
                return (
                  <button
                    key={k}
                    type="button"
                    onClick={() => setActive(k)}
                    aria-current={on ? "page" : undefined}
                    className={
                      "flex cursor-pointer items-center gap-3 border-l-2 px-3 py-2.5 text-left transition-colors " +
                      (on
                        ? "border-nau-accent bg-nau-accent/10 text-nau-fg"
                        : "border-transparent bg-transparent text-nau-fg-dim hover:text-nau-fg")
                    }
                  >
                    <m.Icon size={18} strokeWidth={on ? 1.9 : 1.6} />
                    <span className="whitespace-nowrap font-sans text-sm">{m.label}</span>
                  </button>
                );
              })}
            </div>
          ))}
        </nav>
      </aside>

      {initialPending ? (
        <main className="flex min-h-screen flex-col">
          <PageLoader label="LADE EINSTELLUNGEN" />
        </main>
      ) : (
        <>
          {/* ── Mobile: Kategorie-Liste (nur wenn keine Sektion aktiv) ─ */}
          {active === null && (
            <div className="flex min-h-screen flex-col lg:hidden">
              <div className="flex items-center gap-3 border-b border-nau-line px-4 py-4">
                <button
                  type="button"
                  onClick={() => onNavigate("chat")}
                  aria-label="Zurück zum Chat"
                  className="inline-flex h-10 w-10 items-center justify-center text-nau-fg-dim transition-colors hover:text-nau-accent"
                >
                  <ArrowLeft size={20} strokeWidth={1.5} />
                </button>
                <span className="font-mono text-[11px] tracking-mono-xwide text-nau-fg-dim">
                  // EINSTELLUNGEN
                </span>
              </div>
              <div className="flex-1 px-4 py-5">
                {groups.map((g) => (
                  <div key={g.id} className="mb-6">
                    <div className="px-1 pb-2 font-mono text-[9.5px] tracking-mono-xwide text-nau-fg-dim">
                      {g.label}
                    </div>
                    <div className="border border-nau-line">
                      {g.keys.map((k, i) => {
                        const m = NAV_META[k];
                        return (
                          <button
                            key={k}
                            type="button"
                            onClick={() => setActive(k)}
                            className={
                              "flex w-full items-center gap-4 bg-nau-bg-alt px-4 py-4 text-left " +
                              (i > 0 ? "border-t border-nau-line" : "")
                            }
                          >
                            <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center border border-nau-line text-nau-accent">
                              <m.Icon size={18} />
                            </span>
                            <span className="min-w-0 flex-1">
                              <span className="block font-sans text-[15px] font-medium text-nau-fg">
                                {m.label}
                              </span>
                              <span className="mt-0.5 block font-mono text-[10px] tracking-mono text-nau-fg-dim">
                                {m.hint}
                              </span>
                            </span>
                            <ChevronRight size={18} className="shrink-0 text-nau-fg-dim" />
                          </button>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* ── Detail: Desktop immer, Mobile nur bei aktiver Sektion ── */}
          <main className={(active === null ? "hidden lg:block" : "block") + " min-h-screen"}>
            {/* Mobile-Zurück-Leiste */}
            <div className="flex items-center border-b border-nau-line px-2 py-3 lg:hidden">
              <button
                type="button"
                onClick={() => setActive(null)}
                className="inline-flex items-center gap-1.5 bg-transparent px-2 py-2 font-mono text-[11px] tracking-mono text-nau-accent"
              >
                <ArrowLeft size={18} strokeWidth={1.7} /> EINSTELLUNGEN
              </button>
            </div>

            <div className="mx-auto max-w-[820px] px-4 pb-16 pt-6 lg:px-14 lg:pb-20 lg:pt-10">
              {topError && (
                <div className="mb-6 border border-nau-danger bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
                  // SETTINGS NICHT LADBAR — {topError}
                </div>
              )}
              {sectionContent(current)}
            </div>
          </main>
        </>
      )}
    </div>
  );
}

/** Nur sichtbar mit aktivierter Keycloak-Auth: eingeloggter User + Logout. */
function AccountFooter() {
  const auth = useAuth();
  if (!auth.enabled) return null;

  return (
    <div className="flex flex-col items-start gap-4">
      <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
        // ANGEMELDET ALS {(auth.username ?? auth.email ?? "?").toUpperCase()}
      </span>
      <SecondaryButton onClick={() => void auth.logout()}>ABMELDEN</SecondaryButton>
    </div>
  );
}

function LlmSection({
  llm,
  setLlm,
  ollama,
  setOllama,
}: {
  llm: LlmSettings;
  setLlm: (l: LlmSettings) => void;
  ollama: OllamaSettings;
  setOllama: (o: OllamaSettings) => void;
}) {
  const [llmError, setLlmError] = useState<string | null>(null);
  const [savedFlash, setSavedFlash] = useState(false);
  const [systemPromptDraft, setSystemPromptDraft] = useState(llm.systemPrompt ?? "");

  const [showAdvanced, setShowAdvanced] = useState(false);
  const [hostDraft, setHostDraft] = useState(ollama.host);
  const [apiKeyDraft, setApiKeyDraft] = useState("");
  const [numCtxDraft, setNumCtxDraft] = useState(String(ollama.numCtx));
  const [tempDraft, setTempDraft] = useState(String(ollama.temperature));
  const [editingOllamaKey, setEditingOllamaKey] = useState(false);
  const [ollamaSaving, setOllamaSaving] = useState(false);
  const [ollamaError, setOllamaError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);
  const [availableModels, setAvailableModels] = useState<string[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    testOllamaConnection(ollama.host, null)
      .then((r) => {
        if (cancelled) return;
        if (r.ok && r.models && r.models.length > 0) {
          setAvailableModels(r.models);
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [ollama.host]);

  const ollamaDirty =
    hostDraft !== ollama.host ||
    numCtxDraft !== String(ollama.numCtx) ||
    tempDraft !== String(ollama.temperature) ||
    editingOllamaKey;

  const systemPromptDirty =
    (systemPromptDraft.trim() === "" ? null : systemPromptDraft.trim()) !==
    (llm.systemPrompt ?? null);

  const saveLlm = async (patch: { ollamaModel?: string; systemPrompt?: string | null }) => {
    setLlmError(null);
    try {
      await updateLlmSettings({
        ollamaModel: patch.ollamaModel ?? llm.ollamaModel,
        systemPrompt: patch.systemPrompt !== undefined ? patch.systemPrompt : llm.systemPrompt,
      });
      const fresh = await getLlmSettings();
      setLlm(fresh);
      setSystemPromptDraft(fresh.systemPrompt ?? "");
      setSavedFlash(true);
      setTimeout(() => setSavedFlash(false), 2500);
    } catch (e) {
      setLlmError(String((e as Error).message ?? e));
    }
  };

  const saveOllama = async () => {
    setOllamaSaving(true);
    setOllamaError(null);
    try {
      const apiKey = editingOllamaKey ? apiKeyDraft : null;
      await updateOllamaSettings({
        host: hostDraft,
        apiKey,
        numCtx: parseInt(numCtxDraft, 10),
        temperature: parseFloat(tempDraft),
      });
      const fresh = await getOllamaSettings();
      setOllama(fresh);
      setHostDraft(fresh.host);
      setNumCtxDraft(String(fresh.numCtx));
      setTempDraft(String(fresh.temperature));
      setApiKeyDraft("");
      setEditingOllamaKey(false);
    } catch (e) {
      setOllamaError(String((e as Error).message ?? e));
    } finally {
      setOllamaSaving(false);
    }
  };

  const runTest = async () => {
    setTestResult("// TESTE …");
    const r = await testOllamaConnection(hostDraft, editingOllamaKey ? apiKeyDraft : null);
    if (r.ok) {
      if (r.models && r.models.length > 0) {
        setAvailableModels(r.models);
      }
      setTestResult(`// ERREICHBAR · ${r.models?.length ?? 0} MODELLE`);
    } else {
      setTestResult(`// FEHLER: ${r.error ?? "unbekannt"}`);
    }
  };

  return (
    <div id="section-llm">
      <SectionHead n={1} label="SPRACHMODELL" title="Wie Nau denkt." />

      <Row label="Modell" hint="Vorschläge — oder eigenes lokal gepulltes Modell eintippen.">
        <ModelCombobox
          value={llm.ollamaModel}
          onCommit={(v) => saveLlm({ ollamaModel: v })}
          suggestions={availableModels ?? OLLAMA_MODELS}
          placeholder="z.B. gemma4:26b"
        />
      </Row>

      <Row
        label="System-Prompt"
        hint="Wer Nau ist und wie er sich verhalten soll. Leer = Default aus der Konfig."
      >
        <div className="flex w-full flex-col gap-2 lg:max-w-[640px]">
          <textarea
            value={systemPromptDraft}
            onChange={(e) => setSystemPromptDraft(e.target.value)}
            placeholder={
              llm.defaultSystemPrompt || "Du bist NauAssist, ein persönlicher Kalender-Agent."
            }
            rows={6}
            className="min-h-[140px] w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm leading-relaxed text-nau-fg"
          />
          <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
            <PrimaryButton
              onClick={() =>
                saveLlm({
                  systemPrompt: systemPromptDraft.trim() === "" ? null : systemPromptDraft,
                })
              }
              disabled={!systemPromptDirty}
            >
              PROMPT SPEICHERN ↵
            </PrimaryButton>
            <SecondaryButton
              onClick={() => {
                setSystemPromptDraft("");
                saveLlm({ systemPrompt: null });
              }}
              disabled={llm.systemPrompt === null}
            >
              AUF DEFAULT ZURÜCKSETZEN
            </SecondaryButton>
            {llm.systemPrompt === null && (
              <span className="font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
                // NUTZT DEFAULT
              </span>
            )}
          </div>
        </div>
      </Row>

      <Row label="Ollama erweitert" hint="Host, API-Key, Kontext-Größe, Temperatur.">
        <button
          type="button"
          onClick={() => setShowAdvanced(!showAdvanced)}
          className="cursor-pointer bg-transparent font-mono text-[11px] tracking-mono-wide text-nau-fg-dim"
        >
          {showAdvanced ? "▼ EINKLAPPEN" : "▶ AUSKLAPPEN"}
        </button>
      </Row>

      {showAdvanced && (
        <>
          <Row
            label="Ollama-Host"
            hint="Z.B. http://localhost:11434 oder hinter einem Reverse-Proxy."
          >
            <div className="flex w-full flex-col gap-2 lg:max-w-[480px]">
              <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
                <TextInput
                  value={hostDraft}
                  onChange={setHostDraft}
                  placeholder="http://localhost:11434"
                />
                <SecondaryButton onClick={runTest}>TESTEN</SecondaryButton>
              </div>
              {testResult && (
                <div className="font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
                  {testResult}
                </div>
              )}
            </div>
          </Row>

          <Row label="Ollama API-Key" hint="Optional. Bearer-Token für Reverse-Proxy-Endpoints.">
            {ollama.hasApiKey && !editingOllamaKey ? (
              <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
                <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
                  •••••• GESPEICHERT
                </span>
                <SecondaryButton onClick={() => setEditingOllamaKey(true)}>ÄNDERN</SecondaryButton>
              </div>
            ) : (
              <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
                <input
                  type="password"
                  value={apiKeyDraft}
                  onChange={(e) => {
                    setApiKeyDraft(e.target.value);
                    setEditingOllamaKey(true);
                  }}
                  placeholder="optional"
                  className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg lg:max-w-[360px] lg:flex-1"
                />
                {ollama.hasApiKey && (
                  <SecondaryButton
                    onClick={() => {
                      setEditingOllamaKey(false);
                      setApiKeyDraft("");
                    }}
                  >
                    ABBRECHEN
                  </SecondaryButton>
                )}
              </div>
            )}
          </Row>

          <Row label="NumCtx" hint="Kontextfenster in Tokens (8192 / 16384 / …).">
            <TextInput type="number" value={numCtxDraft} onChange={setNumCtxDraft} />
          </Row>

          <Row label="Temperature" hint="0.0 = deterministisch, 1.0 = kreativ.">
            <TextInput type="number" value={tempDraft} onChange={setTempDraft} />
          </Row>

          <div className="flex flex-col gap-3 border-b border-nau-line py-4 lg:flex-row lg:items-center">
            <PrimaryButton onClick={saveOllama} disabled={!ollamaDirty || ollamaSaving}>
              OLLAMA SPEICHERN ↵
            </PrimaryButton>
            {ollamaError && (
              <span className="font-mono text-[10px] tracking-mono-wide text-nau-danger">
                // FEHLER: {ollamaError}
              </span>
            )}
          </div>
        </>
      )}

      {savedFlash && (
        <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-accent">
          // MODELL AKTUALISIERT — WIRD AB DEINER NÄCHSTEN NACHRICHT GENUTZT
        </div>
      )}
      {llmError && (
        <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-danger">
          // FEHLER: {llmError}
        </div>
      )}
    </div>
  );
}

function CalendarSection({
  calendar,
  setCalendar,
}: {
  calendar: CalendarSettings;
  setCalendar: (c: CalendarSettings) => void;
}) {
  const [calendarId, setCalendarId] = useState(calendar.calendarId);
  const [whStart, setWhStart] = useState(calendar.workingHoursStart);
  const [whEnd, setWhEnd] = useState(calendar.workingHoursEnd);
  const [defaultDur, setDefaultDur] = useState(String(calendar.defaultDurationMinutes));
  const [horizon, setHorizon] = useState(String(calendar.searchHorizonDays));

  const [clientIdDraft, setClientIdDraft] = useState("");
  const [clientSecretDraft, setClientSecretDraft] = useState("");
  const [editingCreds, setEditingCreds] = useState(false);

  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [authState, setAuthState] = useState<{ url: string; sessionId: string } | null>(null);
  const [authCode, setAuthCode] = useState("");
  const [authError, setAuthError] = useState<string | null>(null);

  const dirty =
    calendarId !== calendar.calendarId ||
    whStart !== calendar.workingHoursStart ||
    whEnd !== calendar.workingHoursEnd ||
    defaultDur !== String(calendar.defaultDurationMinutes) ||
    horizon !== String(calendar.searchHorizonDays) ||
    editingCreds;

  const save = async () => {
    setSaving(true);
    setSaveError(null);
    try {
      await updateCalendarSettings({
        calendarId,
        workingHoursStart: whStart,
        workingHoursEnd: whEnd,
        defaultDurationMinutes: parseInt(defaultDur, 10),
        searchHorizonDays: parseInt(horizon, 10),
        googleClientId: editingCreds ? clientIdDraft : null,
        googleClientSecret: editingCreds ? clientSecretDraft : null,
      });
      const fresh = await getCalendarSettings();
      setCalendar(fresh);
      setEditingCreds(false);
      setClientIdDraft("");
      setClientSecretDraft("");
    } catch (e) {
      setSaveError(String((e as Error).message ?? e));
    } finally {
      setSaving(false);
    }
  };

  const startAuth = async () => {
    setAuthError(null);
    try {
      const r = await startGoogleAuth();
      setAuthState({ url: r.authUrl, sessionId: r.sessionId });
    } catch (e) {
      setAuthError(String((e as Error).message ?? e));
    }
  };

  const completeAuth = async () => {
    if (!authState) return;
    setAuthError(null);
    try {
      await completeGoogleAuth(authState.sessionId, authCode.trim());
      const fresh = await getCalendarSettings();
      setCalendar(fresh);
      setAuthState(null);
      setAuthCode("");
    } catch (e) {
      setAuthError(String((e as Error).message ?? e));
    }
  };

  const disconnect = async () => {
    await disconnectGoogle();
    const fresh = await getCalendarSettings();
    setCalendar(fresh);
  };

  return (
    <div id="section-calendar">
      <SectionHead
        n={2}
        label="KALENDER"
        title="Google-Kalender."
        kicker="OAuth-Credentials + Verhalten von Nau gegenüber deinem Kalender."
      />

      <Row label="Verbindungsstatus" hint="">
        <div className="flex flex-col items-start gap-3 lg:flex-row lg:items-center">
          <span
            className="px-2.5 py-1 font-mono text-[10px] tracking-mono-wide"
            style={{
              color: calendar.isConnected ? "#facc15" : "#888885",
              border: calendar.isConnected
                ? "1px solid #facc15"
                : "1px solid rgba(255,255,255,0.10)",
              background: calendar.isConnected ? "rgba(250,204,21,0.08)" : "transparent",
            }}
          >
            {calendar.isConnected ? "● VERBUNDEN" : "○ NICHT VERBUNDEN"}
          </span>
          {calendar.isConnected ? (
            <SecondaryButton onClick={disconnect}>TRENNEN</SecondaryButton>
          ) : (
            <PrimaryButton onClick={startAuth} disabled={!calendar.hasGoogleCredentials}>
              MIT GOOGLE VERBINDEN →
            </PrimaryButton>
          )}
        </div>
      </Row>

      {authState && (
        <div className="border border-nau-line bg-white/[0.015] px-4 py-4 my-4">
          <div className="mb-3 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            // GOOGLE-AUTORISIERUNG
          </div>
          <ol className="m-0 mb-3 list-decimal pl-5 font-sans text-[13px] leading-relaxed text-nau-fg-dim">
            <li>Öffne die URL in einem Browser und klicke „Erlauben".</li>
            <li>
              Nach „Erlauben" landet der Browser auf einer nicht-erreichbaren Seite (Absicht).
            </li>
            <li>
              Kopiere aus der Adresszeile den Wert hinter <code>code=</code> bis zum nächsten{" "}
              <code>&amp;</code>.
            </li>
          </ol>
          <div className="mb-3 max-w-[600px] break-all border border-nau-line bg-nau-bg px-3 py-2 font-mono text-[11px] text-nau-fg">
            {authState.url}
          </div>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
            <input
              type="text"
              value={authCode}
              onChange={(e) => setAuthCode(e.target.value)}
              placeholder="code=..."
              className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg lg:max-w-[360px] lg:flex-1"
            />
            <PrimaryButton onClick={completeAuth} disabled={authCode.trim().length === 0}>
              CODE ÜBERMITTELN ↵
            </PrimaryButton>
            <SecondaryButton
              onClick={() => {
                setAuthState(null);
                setAuthCode("");
              }}
            >
              ABBRECHEN
            </SecondaryButton>
          </div>
          {authError && (
            <div className="mt-3 font-mono text-[10px] tracking-mono-wide text-nau-danger">
              // FEHLER: {authError}
            </div>
          )}
        </div>
      )}

      {!calendar.hasGoogleCredentials && !authState && (
        <div className="my-4 border border-nau-line bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
          // GOOGLE-CLIENT-ID / -SECRET FEHLEN — BITTE UNTEN EINTRAGEN UND SPEICHERN
        </div>
      )}

      <Row label="Google Client-ID" hint="Aus Google Cloud Console → OAuth-Client (Desktop App).">
        {calendar.hasGoogleCredentials && !editingCreds ? (
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
            <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">GESPEICHERT</span>
            <SecondaryButton onClick={() => setEditingCreds(true)}>ÄNDERN</SecondaryButton>
          </div>
        ) : (
          <TextInput
            value={clientIdDraft}
            onChange={(v) => {
              setClientIdDraft(v);
              setEditingCreds(true);
            }}
            placeholder="123-abc.apps.googleusercontent.com"
          />
        )}
      </Row>

      <Row label="Google Client-Secret" hint="Aus derselben OAuth-Client-Definition.">
        {calendar.hasGoogleCredentials && !editingCreds ? (
          <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
            •••••• GESPEICHERT
          </span>
        ) : (
          <TextInput
            type="password"
            value={clientSecretDraft}
            onChange={(v) => {
              setClientSecretDraft(v);
              setEditingCreds(true);
            }}
            placeholder="GOCSPX-..."
          />
        )}
      </Row>

      <Row label="Calendar-ID" hint='„primary" oder eine konkrete Kalender-Adresse.'>
        <TextInput value={calendarId} onChange={setCalendarId} />
      </Row>

      <Row label="Arbeitszeiten" hint="Außerhalb dieser Zeiten schlägt Nau nichts vor.">
        <div className="flex items-center gap-2 lg:gap-3">
          <TextInput value={whStart} onChange={setWhStart} placeholder="09:00" />
          <span className="font-mono text-xs text-nau-fg-dim">→</span>
          <TextInput value={whEnd} onChange={setWhEnd} placeholder="18:00" />
        </div>
      </Row>

      <Row label="Standard-Dauer" hint="Wird genutzt, wenn du keine Dauer angibst (Minuten).">
        <TextInput type="number" value={defaultDur} onChange={setDefaultDur} />
      </Row>

      <Row label="Such-Horizont" hint="Wie viele Tage in die Zukunft Nau plant.">
        <TextInput type="number" value={horizon} onChange={setHorizon} />
      </Row>

      <div className="flex flex-col gap-3 py-4 lg:flex-row lg:items-center">
        <PrimaryButton onClick={save} disabled={!dirty || saving}>
          KALENDER SPEICHERN ↵
        </PrimaryButton>
        {saveError && (
          <span className="font-mono text-[10px] tracking-mono-wide text-nau-danger">
            // FEHLER: {saveError}
          </span>
        )}
      </div>
    </div>
  );
}
