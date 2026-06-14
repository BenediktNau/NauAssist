import { CalendarBoard } from "@/components/calendar/CalendarBoard";
import type { AppPage } from "@/App";

interface CalendarPageProps {
  onNavigate: (page: AppPage) => void;
}

export function CalendarPage({ onNavigate }: CalendarPageProps) {
  // Header + MobileTabBar liefert die Layout-Hülle; hier nur der scrollbare Inhalt.
  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto w-full max-w-[1400px] px-4 py-6 lg:px-8 lg:py-8">
        <CalendarBoard variant="full" onNavigate={onNavigate} />
      </div>
    </div>
  );
}
