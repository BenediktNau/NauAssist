import { Settings } from "lucide-react";

interface HeaderProps {
  onOpenSettings?: () => void;
  /** Right-side meta text. Defaults to the chat status line. */
  meta?: string;
  /** When true, hides the small subtitle under the brand. */
  compact?: boolean;
}

// HINWEIS: Die früheren Mobile-Top-Tabs (CHAT / KALENDER / EMPFEHLUNGEN) wurden
// entfernt — die Navigation läuft auf Mobile jetzt über <MobileTabBar /> (unten).
// Dadurch sind `currentTab` / `onSelectTab` hier nicht mehr nötig. Der Header ist
// auf Mobile schlank: nur Brand + Settings-Icon. LIVE/Meta bleiben Desktop-only.
export function Header({
  onOpenSettings,
  meta = "3 KALENDER · 24 EVENTS",
  compact = false,
}: HeaderProps) {
  return (
    <div className="flex items-center justify-between border-b border-nau-line px-4 py-3 lg:px-12 lg:py-5">
      <div className="flex items-center gap-3 lg:gap-4">
        <span className="inline-flex h-8 w-8 items-center justify-center bg-nau-accent font-mono text-[14px] font-bold text-nau-bg">
          N
        </span>
        <div className="flex flex-col leading-tight">
          <span className="font-sans text-lg font-semibold text-nau-fg">NauAssist</span>
          {!compact && (
            <span className="hidden font-mono text-[11px] tracking-mono text-nau-fg-dim lg:inline">
              // CALENDAR_AGENT · v0.4
            </span>
          )}
        </div>
      </div>

      <div className="flex items-center gap-4 lg:gap-6">
        <span className="hidden font-mono text-[11px] tracking-mono text-nau-fg-dim lg:inline">
          {meta}
        </span>
        <span className="hidden items-center gap-2 lg:inline-flex">
          <span
            className="nau-dot pulse h-2 w-2"
            style={{ background: "#facc15", boxShadow: "0 0 10px #facc15" }}
          />
          <span className="font-mono text-[11px] tracking-mono-wide text-nau-accent">LIVE</span>
        </span>
        {onOpenSettings && (
          <button
            type="button"
            onClick={onOpenSettings}
            aria-label="Einstellungen öffnen"
            className="inline-flex h-11 w-11 items-center justify-center text-nau-fg-dim transition-colors hover:text-nau-accent lg:h-auto lg:w-auto"
          >
            <Settings size={20} strokeWidth={1.5} />
          </button>
        )}
      </div>
    </div>
  );
}
