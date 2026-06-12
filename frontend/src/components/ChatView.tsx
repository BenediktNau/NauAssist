import { useEffect, useRef } from "react";
import type { SlotInfo } from "@/api/types";
import { useChat } from "@/hooks/useChat";
import { ChatBubble } from "./ChatBubble";
import { ClearDivider } from "./ClearDivider";
import { MessageInput } from "./MessageInput";
import { formatSlot } from "./SlotCard";
import { Header } from "./nau/Header";
import { CalendarBoard } from "./calendar/CalendarBoard";
import { DeleteEventsModal } from "./DeleteEventsModal";
import { FreeSlotsModal } from "./FreeSlotsModal";
import { MoveEventsModal } from "./MoveEventsModal";
import { NewEventModal } from "./NewEventModal";
import { RulesModal } from "./RulesModal";
import { WeekViewModal } from "./WeekViewModal";
import { ThinkingTerminal } from "./nau/ThinkingTerminal";
import { MobileTabBar } from "./nau/MobileTabBar";
import type { AppPage } from "@/App";

const TOOL_STATUS_LABEL: Record<string, string> = {
  lookup_free_slots: "SUCHE FREIE SLOTS",
  get_calendar_range: "LESE KALENDER",
  create_event: "LEGE TERMIN AN",
  add_rule: "SPEICHERE REGEL",
  delete_rule: "LÖSCHE REGEL",
  list_rules: "LADE REGELN",
  present_proposals: "BEREITE VORSCHLÄGE VOR",
};

interface ChatViewProps {
  onNavigate: (page: AppPage) => void;
}

export function ChatView({ onNavigate }: ChatViewProps) {
  const {
    bubbles,
    toolStatus,
    error,
    sending,
    send,
    activeProposals,
    rulesModalOpen,
    closeRulesModal,
    newEventModalOpen,
    closeNewEventModal,
    weekViewModalOpen,
    closeWeekViewModal,
    freeSlotsModalOpen,
    closeFreeSlotsModal,
    deleteEventsModalOpen,
    closeDeleteEventsModal,
    moveEventsModalOpen,
    closeMoveEventsModal,
    bumpCalendarReload,
  } = useChat();
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [bubbles, toolStatus]);

  const onPickSlot = (slot: SlotInfo) => {
    send(`Ich nehme den Slot ${formatSlot(slot)}.`);
  };

  const lastBubble = bubbles[bubbles.length - 1];
  const nauIsWriting =
    lastBubble?.kind === "message" &&
    lastBubble.role === "assistant" &&
    lastBubble.streaming &&
    lastBubble.content.length > 0;

  const liveLabel =
    sending && !nauIsWriting
      ? toolStatus && toolStatus.state === "started"
        ? (TOOL_STATUS_LABEL[toolStatus.name] ?? `WERKZEUG: ${toolStatus.name.toUpperCase()}`)
        : "DENKE NACH"
      : null;

  return (
    <div className="flex h-screen flex-col bg-nau-bg text-nau-fg pb-[calc(3.5rem+env(safe-area-inset-bottom))] lg:pb-0">
      <Header onOpenSettings={() => onNavigate("settings")} />

      <main className="min-h-0 flex-1 px-0 py-0 lg:px-10 lg:py-8">
        <div className="mx-auto flex h-full w-full max-w-[1480px] gap-6">
          {/* ── Chat card ───────────────────────────────────── */}
          <section className="flex min-w-0 flex-1 flex-col bg-nau-bg-alt lg:rounded-[4px] lg:border lg:border-nau-line">
            <div ref={scrollRef} className="flex-1 overflow-y-auto px-5 py-6 lg:px-8">
              {bubbles.length === 0 && (
                <WelcomeBlock onPickPrompt={(text) => send(text)} />
              )}
              {bubbles.map((b) =>
                b.kind === "clear-marker" ? (
                  <ClearDivider key={b.id} createdAt={b.createdAt} />
                ) : (
                  <ChatBubble
                    key={b.id}
                    bubble={b}
                    onPickSlot={onPickSlot}
                    pickDisabled={sending}
                  />
                ),
              )}
              {liveLabel && <ThinkingTerminal label={liveLabel} />}
              {error && (
                <div className="mt-4 border border-nau-danger bg-white/[0.02] p-4 font-mono text-sm leading-relaxed text-nau-danger">
                  [ ! ] {error}
                </div>
              )}
              <div ref={bottomRef} />
            </div>

            <div className="border-t border-nau-line px-5 py-4 lg:px-8">
              <MessageInput onSend={send} disabled={sending} />
            </div>
          </section>

          {/* ── Calendar column ─────────────────────────── */}
          <aside className="hidden w-[560px] shrink-0 flex-col gap-5 overflow-y-auto pr-1 lg:flex xl:w-[620px]">
            <CalendarBoard
              variant="compact"
              onNavigate={onNavigate}
              proposals={activeProposals}
              onPickProposal={onPickSlot}
            />
          </aside>
        </div>
      </main>

      <RulesModal open={rulesModalOpen} onClose={closeRulesModal} />
      <NewEventModal
        open={newEventModalOpen}
        onClose={closeNewEventModal}
        onCreated={bumpCalendarReload}
      />
      <WeekViewModal
        open={weekViewModalOpen}
        onClose={closeWeekViewModal}
        onNavigate={onNavigate}
      />
      <FreeSlotsModal open={freeSlotsModalOpen} onClose={closeFreeSlotsModal} />
      <DeleteEventsModal
        open={deleteEventsModalOpen}
        onClose={closeDeleteEventsModal}
        onMutated={bumpCalendarReload}
      />
      <MoveEventsModal
        open={moveEventsModalOpen}
        onClose={closeMoveEventsModal}
        onMutated={bumpCalendarReload}
      />

      <MobileTabBar current="chat" onSelect={onNavigate} />
    </div>
  );
}

