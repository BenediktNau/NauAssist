import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { AppPage } from "@/App";
import {
  getLlmSettings,
  updateLlmSettings,
  getOllamaSettings,
  updateOllamaSettings,
  testOllamaConnection,
  OLLAMA_MODELS,
  GEMINI_MODELS,
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
    <div
      className="grid items-start gap-8 border-b border-nau-line py-4"
      style={{ gridTemplateColumns: "260px 1fr" }}
    >
      <div>
        <div
          className="font-sans text-sm font-medium text-nau-fg"
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
        <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
          {label}
        </span>
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
  value, onChange, placeholder, type = "text", disabled = false,
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
      className="max-w-[480px] w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg disabled:opacity-50"
    />
  );
}

function PrimaryButton({ children, onClick, disabled }: {
  children: ReactNode; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40"
    >
      {children}
    </button>
  );
}

function SecondaryButton({ children, onClick, disabled }: {
  children: ReactNode; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg disabled:opacity-40"
    >
      {children}
    </button>
  );
}

export function SettingsPage({ onNavigate }: SettingsPageProps) {
  const [llm, setLlm] = useState<LlmSettings | null>(null);
  const [ollama, setOllama] = useState<OllamaSettings | null>(null);
  const [calendar, setCalendar] = useState<CalendarSettings | null>(null);
  const [topError, setTopError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([getLlmSettings(), getOllamaSettings(), getCalendarSettings()])
      .then(([l, o, c]) => {
        setLlm(l); setOllama(o); setCalendar(c);
      })
      .catch((e) => setTopError(String(e.message ?? e)));
  }, []);

  const navItems = [
    { n: "01", label: "KI-Provider", anchor: "section-llm" },
    { n: "02", label: "Kalender", anchor: "section-calendar" },
  ];

  return (
    <div
      className="grid min-h-screen bg-nau-bg text-nau-fg"
      style={{ gridTemplateColumns: "260px 1fr" }}
    >
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
            <a
              key={it.n}
              href={`#${it.anchor}`}
              className="flex cursor-pointer items-center gap-3 px-3 py-2.5 no-underline"
            >
              <span className="font-mono text-[11px] font-bold tracking-mono text-nau-fg-dim">
                {it.n}
              </span>
              <span className="font-sans text-sm text-nau-fg-dim">{it.label}</span>
            </a>
          ))}
        </nav>
      </aside>

      <main className="max-w-[980px] px-16 pb-20 pt-10">
        <div className="mb-9">
          <div className="mb-3 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            — EINSTELLUNGEN —
          </div>
          <h1 className="m-0 font-sans text-4xl font-normal leading-[1.05] tracking-tight text-nau-fg">
            Provider &amp; Kalender.
          </h1>
        </div>

        {topError && (
          <div className="mb-6 border border-nau-danger bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
            // SETTINGS NICHT LADBAR — {topError}
          </div>
        )}

        {llm && ollama && (
          <LlmSection
            llm={llm} setLlm={setLlm}
            ollama={ollama} setOllama={setOllama}
          />
        )}
        {calendar && (
          <CalendarSection calendar={calendar} setCalendar={setCalendar} />
        )}

        <div className="mt-14 flex items-center justify-end border-t border-nau-line pt-6">
          <SecondaryButton onClick={() => onNavigate("chat")}>
            ZURÜCK ZUM CHAT
          </SecondaryButton>
        </div>
      </main>
    </div>
  );
}

