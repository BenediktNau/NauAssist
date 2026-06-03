import { useEffect, useRef, useState } from "react";
import {
  createWhatsAppAccount,
  deleteSourceAccount,
  deleteWhatsAppSession,
  getWhatsAppSession,
  listSourceAccounts,
  listWhatsAppChats,
  listWhatsAppChatsForAccount,
  startWhatsAppSession,
  updateSourceAccount,
  type SourceAccountDto,
  type WhatsAppChatDto,
} from "@/api/source-accounts";

interface WhatsAppSectionProps {
  anchor: string;
}

export function WhatsAppSection({ anchor }: WhatsAppSectionProps) {
  const [accounts, setAccounts] = useState<SourceAccountDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  const reload = async () => {
    setLoading(true);
    setError(null);
    try {
      setAccounts(await listSourceAccounts("whatsapp"));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void reload();
  }, []);

  return (
    <div id={anchor} className="mt-14">
      <SectionHead n={6} label="WHATSAPP" title="WhatsApp-Nummer." />
      <p className="mb-4 max-w-[640px] font-sans text-sm leading-relaxed text-nau-fg-dim">
        Verbinde eine WhatsApp-Nummer per QR-Scan. Der Agent liest die freigegebenen
        Chats und schlägt passende Termin-Slots vor. Inoffizielle Anbindung &mdash; nutze
        besser eine <strong>Zweitnummer</strong>, nicht deine private Hauptnummer.
      </p>

      {error && (
        <div className="mb-4 border border-nau-danger bg-white/[0.02] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
          // {error}
        </div>
      )}

      {loading && accounts.length === 0 ? (
        <div className="border border-dashed border-nau-line px-4 py-6 text-center font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
          // LADE ACCOUNTS …
        </div>
      ) : (
        <ul className="flex flex-col gap-3">
          {accounts.map((a) => (
            <WaAccountCard key={a.id} account={a} onChanged={reload} />
          ))}
        </ul>
      )}

      <div className="mt-5">
        {adding ? (
          <AddWhatsAppForm
            onCreated={async () => {
              setAdding(false);
              await reload();
            }}
            onCancel={() => setAdding(false)}
          />
        ) : (
          <button
            type="button"
            onClick={() => setAdding(true)}
            className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent"
          >
            + NUMMER VERBINDEN
          </button>
        )}
      </div>
    </div>
  );
}

function SectionHead({ n, label, title }: { n: number; label: string; title: string }) {
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
    </div>
  );
}

interface WaAccountCardProps {
  account: SourceAccountDto;
  onChanged: () => Promise<void>;
}

