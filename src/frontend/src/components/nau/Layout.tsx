import type { ReactNode } from "react";
import { Header } from "@/components/nau/Header";
import { MobileTabBar } from "@/components/nau/MobileTabBar";
import type { AppPage } from "@/App";

type TabKey = "chat" | "calendar" | "recommendations" | "watchers";

interface LayoutProps {
  /** Aktive Tab-Seite — steuert den gleitenden Indikator der MobileTabBar. */
  current: TabKey;
  onNavigate: (page: AppPage) => void;
  /** Capabilities-gated: Watcher-Tab nur zeigen, wenn Watch-Jobs am Backend aktiv sind. */
  watchersEnabled?: boolean;
  /** Austauschbarer, animierter Seiten-Inhalt. */
  children: ReactNode;
}

/**
 * Persistente Hülle der Tab-Seiten (Chat / Kalender / Empfehlungen): statischer
 * Header oben, statische MobileTabBar unten, dazwischen der wechselnde Content.
 *
 * Header und Tab-Leiste bleiben über Seitenwechsel hinweg gemountet — nur
 * `children` (in App über `key` neu gemountet + animiert) tauscht aus. So
 * „lädt" die Chrome nicht mehr neu, und nur die Inhaltsfläche bewegt sich.
 *
 * `main` reserviert unten Platz für die fixierte Tab-Leiste (Mobile) und kappt
 * den horizontalen Überhang der Wechsel-Animation (`overflow-hidden`).
 */
export function Layout({ current, onNavigate, watchersEnabled, children }: LayoutProps) {
  return (
    <div className="flex h-screen flex-col bg-nau-bg text-nau-fg">
      <Header onOpenSettings={() => onNavigate("settings")} />
      <main className="relative min-h-0 flex-1 overflow-hidden pb-[calc(3.5rem+env(safe-area-inset-bottom))] lg:pb-0">
        {children}
      </main>
      <MobileTabBar current={current} onSelect={onNavigate} watchersEnabled={watchersEnabled} />
    </div>
  );
}
