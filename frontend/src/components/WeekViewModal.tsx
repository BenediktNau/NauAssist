import { useEffect } from "react";
import { createPortal } from "react-dom";
import { CalendarBoard } from "./calendar/CalendarBoard";
import type { AppPage } from "@/App";

interface WeekViewModalProps {
  open: boolean;
  onClose: () => void;
  onNavigate: (page: AppPage) => void;
  reloadKey?: number;
}

export function WeekViewModal({ open, onClose, onNavigate, reloadKey }: WeekViewModalProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open) return null;

  const content = (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-black/60 p-4"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="flex max-h-[90vh] w-full max-w-[1200px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // WOCHENÜBERSICHT
          </span>
          <button
            type="button"
            onClick={onClose}
            className="cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent"
          >
            SCHLIESSEN
          </button>
        </header>
        <div className="flex-1 overflow-y-auto px-5 py-4">
          <CalendarBoard
            variant="full"
            onNavigate={onNavigate}
            reloadKey={reloadKey}
          />
        </div>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}
