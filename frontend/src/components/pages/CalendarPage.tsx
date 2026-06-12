import { Header } from "@/components/nau/Header";
import { MobileTabBar } from "@/components/nau/MobileTabBar";
import { CalendarBoard } from "@/components/calendar/CalendarBoard";
import type { AppPage } from "@/App";

interface CalendarPageProps {
  onNavigate: (page: AppPage) => void;
}

export function CalendarPage({ onNavigate }: CalendarPageProps) {
  return (
    <div className="flex min-h-screen flex-col bg-nau-bg text-nau-fg pb-[calc(3.5rem+env(safe-area-inset-bottom))] lg:pb-0">
      <Header onOpenSettings={() => onNavigate("settings")} />
      <div className="mx-auto w-full max-w-[1400px] flex-1 px-4 py-6 lg:px-8 lg:py-8">
        <CalendarBoard variant="full" onNavigate={onNavigate} />
      </div>
      <MobileTabBar current="calendar" onSelect={onNavigate} />
    </div>
  );
}
