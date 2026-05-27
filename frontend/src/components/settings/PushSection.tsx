import { useEffect, useState } from "react";
import {
  deleteSubscription,
  getCurrentSubscription,
  getVapidPublicKey,
  isPushSupported,
  postSubscription,
  sendTestPush,
  subscribeBrowser,
  subscriptionToPayload,
  unsubscribeBrowser,
} from "@/api/push";

interface PushSectionProps {
  anchor: string;
}

type Status = "loading" | "unsupported" | "denied" | "off" | "on";

export function PushSection({ anchor }: PushSectionProps) {
  const [status, setStatus] = useState<Status>("loading");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const refresh = async () => {
    setError(null);
    if (!isPushSupported()) {
      setStatus("unsupported");
      return;
    }
    if (Notification.permission === "denied") {
      setStatus("denied");
      return;
    }
    const sub = await getCurrentSubscription();
    setStatus(sub ? "on" : "off");
  };

  useEffect(() => {
    void refresh();
  }, []);

  const enable = async () => {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      if (Notification.permission !== "granted") {
        const perm = await Notification.requestPermission();
        if (perm !== "granted") {
          setStatus(perm === "denied" ? "denied" : "off");
          return;
        }
      }

      const { publicKey, configured } = await getVapidPublicKey();
      if (!configured || !publicKey) {
        throw new Error("VAPID-Keys auf dem Server nicht konfiguriert.");
      }

      const sub = await subscribeBrowser(publicKey);
      await postSubscription(subscriptionToPayload(sub));
      setStatus("on");
      setInfo("Push aktiv auf diesem Gerät.");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const disable = async () => {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const endpoint = await unsubscribeBrowser();
      if (endpoint) {
        await deleteSubscription(endpoint);
      }
      setStatus("off");
      setInfo("Push deaktiviert.");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const test = async () => {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const { sent } = await sendTestPush();
      setInfo(`Test-Push an ${sent} Gerät(e) geschickt.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div id={anchor}>
      <div className="mb-6 pt-2">
        <div className="mb-4 flex items-center gap-3.5">
          <span className="font-mono text-[13px] font-bold text-nau-accent">05</span>
          <span className="h-px w-8 bg-nau-line" />
          <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            PUSH-NOTIFICATIONS
          </span>
        </div>
        <h2 className="m-0 mb-2 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg">
          Aufs Handy.
        </h2>
        <p className="m-0 max-w-[640px] font-sans text-sm leading-relaxed text-nau-fg-dim">
          Web Push für PWAs &mdash; tippe auf die Benachrichtigung und springst direkt in die
          Empfehlungen. Auf iOS musst du die App vorher per &quot;Zum Home-Bildschirm
          hinzufügen&quot; installieren (iOS 16.4+).
        </p>
      </div>

      {error && (
        <div className="mb-4 border border-nau-danger bg-white/[0.02] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
          // {error}
        </div>
      )}
      {info && (
        <div className="mb-4 border border-nau-line bg-white/[0.02] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
          // {info}
        </div>
      )}

      {status === "loading" && (
        <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">// LADE …</div>
      )}

      {status === "unsupported" && (
        <div className="border border-dashed border-nau-line px-4 py-6 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
          // Dieser Browser unterstützt keine Web-Push-Notifications.
        </div>
      )}

      {status === "denied" && (
        <div className="border border-dashed border-nau-danger px-4 py-6 font-mono text-[11px] tracking-mono text-nau-danger">
          // Benachrichtigungen sind im Browser geblockt. Site-Settings öffnen und freigeben.
        </div>
      )}

      {status === "off" && (
        <button
          type="button"
          onClick={enable}
          disabled={busy}
          className="min-h-11 cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40"
        >
          {busy ? "AKTIVIERE …" : "PUSH AKTIVIEREN"}
        </button>
      )}

      {status === "on" && (
        <div className="flex flex-wrap gap-2">
          <span className="inline-flex items-center px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-accent">
            ✓ AKTIV
          </span>
          <button
            type="button"
            onClick={test}
            disabled={busy}
            className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-40"
          >
            {busy ? "SENDE …" : "TEST-PUSH"}
          </button>
          <button
            type="button"
            onClick={disable}
            disabled={busy}
            className="min-h-11 cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-danger hover:text-nau-danger disabled:opacity-40"
          >
            DEAKTIVIEREN
          </button>
        </div>
      )}
    </div>
  );
}
