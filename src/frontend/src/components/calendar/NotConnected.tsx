import type { AppPage } from "@/App";

interface NotConnectedProps {
  onNavigate: (page: AppPage) => void;
  hasGoogleCredentials: boolean;
}

export function NotConnected({ onNavigate, hasGoogleCredentials }: NotConnectedProps) {
  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-8 text-center">
      <div className="mb-3 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
        // KEINE VERBINDUNG
      </div>
      <h2 className="m-0 mb-2 font-sans text-2xl font-normal text-nau-fg">
        Google-Kalender ist nicht verbunden.
      </h2>
      <p className="mx-auto mb-6 max-w-[480px] font-sans text-sm leading-relaxed text-nau-fg-dim">
        {hasGoogleCredentials
          ? "Credentials sind hinterlegt — verbinde Google jetzt, um deine Termine zu sehen."
          : "Hinterlege erst die OAuth-Credentials und verbinde dann Google."}
      </p>
      <button
        type="button"
        onClick={() => onNavigate("settings")}
        className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg"
      >
        ZU DEN EINSTELLUNGEN →
      </button>
    </div>
  );
}
