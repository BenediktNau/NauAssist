import { ArrowLeft } from "lucide-react";
import { Header } from "@/components/nau/Header";
import { CalendarCard } from "@/components/nau/CalendarCard";
import { FocusPanel } from "@/components/nau/FocusPanel";
import type { AppPage } from "@/App";

interface CalendarPageProps {
  onNavigate: (page: AppPage) => void;
}

export function CalendarPage({ onNavigate }: CalendarPageProps) {
  return (
    <div className="flex min-h-screen flex-col bg-nau-bg text-nau-fg">
      <Header
        onOpenSettings={() => onNavigate("settings")}
        meta="3 KAL · 24 EVT · 2 CFL"
        currentTab="calendar"
        onSelectTab={onNavigate}
      />

      <div className="mx-auto w-full max-w-[1200px] flex-1 px-4 py-6 lg:px-8 lg:py-8">
        <div className="mb-6 flex items-center justify-between">
          <div>
            <div className="mb-2 hidden font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim lg:block">
              — KALENDER —
            </div>
            <h1 className="m-0 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg lg:text-4xl">
              Deine{" "}
              <span className="font-mono font-bold text-nau-accent">Woche</span>.
            </h1>
          </div>
          <button
            type="button"
            onClick={() => onNavigate("chat")}
            className="hidden cursor-pointer items-center gap-2 border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent lg:inline-flex"
          >
            <ArrowLeft size={14} strokeWidth={1.5} />
            ZURÜCK ZUM CHAT
          </button>
        </div>

        <div className="flex flex-col gap-5">
          <CalendarCard density="comfortable" />
          <FocusPanel />
        </div>

        <div className="mt-6 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
          // STATISCHE DEMO-DATEN · ANBINDUNG AN ECHTE KALENDER FOLGT
        </div>
      </div>
    </div>
  );
}