interface WelcomeBlockProps {
  onPickPrompt: (text: string) => void;
}

function WelcomeBlock({ onPickPrompt }: WelcomeBlockProps) {
  const prompts = [
    { cmd: "/frei", text: "Wann hab ich Zeit für einen Friseur diese Woche?" },
    { cmd: "/verschieben", text: "Verschiebe das 1:1 mit Lina auf morgen" },
    { cmd: "/termin", text: "Plane mir 2h Deep Work für die Spec" },
  ];

  return (
    <div className="flex flex-col justify-center py-14">
      <div className="mb-10 flex items-center gap-3.5">
        <span className="font-mono text-[15px] font-bold text-nau-accent">01</span>
        <span className="h-px w-10 bg-nau-line" />
        <span className="font-mono text-[12px] tracking-mono-wide text-nau-fg-dim">
          GUTEN MORGEN, NAU
        </span>
      </div>

      <h1 className="m-0 mb-5 font-sans text-4xl font-normal leading-[1.05] tracking-tight text-nau-fg lg:text-6xl">
        Was steht heute
        <br />
        auf{" "}
        <span className="font-mono font-bold text-nau-accent">deinem Plan</span>?
      </h1>
      <p className="m-0 mb-10 max-w-[560px] font-sans text-lg leading-relaxed text-nau-fg-dim">
        Ich lese deine Kalender, finde freie Slots, mache dir Vorschläge — und verschiebe
        Termine, bevor sie kollidieren.
      </p>

      <div className="flex flex-col gap-3">
        <span className="mb-1 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
          // SCHNELLSTART
        </span>
        {prompts.map((p) => (
          <button
            key={p.text}
            type="button"
            onClick={() => onPickPrompt(p.text)}
            className="flex cursor-pointer items-center gap-4 border border-nau-line bg-white/[0.02] px-5 py-4 text-left font-sans text-base text-nau-fg transition-colors hover:border-nau-accent hover:bg-white/[0.04]"
          >
            <span className="w-[88px] font-mono text-[11px] tracking-mono text-nau-accent">
              {p.cmd}
            </span>
            <span className="flex-1">{p.text}</span>
            <span className="text-lg text-nau-fg-dim">→</span>
          </button>
        ))}
      </div>
    </div>
  );
}
