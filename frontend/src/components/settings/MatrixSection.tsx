import { useEffect, useState } from "react";
import {
  createMatrixAccount,
  deleteSourceAccount,
  listMatrixRooms,
  listMatrixRoomsForAccount,
  listSourceAccounts,
  updateSourceAccount,
  type MatrixCredentialsInput,
  type MatrixRoomDto,
  type SourceAccountDto,
} from "@/api/source-accounts";

interface MatrixSectionProps {
  /** ID-Anchor für die Side-Nav. */
  anchor: string;
}

export function MatrixSection({ anchor }: MatrixSectionProps) {
  const [accounts, setAccounts] = useState<SourceAccountDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  const reload = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listSourceAccounts("matrix");
      setAccounts(data);
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
      <SectionHead n={3} label="MATRIX" title="Chat-Accounts." />
      <p className="mb-4 max-w-[640px] font-sans text-sm leading-relaxed text-nau-fg-dim">
        Der autonome Agent liest alle 20 min die freigegebenen Räume und schlägt
        passende Termin-Slots vor. Für verschlüsselte Räume empfiehlt sich Pantalaimon
        als Proxy &mdash; die Homeserver-URL zeigt dann auf Pantalaimon statt direkt
        auf den Matrix-Server.
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
            <AccountCard
              key={a.id}
              account={a}
              onChanged={reload}
            />
          ))}
        </ul>
      )}

      <div className="mt-5">
        {adding ? (
          <AddAccountForm
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
            + ACCOUNT HINZUFÜGEN
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
        <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
          {label}
        </span>
      </div>
      <h2 className="m-0 mb-2 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg">
        {title}
      </h2>
    </div>
  );
}

interface AccountCardProps {
  account: SourceAccountDto;
  onChanged: () => Promise<void>;
}

