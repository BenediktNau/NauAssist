import { useEffect, useState } from "react";
import {
  createImapAccount,
  deleteSourceAccount,
  listImapFolders,
  listImapFoldersForAccount,
  listSourceAccounts,
  updateSourceAccount,
  type ImapCredentialsInput,
  type SourceAccountDto,
} from "@/api/source-accounts";

interface ImapSectionProps {
  anchor: string;
}

export function ImapSection({ anchor }: ImapSectionProps) {
  const [accounts, setAccounts] = useState<SourceAccountDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  const reload = async () => {
    setLoading(true);
    setError(null);
    try {
      setAccounts(await listSourceAccounts("imap"));
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
    <div id={anchor}>
      <div className="mb-6 pt-2">
        <div className="mb-4 flex items-center gap-3.5">
          <span className="font-mono text-[13px] font-bold text-nau-accent">06</span>
          <span className="h-px w-8 bg-nau-line" />
          <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            E-MAIL (IMAP/SMTP)
          </span>
        </div>
        <h2 className="m-0 mb-2 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg">
          Postfächer.
        </h2>
        <p className="m-0 max-w-[640px] font-sans text-sm leading-relaxed text-nau-fg-dim">
          IMAP fürs Lesen, SMTP für Antworten. Für Gmail braucht's ein App-Passwort
          (2FA → Sicherheit → App-Passwörter). Du wählst pro Account, welche Ordner
          der Agent beobachten darf.
        </p>
      </div>

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
            <ImapAccountCard key={a.id} account={a} onChanged={reload} />
          ))}
        </ul>
      )}

      <div className="mt-5">
        {adding ? (
          <AddImapForm
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
            + POSTFACH HINZUFÜGEN
          </button>
        )}
      </div>
    </div>
  );
}

interface AccountCardProps {
  account: SourceAccountDto;
  onChanged: () => Promise<void>;
}

function ImapAccountCard({ account, onChanged }: AccountCardProps) {
  const [folders, setFolders] = useState<string[] | null>(null);
  const [draftAllowlist, setDraftAllowlist] = useState<string[]>(account.allowlist);
  const [loadingFolders, setLoadingFolders] = useState(false);
  const [savingAllowlist, setSavingAllowlist] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => setDraftAllowlist(account.allowlist), [account.allowlist]);

  const loadFolders = async () => {
    setLoadingFolders(true);
    setError(null);
    try {
      setFolders(await listImapFoldersForAccount(account.id));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoadingFolders(false);
    }
  };

  const toggle = (f: string) =>
    setDraftAllowlist((prev) =>
      prev.includes(f) ? prev.filter((x) => x !== f) : [...prev, f],
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
    if (!confirm(`Postfach "${account.displayName}" wirklich löschen?`)) return;
    setError(null);
    try {
      await deleteSourceAccount(account.id);
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const dirty =
    draftAllowlist.length !== account.allowlist.length
    || draftAllowlist.some((r) => !account.allowlist.includes(r));

  return (
    <li className="border border-nau-line bg-white/[0.02] p-4">
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <span className="font-sans text-base font-medium text-nau-fg">{account.displayName}</span>
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {account.credentials.username ?? "—"}
        </span>
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {account.credentials.imapHost ?? "—"} · {account.credentials.smtpHost ?? "—"}
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
        // {account.allowlist.length} Ordner freigegeben
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent"
        >
          {expanded ? "SCHLIESSEN" : "ORDNER VERWALTEN"}
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
              onClick={loadFolders}
              disabled={loadingFolders}
              className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-50"
            >
              {loadingFolders ? "LADE …" : folders === null ? "ORDNER LADEN" : "AKTUALISIEREN"}
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

          {folders && folders.length > 0 && (
            <ul className="mt-3 flex flex-col gap-1.5">
              {folders.map((f) => {
                const active = draftAllowlist.includes(f);
                return (
                  <li key={f}>
                    <label className="flex cursor-pointer items-center gap-3 py-1">
                      <input
                        type="checkbox"
                        checked={active}
                        onChange={() => toggle(f)}
                        className="cursor-pointer"
                      />
                      <span className="font-sans text-sm text-nau-fg">{f}</span>
                    </label>
                  </li>
                );
              })}
            </ul>
          )}

          {folders && folders.length === 0 && (
            <div className="mt-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
              // Keine Ordner gefunden.
            </div>
          )}
        </div>
      )}
    </li>
  );
}

interface AddFormProps {
  onCreated: () => Promise<void>;
  onCancel: () => void;
}

