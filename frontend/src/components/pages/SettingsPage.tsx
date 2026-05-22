import { useEffect, useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import type { AppPage } from "@/App";
import {
  getLlmSettings,
  updateLlmSettings,
  OLLAMA_MODELS,
  GEMINI_MODELS,
  type LlmSettings,
} from "@/api/settings";

interface SettingsPageProps {
  onNavigate: (page: AppPage) => void;
}

// ─── Form primitives ─────────────────────────────────────────

interface RowProps {
  label: string;
  hint?: string;
  mono?: boolean;
  children: ReactNode;
}

function Row({ label, hint, mono, children }: RowProps) {
  return (
    <div
      className="grid items-start gap-8 border-b border-nau-line py-4"
      style={{ gridTemplateColumns: "260px 1fr" }}
    >
      <div>
        <div
          className={
            "text-nau-fg " +
            (mono
              ? "font-mono text-xs font-medium tracking-mono"
              : "font-sans text-sm font-medium")
          }
          style={{ marginBottom: hint ? 6 : 0 }}
        >
          {label}
        </div>
        {hint && (
          <div className="max-w-[240px] font-sans text-[13px] leading-relaxed text-nau-fg-dim">
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

interface TxtFieldProps {
  value?: string;
  placeholder?: string;
  suffix?: string;
}

function TxtField({ value, placeholder, suffix }: TxtFieldProps) {
  return (
    <div className="flex max-w-[480px] items-center border border-nau-line bg-white/[0.03] px-3.5 py-3">
      <span
        className="flex-1 font-sans text-sm"
        style={{ color: value ? "#f5f5f4" : "#888885" }}
      >
        {value || placeholder}
      </span>
      {suffix && (
        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">{suffix}</span>
      )}
    </div>
  );
}

function Toggle({ on = false }: { on?: boolean }) {
  return (
    <span className="inline-flex items-center gap-3">
      <span
        className="relative inline-block"
        style={{
          width: 36,
          height: 20,
          background: on ? "#facc15" : "rgba(255,255,255,0.10)",
          border: "1px solid rgba(255,255,255,0.10)",
        }}
      >
        <span
          className="absolute"
          style={{
            top: 2,
            left: on ? 18 : 2,
            width: 14,
            height: 14,
            background: on ? "#0a0a0a" : "#fff",
            transition: "left 0.15s",
          }}
        />
      </span>
      <span
        className="font-mono text-[10px] tracking-mono-wide"
        style={{ color: on ? "#facc15" : "#888885" }}
      >
        {on ? "AN" : "AUS"}
      </span>
    </span>
  );
}

interface SegOption {
  value: string;
  label: string;
}

function SegRadio({ value, options }: { value: string; options: SegOption[] }) {
  return (
    <div className="inline-flex border border-nau-line">
      {options.map((o, i) => {
        const active = o.value === value;
        return (
          <span
            key={o.value}
            className="cursor-pointer px-4 py-2.5 font-mono text-[11px] uppercase tracking-mono"
            style={{
              background: active ? "#facc15" : "transparent",
              color: active ? "#0a0a0a" : "#f5f5f4",
              borderLeft: i > 0 ? "1px solid rgba(255,255,255,0.10)" : "none",
            }}
          >
            {o.label}
          </span>
        );
      })}
    </div>
  );
}

function Stepper({ value, unit }: { value: number; unit: string }) {
  return (
    <div className="inline-flex items-stretch border border-nau-line">
      <span className="cursor-pointer border-r border-nau-line px-3.5 py-2.5 font-mono text-sm text-nau-fg-dim">
        −
      </span>
      <span
        className="px-6 py-2.5 text-center font-mono text-sm text-nau-fg"
        style={{ minWidth: 80 }}
      >
        {value} <span className="text-[11px] text-nau-fg-dim">{unit}</span>
      </span>
      <span className="cursor-pointer border-l border-nau-line px-3.5 py-2.5 font-mono text-sm text-nau-fg-dim">
        +
      </span>
    </div>
  );
}

function ColorSwatchRow({ value, options }: { value: string; options: string[] }) {
  return (
    <div className="flex items-center gap-3">
      {options.map((c) => {
        const active = c === value;
        const wrap: CSSProperties = {
          position: "relative",
          width: 36,
          height: 36,
          background: c,
          border: active ? "1px solid #facc15" : "1px solid transparent",
          padding: 4,
          cursor: "pointer",
        };
        const inner: CSSProperties = {
          display: "block",
          width: "100%",
          height: "100%",
          background: c,
          outline: active ? "1px solid #0a0a0a" : "none",
          outlineOffset: -3,
        };
        return (
          <span key={c} style={wrap}>
            <span style={inner} />
          </span>
        );
      })}
      <span className="ml-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
        {value.toUpperCase()}
      </span>
    </div>
  );
}

interface CalRowProps {
  name: string;
  account: string;
  status: "LIVE" | "PAUSED";
  color: string;
}

function CalRow({ name, account, status, color }: CalRowProps) {
  const isLive = status === "LIVE";
  return (
    <div className="flex items-center gap-4 border border-nau-line bg-white/[0.015] px-4 py-4">
      <span style={{ width: 4, height: 28, background: color }} />
      <div className="flex-1">
        <div className="mb-0.5 font-sans text-[15px] text-nau-fg">{name}</div>
        <div className="font-mono text-[11px] tracking-[0.04em] text-nau-fg-dim">{account}</div>
      </div>
      <span
        className="px-2.5 py-1 font-mono text-[10px] tracking-mono-wide"
        style={{
          color: isLive ? "#facc15" : "#888885",
          border: isLive ? "1px solid #facc15" : "1px solid rgba(255,255,255,0.10)",
          background: isLive ? "rgba(250,204,21,0.08)" : "transparent",
        }}
      >
        {isLive && <span className="mr-1">●</span>}
        {status}
      </span>
      <Toggle on={isLive} />
      <span className="cursor-pointer px-2 py-1 font-mono text-[11px] tracking-mono text-nau-fg-dim">
        · · ·
      </span>
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────

export function SettingsPage({ onNavigate }: SettingsPageProps) {
  const navItems = [
    { n: "01", label: "Profil", active: true },
    { n: "02", label: "Kalender" },
    { n: "03", label: "AI · Verhalten" },
    { n: "04", label: "Darstellung" },
    { n: "05", label: "Shortcuts" },
    { n: "06", label: "Datenschutz" },
  ];

  const [llm, setLlm] = useState<LlmSettings | null>(null);
  const [llmError, setLlmError] = useState<string | null>(null);
  const [draftKey, setDraftKey] = useState<string>("");
  const [editingKey, setEditingKey] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savedFlash, setSavedFlash] = useState(false);

  useEffect(() => {
    getLlmSettings()
      .then(setLlm)
      .catch((e) => setLlmError(String(e.message ?? e)));
  }, []);

  const saveLlm = async (
    patch: Partial<LlmSettings> & { geminiApiKey?: string | null },
  ) => {
    if (!llm) return;
    setSaving(true);
    setLlmError(null);
    try {
      await updateLlmSettings({
        provider: patch.provider ?? llm.provider,
        ollamaModel: patch.ollamaModel ?? llm.ollamaModel,
        geminiModel: patch.geminiModel ?? llm.geminiModel,
        geminiApiKey: patch.geminiApiKey ?? null,
      });
      const fresh = await getLlmSettings();
      setLlm(fresh);
      setSavedFlash(true);
      setTimeout(() => setSavedFlash(false), 2500);
    } catch (e) {
      setLlmError(String((e as Error).message ?? e));
    } finally {
      setSaving(false);
      setEditingKey(false);
      setDraftKey("");
    }
  };

  const shortcuts = [
    { cmd: "/termin", desc: "Neuen Termin anlegen", keys: "⌘ T" },
    { cmd: "/verschieben", desc: "Letzten Termin verschieben", keys: "⌘ ⇧ V" },
    { cmd: "/woche", desc: "Wochenübersicht öffnen", keys: "⌘ W" },
    { cmd: "/frei", desc: "Freie Slots finden", keys: "⌘ F" },
    { cmd: "/konflikte", desc: "Konflikte prüfen", keys: "⌘ K" },
  ];

  return (
    <div
      className="grid min-h-screen bg-nau-bg text-nau-fg"
      style={{ gridTemplateColumns: "260px 1fr" }}
    >
      {/* Left nav */}
      <aside className="relative border-r border-nau-line px-6 py-7">
        <button
          type="button"
          onClick={() => onNavigate("chat")}
          className="mb-10 flex cursor-pointer items-center gap-3 bg-transparent"
          aria-label="Zurück zum Chat"
        >
          <span className="inline-flex h-7 w-7 items-center justify-center bg-nau-accent font-mono text-[13px] font-bold text-nau-bg">
            N
          </span>
          <span className="font-sans text-[15px] font-semibold text-nau-fg">NauAssist</span>
        </button>

        <div className="mb-4 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
          // EINSTELLUNGEN
        </div>

        <nav className="flex flex-col gap-0.5">
          {navItems.map((it) => (
            <span
              key={it.n}
              className="flex cursor-pointer items-center gap-3 px-3 py-2.5"
              style={{
                background: it.active ? "rgba(250,204,21,0.06)" : "transparent",
                borderLeft: it.active
                  ? "2px solid #facc15"
                  : "2px solid transparent",
              }}
            >
              <span
                className="font-mono text-[11px] font-bold tracking-mono"
                style={{ color: it.active ? "#facc15" : "#888885" }}
              >
                {it.n}
              </span>
              <span
                className="font-sans text-sm"
                style={{ color: it.active ? "#f5f5f4" : "#888885" }}
              >
                {it.label}
              </span>
            </span>
          ))}
        </nav>

        <div className="absolute bottom-6 left-6 right-6 border-t border-nau-line pt-4">
          <div className="mb-1.5 font-mono text-[9px] tracking-mono-xwide text-nau-fg-dim">
            // STATUS
          </div>
          <div className="font-mono text-[10px] leading-7 tracking-[0.04em] text-nau-fg-dim">
            <div>v0.4 · build_2147</div>
            <div>3 KAL · 24 EVT · LIVE</div>
          </div>
        </div>
      </aside>

      {/* Main content */}
      <main className="max-w-[980px] px-16 pb-20 pt-10">
        {/* page header */}
        <div className="mb-9">
          <div className="mb-3 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            — EINSTELLUNGEN —
          </div>
          <h1 className="m-0 font-sans text-5xl font-normal leading-[1.05] tracking-tight text-nau-fg">
            So arbeitet{" "}
            <span className="font-mono font-bold text-nau-accent">Nau</span> für dich.
          </h1>
          <p className="m-0 mt-3.5 max-w-[540px] font-sans text-[15px] leading-relaxed text-nau-fg-dim">
            Verbinde deine Kalender, sag Nau wie er dich ansprechen soll und passe das Erlebnis an
            deinen Rhythmus an.
          </p>
        </div>

        {/* ── 01 · Profil ─── */}
        <SectionHead n={1} label="PROFIL" title="Dein Profil." />
        <div>
          <Row label="Avatar" hint="Wird im Chat und in deinen Einladungen angezeigt.">
            <div className="flex items-center gap-4">
              <span
                className="inline-flex items-center justify-center border border-dashed border-nau-line bg-white/[0.05] font-mono text-[22px] text-nau-fg-dim"
                style={{ width: 64, height: 64 }}
              >
                BN
              </span>
              <span className="cursor-pointer border border-nau-line px-3.5 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg">
                BILD HOCHLADEN →
              </span>
            </div>
          </Row>
          <Row label="Vollständiger Name" hint="So erscheinst du in deinem Account.">
            <TxtField value="Benedikt Nau" placeholder="Dein Name" />
          </Row>
          <Row label="Wie soll Nau dich nennen?" hint="Anrede in Antworten und Begrüßungen.">
            <div className="flex flex-col gap-2.5">
              <TxtField value="Benedikt" placeholder="z.B. Benedikt, Boss, Hey du" />
              <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
                // VORSCHAU · „Hey Benedikt, du hast morgen zwei freie Stunden am Vormittag."
              </div>
            </div>
          </Row>
          <Row label="E-Mail" hint="Für Einladungen, Synchronisation, Recovery.">
            <TxtField value="benedikt.nau@tegut.com" suffix="VERIFIZIERT" />
          </Row>
          <Row label="Zeitzone" hint="Wird automatisch erkannt — du kannst überschreiben.">
            <TxtField value="Europe / Berlin · UTC+2" suffix="AUTO" />
          </Row>
        </div>

        {/* ── 02 · Kalender ─── */}
        <div className="mt-14">
          <SectionHead
            n={2}
            label="KALENDER"
            title="Verbundene Kalender."
            kicker="Nau liest und schreibt in alle aktiven Kalender. Du entscheidest, welche."
          />
          <div className="flex flex-col gap-px">
            <CalRow
              name="Work · Acme Inc"
              account="benedikt@acme.com · Google Calendar"
              status="LIVE"
              color="#facc15"
            />
            <CalRow
              name="Personal"
              account="benedikt.nau@nau.studio · Google Calendar"
              status="LIVE"
              color="#f472b6"
            />
            <CalRow
              name="Family"
              account="nau.family@icloud · iCloud"
              status="LIVE"
              color="#60a5fa"
            />
            <CalRow
              name="Birthdays"
              account="benedikt.nau@nau.studio · Apple"
              status="PAUSED"
              color="rgba(255,255,255,0.4)"
            />
          </div>
          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              className="cursor-pointer border border-nau-line bg-transparent px-4 py-3 font-mono text-[11px] tracking-mono-wide text-nau-fg"
            >
              + KALENDER VERBINDEN
            </button>
            <div className="flex items-center gap-2.5 font-mono text-[11px] tracking-[0.08em] text-nau-fg-dim">
              <span>// UNTERSTÜTZT:</span>
              <span className="text-nau-fg">GOOGLE</span>
              <span className="opacity-40">·</span>
              <span className="text-nau-fg">APPLE</span>
              <span className="opacity-40">·</span>
              <span className="text-nau-fg">OUTLOOK</span>
              <span className="opacity-40">·</span>
              <span className="text-nau-fg">CALDAV</span>
            </div>
          </div>
        </div>

        {/* ── 03 · AI Verhalten ─── */}
        <div className="mt-14">
          <SectionHead
            n={3}
            label="AI · VERHALTEN"
            title="Wie Nau plant."
            kicker="Stelle ein, wie Nau Termine setzt, wie er antwortet und wann er dich in Ruhe lässt."
          />

          {llmError && !llm && (
            <div className="border border-nau-line bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
              // SETTINGS NICHT LADBAR — BACKEND OFFLINE?
            </div>
          )}

          {llm && (
            <>
              <Row label="AI-Provider" hint="Welche AI Nau für seine Antworten nutzt.">
                <div className="inline-flex border border-nau-line">
                  {(["ollama", "gemini"] as const).map((p, i) => {
                    const active = llm.provider === p;
                    return (
                      <button
                        key={p}
                        type="button"
                        onClick={() => saveLlm({ provider: p })}
                        disabled={saving}
                        className="cursor-pointer bg-transparent px-4 py-2.5 font-mono text-[11px] uppercase tracking-mono"
                        style={{
                          background: active ? "#facc15" : "transparent",
                          color: active ? "#0a0a0a" : "#f5f5f4",
                          borderLeft: i > 0 ? "1px solid rgba(255,255,255,0.10)" : "none",
                        }}
                      >
                        {p === "ollama" ? "Ollama (lokal)" : "Gemini (Cloud)"}
                      </button>
                    );
                  })}
                </div>
              </Row>

              <Row label="Modell" hint="Welches Modell verwendet wird.">
                <select
                  value={llm.provider === "ollama" ? llm.ollamaModel : llm.geminiModel}
                  disabled={saving}
                  onChange={(e) =>
                    saveLlm(
                      llm.provider === "ollama"
                        ? { ollamaModel: e.target.value }
                        : { geminiModel: e.target.value },
                    )
                  }
                  className="max-w-[480px] border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
                >
                  {(llm.provider === "ollama" ? OLLAMA_MODELS : GEMINI_MODELS).map((m) => (
                    <option key={m} value={m} className="bg-nau-bg text-nau-fg">
                      {m}
                    </option>
                  ))}
                </select>
              </Row>

              {llm.provider === "gemini" && (
                <Row
                  label="Gemini API-Key"
                  hint="Wird sicher lokal gespeichert. Hol dir einen Key bei aistudio.google.com."
                >
                  {llm.hasGeminiApiKey && !editingKey ? (
                    <div className="flex items-center gap-3">
                      <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
                        •••••••••• GESPEICHERT
                      </span>
                      <button
                        type="button"
                        onClick={() => setEditingKey(true)}
                        className="cursor-pointer border border-nau-line bg-transparent px-3.5 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg"
                      >
                        ÄNDERN
                      </button>
                      <button
                        type="button"
                        onClick={() => saveLlm({ geminiApiKey: "" })}
                        disabled={saving}
                        className="cursor-pointer border border-nau-line bg-transparent px-3.5 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim"
                      >
                        ENTFERNEN
                      </button>
                    </div>
                  ) : (
                    <div className="flex items-center gap-3">
                      <input
                        type="password"
                        value={draftKey}
                        onChange={(e) => setDraftKey(e.target.value)}
                        placeholder="AIza..."
                        className="max-w-[360px] flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
                      />
                      <button
                        type="button"
                        onClick={() => saveLlm({ geminiApiKey: draftKey })}
                        disabled={saving || draftKey.length === 0}
                        className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg"
                      >
                        ÜBERNEHMEN ↵
                      </button>
                      {llm.hasGeminiApiKey && (
                        <button
                          type="button"
                          onClick={() => {
                            setEditingKey(false);
                            setDraftKey("");
                          }}
                          className="cursor-pointer bg-transparent px-2 py-2 font-mono text-[10px] tracking-mono text-nau-fg-dim"
                        >
                          ABBRECHEN
                        </button>
                      )}
                    </div>
                  )}
                </Row>
              )}

              {savedFlash && (
                <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-accent">
                  // PROVIDER AKTUALISIERT — WIRD AB DEINER NÄCHSTEN NACHRICHT GENUTZT
                </div>
              )}
              {llmError && llm && (
                <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-danger">
                  // FEHLER: {llmError}
                </div>
              )}

              <div className="border-b border-nau-line py-4 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
                // ↓ ABSCHNITT UNTEN IST MOCKUP — NOCH NICHT VERDRAHTET
              </div>
            </>
          )}

          <Row label="Tonalität" hint="So klingt Nau in seinen Antworten.">
            <SegRadio
              value="warm"
              options={[
                { value: "sachlich", label: "Sachlich" },
                { value: "warm", label: "Warm" },
                { value: "locker", label: "Locker" },
              ]}
            />
          </Row>
          <Row label="Standard-Dauer" hint="Wenn du keine Dauer angibst, plant Nau damit.">
            <Stepper value={45} unit="MIN" />
          </Row>
          <Row
            label="Pufferzeit zwischen Terminen"
            hint="Damit du nicht von Meeting zu Meeting springst."
          >
            <Stepper value={15} unit="MIN" />
          </Row>
          <Row label="Arbeitsstunden" hint="Außerhalb dieser Zeiten schlägt Nau nichts vor.">
            <div className="flex items-center gap-3">
              <TxtField value="09:00" />
              <span className="font-mono text-xs text-nau-fg-dim">→</span>
              <TxtField value="18:00" />
              <span className="ml-2 font-mono text-[10px] tracking-mono text-nau-fg-dim">
                MO – FR
              </span>
            </div>
          </Row>
          <Row label="Wochenende einbeziehen" hint="Schlägt Sa/So auch als freie Slots vor.">
            <Toggle on={false} />
          </Row>
          <Row
            label="Konflikte automatisch lösen"
            hint="Nau verschiebt kleine Konflikte selbständig — und sagt dir Bescheid."
          >
            <Toggle on={true} />
          </Row>
          <Row label="Vorschlags-Modus" hint="Wie viele Optionen Nau standardmäßig anbietet.">
            <SegRadio
              value="3"
              options={[
                { value: "1", label: "Beste 1" },
                { value: "3", label: "3 Slots" },
                { value: "5", label: "5 Slots" },
              ]}
            />
          </Row>
        </div>

        {/* ── 04 · Darstellung ─── */}
        <div className="mt-14">
          <SectionHead n={4} label="DARSTELLUNG" title="Look & Feel." />
          <Row
            label="Akzentfarbe"
            hint="Wird auf Buttons, Highlights und im Kalender verwendet."
          >
            <ColorSwatchRow
              value="#facc15"
              options={["#facc15", "#f59e0b", "#a3e635", "#22d3ee", "#f472b6"]}
            />
          </Row>
          <Row label="Dichte" hint="Wie viel Luft zwischen den Elementen.">
            <SegRadio
              value="comfortable"
              options={[
                { value: "compact", label: "Kompakt" },
                { value: "comfortable", label: "Komfortabel" },
              ]}
            />
          </Row>
          <Row label="Kalender-Standardansicht" hint="Was du beim Öffnen siehst.">
            <SegRadio
              value="woche"
              options={[
                { value: "tag", label: "Tag" },
                { value: "woche", label: "Woche" },
                { value: "monat", label: "Monat" },
              ]}
            />
          </Row>
          <Row label="Wochenstart" hint="">
            <SegRadio
              value="mo"
              options={[
                { value: "mo", label: "Montag" },
                { value: "so", label: "Sonntag" },
              ]}
            />
          </Row>
        </div>

        {/* ── 05 · Shortcuts ─── */}
        <div className="mt-14">
          <SectionHead
            n={5}
            label="SHORTCUTS"
            title="Slash-Commands."
            kicker="Tippe / im Chat um die Palette zu öffnen."
          />
          <div className="border border-nau-line bg-white/[0.015]">
            {shortcuts.map((s, i, arr) => (
              <div
                key={s.cmd}
                className="grid items-center gap-4 px-4 py-3"
                style={{
                  gridTemplateColumns: "180px 1fr 100px 80px",
                  borderBottom:
                    i === arr.length - 1 ? "none" : "1px solid rgba(255,255,255,0.10)",
                }}
              >
                <span className="font-mono text-[13px] text-nau-accent">{s.cmd}</span>
                <span className="font-sans text-sm text-nau-fg">{s.desc}</span>
                <span className="text-right font-mono text-[11px] tracking-mono text-nau-fg-dim">
                  {s.keys}
                </span>
                <span className="cursor-pointer text-right font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
                  EDIT →
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* ── 06 · Datenschutz ─── */}
        <div className="mt-14">
          <SectionHead n={6} label="DATENSCHUTZ" title="Deine Daten." />
          <Row
            label="Lern-Modus"
            hint="Nau merkt sich deine Präferenzen, um bessere Vorschläge zu machen."
          >
            <Toggle on={true} />
          </Row>
          <Row
            label="Aktivitäts-Log behalten"
            hint="Wie lange Nau deine Konversationen speichert."
          >
            <SegRadio
              value="30"
              options={[
                { value: "7", label: "7 Tage" },
                { value: "30", label: "30 Tage" },
                { value: "forever", label: "Für immer" },
              ]}
            />
          </Row>
          <Row
            label="Daten exportieren"
            hint="Lade alle deine Kalender-Daten und Konversationen herunter."
          >
            <button
              type="button"
              className="cursor-pointer border border-nau-line bg-transparent px-4 py-3 font-mono text-[11px] tracking-mono-wide text-nau-fg"
            >
              EXPORT .JSON →
            </button>
          </Row>
          <Row label="Account löschen" hint="Permanenter Verlust aller Daten. Nicht umkehrbar.">
            <button
              type="button"
              className="cursor-pointer border border-nau-danger bg-transparent px-4 py-3 font-mono text-[11px] tracking-mono-wide text-nau-danger"
            >
              ACCOUNT LÖSCHEN →
            </button>
          </Row>
        </div>

        {/* sticky-feeling footer */}
        <div className="mt-14 flex items-center justify-end border-t border-nau-line pt-6">
          <div className="flex gap-3">
            <button
              type="button"
              onClick={() => onNavigate("chat")}
              className="cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg"
            >
              ZURÜCK ZUM CHAT
            </button>
            <button
              type="button"
              onClick={() => onNavigate("chat")}
              className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg"
            >
              FERTIG ↵
            </button>
          </div>
        </div>
      </main>
    </div>
  );
}