function AccountCard({ account, onChanged }: AccountCardProps) {
  const [rooms, setRooms] = useState<MatrixRoomDto[] | null>(null);
  const [loadingRooms, setLoadingRooms] = useState(false);
  const [savingAllowlist, setSavingAllowlist] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [draftAllowlist, setDraftAllowlist] = useState<string[]>(account.allowlist);

  useEffect(() => {
    setDraftAllowlist(account.allowlist);
  }, [account.allowlist]);

  const loadRooms = async () => {
    setLoadingRooms(true);
    setError(null);
    try {
      const data = await listMatrixRoomsForAccount(account.id);
      setRooms(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoadingRooms(false);
    }
  };

  const toggle = (roomId: string) => {
    setDraftAllowlist((prev) =>
      prev.includes(roomId) ? prev.filter((r) => r !== roomId) : [...prev, roomId],
    );
  };

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

  const removeAccount = async () => {
    if (!confirm(`Account "${account.displayName}" wirklich löschen?`)) return;
    setError(null);
    try {
      await deleteSourceAccount(account.id);
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const allowlistDirty =
    draftAllowlist.length !== account.allowlist.length
    || draftAllowlist.some((r) => !account.allowlist.includes(r));

  return (
    <li className="border border-nau-line bg-white/[0.02] p-4">
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <span className="font-sans text-base font-medium text-nau-fg">{account.displayName}</span>
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {account.credentials.homeserverUrl ?? "—"}
        </span>
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {account.credentials.userId ?? "—"}
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
        // {account.allowlist.length} Raum/Räume freigegeben
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent"
        >
          {expanded ? "SCHLIESSEN" : "RÄUME VERWALTEN"}
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
          onClick={removeAccount}
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
              onClick={loadRooms}
              disabled={loadingRooms}
              className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-50"
            >
              {loadingRooms ? "LADE …" : rooms === null ? "RÄUME LADEN" : "AKTUALISIEREN"}
            </button>
            {allowlistDirty && (
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

          {rooms && rooms.length > 0 && (
            <ul className="mt-3 flex flex-col gap-1.5">
              {rooms.map((r) => {
                const active = draftAllowlist.includes(r.roomId);
                return (
                  <li key={r.roomId}>
                    <label className="flex cursor-pointer items-start gap-3 py-1">
                      <input
                        type="checkbox"
                        checked={active}
                        onChange={() => toggle(r.roomId)}
                        className="mt-1 cursor-pointer"
                      />
                      <span className="flex flex-col">
                        <span className="font-sans text-sm text-nau-fg">
                          {r.displayName ?? r.roomId}
                        </span>
                        <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
                          {r.roomId}
                        </span>
                      </span>
                    </label>
                  </li>
                );
              })}
            </ul>
          )}

          {rooms && rooms.length === 0 && (
            <div className="mt-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
              // Keine Räume gefunden.
            </div>
          )}
        </div>
      )}
    </li>
  );
}

interface AddAccountFormProps {
  onCreated: () => Promise<void>;
  onCancel: () => void;
}

function AddAccountForm({ onCreated, onCancel }: AddAccountFormProps) {
  const [displayName, setDisplayName] = useState("");
  const [homeserverUrl, setHomeserverUrl] = useState("");
  const [userId, setUserId] = useState("");
  const [accessToken, setAccessToken] = useState("");

  const [rooms, setRooms] = useState<MatrixRoomDto[] | null>(null);
  const [allowlist, setAllowlist] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const creds = (): MatrixCredentialsInput => ({
    homeserverUrl: homeserverUrl.trim(),
    userId: userId.trim(),
    accessToken: accessToken.trim(),
  });

  const connect = async () => {
    setBusy(true);
    setError(null);
    try {
      const data = await listMatrixRooms(creds());
      setRooms(data);
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
      await createMatrixAccount(displayName.trim() || userId.trim() || "Matrix", creds(), allowlist);
      await onCreated();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const toggle = (roomId: string) =>
    setAllowlist((prev) =>
      prev.includes(roomId) ? prev.filter((r) => r !== roomId) : [...prev, roomId],
    );

  return (
    <div className="border border-nau-line bg-white/[0.02] p-4">
      <div className="mb-3 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
        // NEUER MATRIX-ACCOUNT
      </div>
      <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
        <FormRow label="Anzeigename">
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="z.B. Privat-Element"
            className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
          />
        </FormRow>
        <FormRow label="Homeserver / Pantalaimon">
          <input
            value={homeserverUrl}
            onChange={(e) => setHomeserverUrl(e.target.value)}
            placeholder="https://matrix.example.org"
            className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
          />
        </FormRow>
        <FormRow label="User-ID">
          <input
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
            placeholder="@nau-agent:example.org"
            className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
          />
        </FormRow>
        <FormRow label="Access-Token">
          <input
            value={accessToken}
            onChange={(e) => setAccessToken(e.target.value)}
            type="password"
            placeholder="syt_…"
            className="min-h-11 w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
          />
        </FormRow>
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={connect}
          disabled={busy || !homeserverUrl || !accessToken}
          className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-40"
        >
          {busy && rooms === null ? "VERBINDE …" : "VERBINDEN & RÄUME LADEN"}
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:text-nau-fg"
        >
          ABBRECHEN
        </button>
        {rooms !== null && (
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

      {rooms !== null && (
        <div className="mt-4 border-t border-nau-line pt-3">
          <div className="mb-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
            // RÄUME ({rooms.length}) — wähle, was der Agent beobachten darf
          </div>
          {rooms.length === 0 ? (
            <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
              // Keine Räume gefunden. Den Agent-Account in Räume einladen und neu laden.
            </div>
          ) : (
            <ul className="flex flex-col gap-1.5">
              {rooms.map((r) => (
                <li key={r.roomId}>
                  <label className="flex cursor-pointer items-start gap-3 py-1">
                    <input
                      type="checkbox"
                      checked={allowlist.includes(r.roomId)}
                      onChange={() => toggle(r.roomId)}
                      className="mt-1 cursor-pointer"
                    />
                    <span className="flex flex-col">
                      <span className="font-sans text-sm text-nau-fg">
                        {r.displayName ?? r.roomId}
                      </span>
                      <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
                        {r.roomId}
                      </span>
                    </span>
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