function AddImapForm({ onCreated, onCancel }: AddFormProps) {
  const [displayName, setDisplayName] = useState("");
  const [imapHost, setImapHost] = useState("imap.gmail.com");
  const [imapPort, setImapPort] = useState("993");
  const [imapSsl, setImapSsl] = useState(true);
  const [smtpHost, setSmtpHost] = useState("smtp.gmail.com");
  const [smtpPort, setSmtpPort] = useState("587");
  const [smtpStartTls, setSmtpStartTls] = useState(true);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [fromName, setFromName] = useState("");

  const [folders, setFolders] = useState<string[] | null>(null);
  const [allowlist, setAllowlist] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const creds = (): ImapCredentialsInput => ({
    imapHost: imapHost.trim(),
    imapPort: parseInt(imapPort, 10),
    imapSsl,
    smtpHost: smtpHost.trim(),
    smtpPort: parseInt(smtpPort, 10),
    smtpStartTls,
    username: username.trim(),
    password,
    fromName: fromName.trim() || undefined,
  });

  const connect = async () => {
    setBusy(true);
    setError(null);
    try {
      setFolders(await listImapFolders(creds()));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      await createImapAccount(displayName.trim() || username.trim() || "Postfach", creds(), allowlist);
      await onCreated();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const toggle = (f: string) =>
    setAllowlist((prev) => (prev.includes(f) ? prev.filter((x) => x !== f) : [...prev, f]));

  return (
    <div className="border border-nau-line bg-white/[0.02] p-4">
      <div className="mb-3 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
        // NEUES POSTFACH
      </div>
      <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
        <FormRow label="Anzeigename">
          <Input value={displayName} onChange={setDisplayName} placeholder="z.B. Privat-Gmail" />
        </FormRow>
        <FormRow label="E-Mail / Username">
          <Input value={username} onChange={setUsername} placeholder="you@example.com" />
        </FormRow>
        <FormRow label="Passwort / App-Passwort">
          <Input value={password} onChange={setPassword} type="password" placeholder="••••" />
        </FormRow>
        <FormRow label="Absender-Name (optional)">
          <Input value={fromName} onChange={setFromName} placeholder="z.B. Benedikt Nau" />
        </FormRow>
        <FormRow label="IMAP-Host">
          <Input value={imapHost} onChange={setImapHost} />
        </FormRow>
        <FormRow label="IMAP-Port">
          <div className="flex items-center gap-3">
            <Input value={imapPort} onChange={setImapPort} className="max-w-[120px]" />
            <label className="flex items-center gap-2 font-mono text-[11px] text-nau-fg-dim">
              <input type="checkbox" checked={imapSsl} onChange={(e) => setImapSsl(e.target.checked)} />
              SSL
            </label>
          </div>
        </FormRow>
        <FormRow label="SMTP-Host">
          <Input value={smtpHost} onChange={setSmtpHost} />
        </FormRow>
        <FormRow label="SMTP-Port">
          <div className="flex items-center gap-3">
            <Input value={smtpPort} onChange={setSmtpPort} className="max-w-[120px]" />
            <label className="flex items-center gap-2 font-mono text-[11px] text-nau-fg-dim">
              <input
                type="checkbox"
                checked={smtpStartTls}
                onChange={(e) => setSmtpStartTls(e.target.checked)}
              />
              STARTTLS
            </label>
          </div>
        </FormRow>
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={connect}
          disabled={busy || !imapHost || !username || !password}
          className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-40"
        >
          {busy && folders === null ? "VERBINDE …" : "VERBINDEN & ORDNER LADEN"}
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:text-nau-fg"
        >
          ABBRECHEN
        </button>
        {folders !== null && (
          <button
            type="button"
            onClick={save}
            disabled={busy}
            className="min-h-11 cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40"
          >
            {busy ? "SPEICHERE …" : "SPEICHERN"}
          </button>
        )}
      </div>

      {error && (
        <div className="mt-3 font-mono text-[11px] tracking-mono text-nau-danger">// {error}</div>
      )}

      {folders !== null && (
        <div className="mt-4 border-t border-nau-line pt-3">
          <div className="mb-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
            // ORDNER ({folders.length}) — wähle, was der Agent beobachten darf
          </div>
          {folders.length === 0 ? (
            <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
              // Keine Ordner gefunden.
            </div>
          ) : (
            <ul className="flex flex-col gap-1.5">
              {folders.map((f) => (
                <li key={f}>
                  <label className="flex cursor-pointer items-center gap-3 py-1">
                    <input
                      type="checkbox"
                      checked={allowlist.includes(f)}
                      onChange={() => toggle(f)}
                      className="cursor-pointer"
                    />
                    <span className="font-sans text-sm text-nau-fg">{f}</span>
                  </label>
                </li>
              ))}
            </ul>
          )}
        </div>
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

function Input({
  value,
  onChange,
  placeholder,
  type = "text",
  className = "",
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  type?: "text" | "password";
  className?: string;
}) {
  return (
    <input
      type={type}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      className={`min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg ${className}`}
    />
  );
}
