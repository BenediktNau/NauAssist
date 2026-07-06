import { useEffect, useState } from "react";
import { getPushoverSettings, updatePushoverSettings } from "@/api/pushover";

interface PushoverSectionProps {
  anchor: string;
}

/**
 * Pushover-Zugangsdaten (App-Token + User-Key von pushover.net). Der Server gibt
 * Secrets nie zurück — angezeigt wird nur, ob sie hinterlegt sind; die Felder
 * dienen ausschließlich dem (Über-)Schreiben.
 */
export function PushoverSection({ anchor }: PushoverSectionProps) {
  const [configured, setConfigured] = useState<boolean | null>(null);
  const [token, setToken] = useState("");
  const [userKey, setUserKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const s = await getPushoverSettings();
        setConfigured(s.hasToken && s.hasUserKey);
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
        setConfigured(false);
      }
    })();
  }, []);

  const save = async () => {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      await updatePushoverSettings(token.trim(), userKey.trim());
      const isSet = token.trim().length > 0 && userKey.trim().length > 0;
      setConfigured(isSet);
      setToken("");
      setUserKey("");
      setInfo(isSet ? "Pushover-Zugangsdaten gespeichert." : "Pushover-Zugangsdaten entfernt.");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const clear = async () => {
    setToken("");
    setUserKey("");
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      await updatePushoverSettings("", "");
      setConfigured(false);
      setInfo("Pushover-Zugangsdaten entfernt.");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const inputClass =
    "w-full border border-nau-line bg-nau-bg px-3 py-2 font-mono text-sm text-nau-fg " +
    "placeholder:text-nau-fg-dim focus:border-nau-accent focus:outline-none";

  return (
    <section id={anchor} className="flex flex-col gap-4">
      <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
        {configured === null
          ? "// STATUS: LADE …"
          : configured
            ? "// STATUS: KONFIGURIERT — Watch-Jobs können über Pushover melden"
            : "// STATUS: NICHT KONFIGURIERT — Token & User-Key von pushover.net eintragen"}
      </div>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">APP-TOKEN</span>
        <input
          type="password"
          value={token}
          onChange={(e) => setToken(e.target.value)}
          placeholder="a1b2c3…"
          autoComplete="off"
          className={inputClass}
        />
      </label>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">USER-KEY</span>
        <input
          type="password"
          value={userKey}
          onChange={(e) => setUserKey(e.target.value)}
          placeholder="u9x8y7…"
          autoComplete="off"
          className={inputClass}
        />
      </label>

      <div className="flex gap-3">
        <button
          type="button"
          disabled={busy || token.trim().length === 0 || userKey.trim().length === 0}
          onClick={() => void save()}
          className="cursor-pointer border border-nau-accent px-4 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-accent transition-colors hover:bg-nau-accent hover:text-nau-bg disabled:cursor-not-allowed disabled:opacity-40"
        >
          SPEICHERN
        </button>
        {configured && (
          <button
            type="button"
            disabled={busy}
            onClick={() => void clear()}
            className="cursor-pointer border border-nau-line px-4 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-red-500/60 hover:text-red-400 disabled:opacity-40"
          >
            ENTFERNEN
          </button>
        )}
      </div>

      {info && <p className="font-mono text-[11px] text-emerald-400">{info}</p>}
      {error && <p className="font-mono text-[11px] text-red-400">{error}</p>}
    </section>
  );
}
