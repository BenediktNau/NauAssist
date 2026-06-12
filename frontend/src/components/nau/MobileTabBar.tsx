import { MessageSquare, CalendarDays, Sparkles } from "lucide-react";
import type { ComponentType } from "react";
import type { AppPage } from "@/App";

type TabKey = "chat" | "calendar" | "recommendations";

interface TabDef {
  key: TabKey;
  label: string;
  aria: string;
  Icon: ComponentType<{ size?: number; strokeWidth?: number }>;
}

const TABS: TabDef[] = [
  { key: "chat", label: "CHAT", aria: "Chat", Icon: MessageSquare },
  { key: "calendar", label: "KALENDER", aria: "Kalender", Icon: CalendarDays },
  { key: "recommendations", label: "EMPF.", aria: "Empfehlungen", Icon: Sparkles },
];

interface MobileTabBarProps {
  /** Aktiver Tab — steuert Hervorhebung. */
  current: TabKey;
  /** Wechselt die Seite. Reicht direkt das `onNavigate`/`setPage` aus App durch. */
  onSelect: (page: AppPage) => void;
}

/**
 * Untere Tab-Leiste für Mobile (< lg). Auf Desktop (lg+) ausgeblendet.
 *
 * Ersetzt die früher in den Header gequetschten Top-Tabs. Daumenfreundliche
 * Tap-Ziele (>= 56px Höhe), aktiver Tab in Akzentfarbe mit oberer
 * Indikatorlinie. Respektiert die iOS-Safe-Area unten.
 *
 * Position: fixed bottom. Die jeweilige Seite muss unten Platz lassen
 * (siehe INTEGRATION.md → `pb-[calc(3.5rem+env(safe-area-inset-bottom))] lg:pb-0`),
 * damit Inhalt/Eingabefeld nicht hinter der Leiste verschwindet.
 */
export function MobileTabBar({ current, onSelect }: MobileTabBarProps) {
  return (
    <nav
      role="tablist"
      aria-label="Hauptnavigation"
      className="fixed inset-x-0 bottom-0 z-40 flex border-t border-nau-line bg-nau-bg/95 pb-[env(safe-area-inset-bottom)] backdrop-blur-sm lg:hidden"
    >
      {TABS.map(({ key, label, aria, Icon }) => {
        const active = current === key;
        return (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={active}
            aria-label={aria}
            onClick={() => onSelect(key)}
            className={
              "-mt-px flex min-h-[56px] flex-1 cursor-pointer flex-col items-center justify-center gap-1 border-t-2 bg-transparent pb-1.5 pt-2 font-mono text-[9px] tracking-mono-wide transition-colors " +
              (active
                ? "border-nau-accent text-nau-accent"
                : "border-transparent text-nau-fg-dim hover:text-nau-fg")
            }
          >
            <Icon size={22} strokeWidth={active ? 2 : 1.6} />
            <span>{label}</span>
          </button>
        );
      })}
    </nav>
  );
}