function WaAccountCard({ account, onChanged }: WaAccountCardProps) {
  const [chats, setChats] = useState<WhatsAppChatDto[] | null>(null);
  const [draftAllowlist, setDraftAllowlist] = useState<string[]>(account.allowlist);
  const [loadingChats, setLoadingChats] = useState(false);
  const [savingAllowlist, setSavingAllowlist] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => setDraftAllowlist(account.allowlist), [account.allowlist]);

  const loadChats = async () => {
    setLoadingChats(true);
    setError(null);
    try {
      setChats(await listWhatsAppChatsForAccount(account.id));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoadingChats(false);
    }
  };

  const toggle = (chatId: string) =>
    setDraftAllowlist((prev) =>
      prev.includes(chatId) ? prev.filter((c) => c !== chatId) : [...prev, chatId],
    );

  const saveAllowlist = async () => {
    setSavingAllowlist(true);
    setError(null);
    try {
      await updateSourceAccount(account.id, { allowlist: draftAllowlist });
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSavingAllowlist(false);
    }
  };

  const toggleEnabled = async () => {
    setError(null);
    try {
      await updateSourceAccount(account.id, { enabled: !account.enabled });
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const remove = async () => {
    if (!confirm(`Account "${account.displayName}" wirklich löschen?`)) return;
    setError(null);
    try {
      const sessionId = account.credentials.sessionId;
      await deleteSourceAccount(account.id);
      if (sessionId) {
        await deleteWhatsAppSession(sessionId).catch(() => {});
      }
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const dirty =
    draftAllowlist.length !== account.allowlist.length ||
    draftAllowlist.some((c) => !account.allowlist.includes(c));

  return (
    <li className="border border-nau-line bg-white/[0.02] p-4">
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <span className="font-sans text-base font-medium text-nau-fg">{account.displayName}</span>
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {account.credentials.phoneLabel ?? "—"}
        </span>
        <span
          className={
            "ml-auto font-mono text-[10px] tracking-mono-wide " +
            (account.enabled ? "text-nau-accent" : "text-nau-fg-dim")
          }
        >
          {account.enabled ? "AKTIV" : "DEAKTIVIERT"}
        </span>
      </div>

      <div className="mt-2 font-mono text-[11px] tracking-mono text-nau-fg-dim">
        // {account.allowlist.length} Chat(s) freigegeben
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent"
        >
          {expanded ? "SCHLIESSEN" : "CHATS VERWALTEN"}
        </button>
        <button
          type="button"
          onClick={toggleEnabled}
          className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-accent hover:text-nau-accent"
        >
          {account.enabled ? "DEAKTIVIEREN" : "AKTIVIEREN"}
        </button>
        <button
          type="button"
          onClick={remove}
          className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-danger hover:text-nau-danger"
        >
          LÖSCHEN
        </button>
      </div>

      {error && (
        <div className="mt-3 font-mono text-[11px] tracking-mono text-nau-danger">// {error}</div>
      )}

      {expanded && (
        <div className="mt-4 border-t border-nau-line pt-3">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={loadChats}
              disabled={loadingChats}
              className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-50"
            >
              {loadingChats ? "LADE …" : chats === null ? "CHATS LADEN" : "AKTUALISIEREN"}
            </button>
            {dirty && (
              <button
                type="button"
                onClick={saveAllowlist}
                disabled={savingAllowlist}
                className="min-h-10 cursor-pointer border-none bg-nau-accent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-50"
              >
                {savingAllowlist ? "SPEICHERE …" : "ALLOWLIST SPEICHERN"}
              </button>
            )}
          </div>

          {chats && chats.length > 0 && (
            <ul className="mt-3 flex flex-col gap-1.5">
              {chats.map((c) => {
                const active = draftAllowlist.includes(c.chatId);
                return (
                  <li key={c.chatId}>
                    <label className="flex cursor-pointer items-start gap-3 py-1">
                      <input
                        type="checkbox"
                        checked={active}
                        onChange={() => toggle(c.chatId)}
                        className="mt-1 cursor-pointer"
                      />
                      <span className="flex flex-col">
                        <span className="font-sans text-sm text-nau-fg">{c.name || c.chatId}</span>
                        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
                          {c.chatId}
                        </span>
                      </span>
                    </label>
                  </li>
                );
              })}
            </ul>
          )}

          {chats && chats.length === 0 && (
            <div className="mt-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
              // Keine Chats gefunden. Schreib der Nummer einmal, dann neu laden.
            </div>
          )}
        </div>
      )}
    </li>
  );
}

interface AddWhatsAppFormProps {
  onCreated: () => Promise<void>;
  onCancel: () => void;
}

function AddWhatsAppForm({ onCreated, onCancel }: AddWhatsAppFormProps) {
  const [step, setStep] = useState<"idle" | "pairing" | "connected">("idle");
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [phone, setPhone] = useState<string | null>(null);
  const [chats, setChats] = useState<WhatsAppChatDto[] | null>(null);
  const [allowlist, setAllowlist] = useState<string[]>([]);
  const [displayName, setDisplayName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const pollRef = useRef<number | null>(null);
  const savedRef = useRef(false);
  const sessionIdRef = useRef<string | null>(null);

  const stopPoll = () => {
    if (pollRef.current !== null) {
      window.clearInterval(pollRef.current);
      pollRef.current = null;
    }
  };

  // sessionId in einer Ref spiegeln, damit der Unmount-Cleanup unten die aktuelle ID
  // kennt, ohne selbst von [sessionId] abzuhängen.
  useEffect(() => {
    sessionIdRef.current = sessionId;
  }, [sessionId]);

  // NUR beim Verlassen der Komponente: laufenden Poll stoppen und eine angefangene,
  // ungespeicherte Session im Sidecar verwerfen. Wichtig: Dependency-Array []! Hinge
  // der Effekt an [sessionId], liefe sein Cleanup bei jedem setSessionId() — und würde
  // den in connect() gerade gestarteten QR-Poll sofort wieder abräumen (→ "WARTE AUF QR").
  useEffect(() => {
    return () => {
      stopPoll();
      if (sessionIdRef.current && !savedRef.current) {
        void deleteWhatsAppSession(sessionIdRef.current).catch(() => {});
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const connect = async () => {
    setBusy(true);
    setError(null);
    try {
      const s = await startWhatsAppSession();
      setSessionId(s.sessionId);
      setStep("pairing");
      stopPoll();
      pollRef.current = window.setInterval(async () => {
        try {
          const status = await getWhatsAppSession(s.sessionId);
          setQr(status.qr);
          if (status.state === "connected") {
            stopPoll();
            setPhone(status.phone);
            setStep("connected");
            setChats(await listWhatsAppChats(s.sessionId));
          } else if (status.state === "loggedOut") {
            stopPoll();
            setError("Session abgemeldet — bitte erneut verbinden.");
          }
        } catch {
          // transient — nächster Tick versucht es erneut
        }
      }, 2000);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const toggle = (chatId: string) =>
    setAllowlist((prev) =>
      prev.includes(chatId) ? prev.filter((c) => c !== chatId) : [...prev, chatId],
    );

  const save = async () => {
    if (!sessionId) return;
    setBusy(true);
    setError(null);
    try {
      await createWhatsAppAccount(
        displayName.trim() || phone || "WhatsApp",
        { sessionId, phoneLabel: phone ?? "" },
        allowlist,
      );
      savedRef.current = true;
      await onCreated();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="border border-nau-line bg-white/[0.02] p-4">
      <div className="mb-3 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
        // NEUE WHATSAPP-NUMMER
      </div>

      {step === "idle" && (
        <button
          type="button"
          onClick={connect}
          disabled={busy}
          className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-40"
        >
          {busy ? "STARTE …" : "VERBINDEN & QR ANZEIGEN"}
        </button>
      )}

      {step === "pairing" && (
        <div className="flex flex-col items-start gap-3">
          <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
            // Öffne WhatsApp → Einstellungen → Verknüpfte Geräte → Gerät verknüpfen
          </div>
          {qr ? (
            <img
              src={qr}
              alt="WhatsApp QR-Code"
              className="h-56 w-56 border border-nau-line bg-white p-2"
            />
          ) : (
            <div className="flex h-56 w-56 items-center justify-center border border-dashed border-nau-line font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
              // WARTE AUF QR …
            </div>
          )}
        </div>
      )}

      {step === "connected" && (
        <div className="flex flex-col gap-3">
          <div className="font-mono text-[11px] tracking-mono text-nau-accent">
            ● VERBUNDEN {phone ? `· ${phone}` : ""}
          </div>
          <FormRow label="Anzeigename">
            <input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="z.B. Agent-Nummer"
              className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg lg:max-w-[360px]"
            />
          </FormRow>

          <div className="border-t border-nau-line pt-3">
            <div className="mb-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
              // CHATS ({chats?.length ?? 0}) — wähle, was der Agent beobachten darf
            </div>
            {chats && chats.length > 0 ? (
              <ul className="flex flex-col gap-1.5">
                {chats.map((c) => (
                  <li key={c.chatId}>
                    <label className="flex cursor-pointer items-start gap-3 py-1">
                      <input
                        type="checkbox"
                        checked={allowlist.includes(c.chatId)}
                        onChange={() => toggle(c.chatId)}
                        className="mt-1 cursor-pointer"
                      />
                      <span className="flex flex-col">
                        <span className="font-sans text-sm text-nau-fg">{c.name || c.chatId}</span>
                        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
                          {c.chatId}
                        </span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            ) : (
              <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
                // Noch keine Chats sichtbar. Schreib der Nummer einmal — sie erscheinen
                dann automatisch (du kannst auch ohne Auswahl speichern und später ergänzen).
              </div>
            )}
          </div>
        </div>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        {step === "connected" && (
          <button
            type="button"
            onClick={save}
            disabled={busy}
            className="min-h-11 cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40"
          >
            {busy ? "SPEICHERE …" : "SPEICHERN"}
          </button>
        )}
        <button
          type="button"
          onClick={onCancel}
          className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:text-nau-fg"
        >
          ABBRECHEN
        </button>
      </div>

      {error && (
        <div className="mt-3 font-mono text-[11px] tracking-mono text-nau-danger">// {error}</div>
      )}
    </div>
  );
}

function FormRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">{label}</span>
      {children}
    </label>
  );
}
