// ─────────────────────────────────────────────────────────────────────────────
// SettingsPage – Master-Detail-Umbau (Variante aus Settings-Redesign.html)
//
// NUR die äußere `SettingsPage`-Funktion wird ersetzt + ein paar Imports und ein
// Navigations-Modell ergänzt. ALLE bestehenden Helfer und Sektions-Komponenten
// bleiben unverändert:
//   Row, SectionHead, TextInput, PrimaryButton, SecondaryButton, ModelCombobox,
//   LlmSection, CalendarSection, AccountFooter
//   sowie PersonaSection / PushSection / ImapSection / WhatsAppSection (eigene Files)
//
// → Siehe INTEGRATION.md für die genauen Schritte.
// ─────────────────────────────────────────────────────────────────────────────

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║ 1) IMPORTS — bestehende Zeilen entsprechend erweitern                      ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// react: useMemo ergänzen
import { useEffect, useMemo, useRef, useState } from "react";
// Typen: ComponentType ergänzen (ReactNode bleibt)
import type { ComponentType, ReactNode } from "react";
// lucide: Icons ergänzen (ArrowLeft war schon da)
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
// (useAuth, alle API-Imports und die Section-Imports bleiben unverändert.)

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║ 2) NAVIGATIONS-MODELL — neu, z.B. direkt über `export function SettingsPage`║
// ╚═══════════════════════════════════════════════════════════════════════════╝

type SectionKey =
  | "llm"
  | "calendar"
  | "persona"
  | "push"
  | "imap"
  | "whatsapp"
  | "konto";

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

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║ 3) SettingsPage — komplette Funktion ERSETZEN                              ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

export function SettingsPage({ onNavigate }: SettingsPageProps) {
  const [llm, setLlm] = useState<LlmSettings | null>(null);
  const [ollama, setOllama] = useState<OllamaSettings | null>(null);
  const [calendar, setCalendar] = useState<CalendarSettings | null>(null);
  const [caps, setCaps] = useState<Capabilities | null>(null);
  const [topError, setTopError] = useState<string | null>(null);
  const auth = useAuth();

  // null = (nur Mobile) Kategorie-Liste sichtbar. Gesetzt = Detail.
  // Auf Desktop wird immer ein Detail gezeigt (`current` fällt auf "llm" zurück).
  const [active, setActive] = useState<SectionKey | null>(null);

  useEffect(() => {
    Promise.all([getLlmSettings(), getOllamaSettings(), getCalendarSettings()])
      .then(([l, o, c]) => {
        setLlm(l);
        setOllama(o);
        setCalendar(c);
      })
      .catch((e) => setTopError(String(e.message ?? e)));
  }, []);

  useEffect(() => {
    getCapabilities()
      .then(setCaps)
      .catch(() => setCaps({ whatsApp: false, auth: { enabled: false, loginUrl: "/auth/login" } }));
  }, []);

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
    </div>
  );
}