function LlmSection({
  llm, setLlm, ollama, setOllama,
}: {
  llm: LlmSettings;
  setLlm: (l: LlmSettings) => void;
  ollama: OllamaSettings;
  setOllama: (o: OllamaSettings) => void;
}) {
  const [draftKey, setDraftKey] = useState("");
  const [editingKey, setEditingKey] = useState(false);
  const [llmError, setLlmError] = useState<string | null>(null);
  const [savedFlash, setSavedFlash] = useState(false);

  const [showAdvanced, setShowAdvanced] = useState(false);
  const [hostDraft, setHostDraft] = useState(ollama.host);
  const [apiKeyDraft, setApiKeyDraft] = useState("");
  const [numCtxDraft, setNumCtxDraft] = useState(String(ollama.numCtx));
  const [tempDraft, setTempDraft] = useState(String(ollama.temperature));
  const [editingOllamaKey, setEditingOllamaKey] = useState(false);
  const [ollamaSaving, setOllamaSaving] = useState(false);
  const [ollamaError, setOllamaError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);

  const ollamaDirty =
    hostDraft !== ollama.host ||
    numCtxDraft !== String(ollama.numCtx) ||
    tempDraft !== String(ollama.temperature) ||
    editingOllamaKey;

  const saveLlm = async (
    patch: Partial<LlmSettings> & { geminiApiKey?: string | null },
  ) => {
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
      setEditingKey(false);
      setDraftKey("");
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
    const r = await testOllamaConnection(
      hostDraft,
      editingOllamaKey ? apiKeyDraft : null,
    );
    if (r.ok) {
      setTestResult(`// ERREICHBAR · ${r.models?.length ?? 0} MODELLE`);
    } else {
      setTestResult(`// FEHLER: ${r.error ?? "unbekannt"}`);
    }
  };

  return (
    <div id="section-llm">
      <SectionHead n={1} label="KI-PROVIDER" title="Wie Nau denkt." />

      <Row label="AI-Provider" hint="Welche AI Nau für seine Antworten nutzt.">
        <div className="inline-flex border border-nau-line">
          {(["ollama", "gemini"] as const).map((p, i) => {
            const active = llm.provider === p;
            return (
              <button
                key={p}
                type="button"
                onClick={() => saveLlm({ provider: p })}
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

      <Row
        label="Modell"
        hint={
          llm.provider === "ollama"
            ? "Vorschläge — oder eigenes lokal gepulltes Modell eintippen."
            : "Welches Modell verwendet wird."
        }
      >
        {llm.provider === "ollama" ? (
          <>
            <input
              list="ollama-models"
              value={llm.ollamaModel}
              onChange={(e) => saveLlm({ ollamaModel: e.target.value })}
              className="max-w-[480px] border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
              placeholder="z.B. gemma4:26b"
            />
            <datalist id="ollama-models">
              {OLLAMA_MODELS.map((m) => (
                <option key={m} value={m} />
              ))}
            </datalist>
          </>
        ) : (
          <select
            value={llm.geminiModel}
            onChange={(e) => saveLlm({ geminiModel: e.target.value })}
            className="max-w-[480px] border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
          >
            {GEMINI_MODELS.map((m) => (
              <option key={m} value={m} className="bg-nau-bg text-nau-fg">
                {m}
              </option>
            ))}
          </select>
        )}
      </Row>

      <Row
        label="Gemini API-Key"
        hint="Wird sicher lokal gespeichert. Hol dir einen Key bei aistudio.google.com."
      >
        {llm.hasGeminiApiKey && !editingKey ? (
          <div className="flex items-center gap-3">
            <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
              •••••••••• GESPEICHERT
            </span>
            <SecondaryButton onClick={() => setEditingKey(true)}>ÄNDERN</SecondaryButton>
            <SecondaryButton
              onClick={() => saveLlm({ geminiApiKey: "" })}
              disabled={llm.provider === "gemini"}
            >
              ENTFERNEN
            </SecondaryButton>
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
            <PrimaryButton
              onClick={() => saveLlm({ geminiApiKey: draftKey })}
              disabled={draftKey.length === 0}
            >
              ÜBERNEHMEN ↵
            </PrimaryButton>
            {llm.hasGeminiApiKey && (
              <SecondaryButton onClick={() => {
                setEditingKey(false); setDraftKey("");
              }}>
                ABBRECHEN
              </SecondaryButton>
            )}
          </div>
        )}
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
          <Row label="Ollama-Host" hint="Z.B. http://localhost:11434 oder hinter einem Reverse-Proxy.">
            <div className="flex flex-col gap-2 max-w-[480px]">
              <div className="flex items-center gap-3">
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
              <div className="flex items-center gap-3">
                <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
                  •••••• GESPEICHERT
                </span>
                <SecondaryButton onClick={() => setEditingOllamaKey(true)}>ÄNDERN</SecondaryButton>
              </div>
            ) : (
              <div className="flex items-center gap-3">
                <input
                  type="password"
                  value={apiKeyDraft}
                  onChange={(e) => {
                    setApiKeyDraft(e.target.value);
                    setEditingOllamaKey(true);
                  }}
                  placeholder="optional"
                  className="max-w-[360px] flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
                />
                {ollama.hasApiKey && (
                  <SecondaryButton onClick={() => {
                    setEditingOllamaKey(false); setApiKeyDraft("");
                  }}>
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

          <div className="flex items-center gap-3 border-b border-nau-line py-4">
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
          // PROVIDER AKTUALISIERT — WIRD AB DEINER NÄCHSTEN NACHRICHT GENUTZT
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
  calendar, setCalendar,
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
    <div id="section-calendar" className="mt-14">
      <SectionHead
        n={2}
        label="KALENDER"
        title="Google-Kalender."
        kicker="OAuth-Credentials + Verhalten von Nau gegenüber deinem Kalender."
      />

      <Row label="Verbindungsstatus" hint="">
        <div className="flex items-center gap-3">
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
            <PrimaryButton
              onClick={startAuth}
              disabled={!calendar.hasGoogleCredentials}
            >
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
            <li>Nach „Erlauben" landet der Browser auf einer nicht-erreichbaren Seite (Absicht).</li>
            <li>Kopiere aus der Adresszeile den Wert hinter <code>code=</code> bis zum nächsten <code>&amp;</code>.</li>
          </ol>
          <div className="mb-3 max-w-[600px] break-all border border-nau-line bg-nau-bg px-3 py-2 font-mono text-[11px] text-nau-fg">
            {authState.url}
          </div>
          <div className="flex items-center gap-3">
            <input
              type="text"
              value={authCode}
              onChange={(e) => setAuthCode(e.target.value)}
              placeholder="code=..."
              className="max-w-[360px] flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
            />
            <PrimaryButton onClick={completeAuth} disabled={authCode.trim().length === 0}>
              CODE ÜBERMITTELN ↵
            </PrimaryButton>
            <SecondaryButton onClick={() => { setAuthState(null); setAuthCode(""); }}>
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
          <div className="flex items-center gap-3">
            <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
              GESPEICHERT
            </span>
            <SecondaryButton onClick={() => setEditingCreds(true)}>ÄNDERN</SecondaryButton>
          </div>
        ) : (
          <TextInput
            value={clientIdDraft}
            onChange={(v) => { setClientIdDraft(v); setEditingCreds(true); }}
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
            onChange={(v) => { setClientSecretDraft(v); setEditingCreds(true); }}
            placeholder="GOCSPX-..."
          />
        )}
      </Row>

      <Row label="Calendar-ID" hint="„primary&quot; oder eine konkrete Kalender-Adresse.">
        <TextInput value={calendarId} onChange={setCalendarId} />
      </Row>

      <Row label="Arbeitszeiten" hint="Außerhalb dieser Zeiten schlägt Nau nichts vor.">
        <div className="flex items-center gap-3">
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

      <div className="flex items-center gap-3 py-4">
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
