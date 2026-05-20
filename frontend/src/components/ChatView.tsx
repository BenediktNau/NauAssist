import { useEffect, useRef } from "react";
import { ScrollArea } from "@/components/ui/scroll-area";
import type { SlotInfo } from "@/api/types";
import { useChat } from "@/hooks/useChat";
import { ChatBubble } from "./ChatBubble";
import { MessageInput } from "./MessageInput";
import { formatSlot } from "./SlotCard";

const TOOL_STATUS_LABEL: Record<string, string> = {
  lookup_free_slots: "Kalender wird durchsucht…",
  get_calendar_range: "Kalender wird gelesen…",
  create_event: "Termin wird angelegt…",
  add_rule: "Regel wird gespeichert…",
  delete_rule: "Regel wird gelöscht…",
  list_rules: "Regeln werden geladen…",
  present_proposals: "Vorschläge werden vorbereitet…",
};

export function ChatView() {
  const { bubbles, toolStatus, error, sending, send } = useChat();
  const bottomRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [bubbles, toolStatus]);

  const onPickSlot = (slot: SlotInfo) => {
    send(`Ich nehme den Slot ${formatSlot(slot)}.`);
  };

  const liveStatus =
    toolStatus && toolStatus.state === "started"
      ? (TOOL_STATUS_LABEL[toolStatus.name] ?? `Werkzeug läuft: ${toolStatus.name}`)
      : null;

  return (
    <div className="flex flex-col h-screen max-w-3xl mx-auto bg-white">
      <header className="p-4 border-b">
        <h1 className="text-xl font-semibold">NauAssist</h1>
      </header>

      <ScrollArea className="flex-1 px-4 py-4">
        <div className="flex flex-col gap-4">
          {bubbles.map((b) => (
            <ChatBubble key={b.id} bubble={b} onPickSlot={onPickSlot} pickDisabled={sending} />
          ))}
          {liveStatus && <div className="text-sm text-slate-500 italic">{liveStatus}</div>}
          {error && (
            <div className="text-sm text-red-600 border border-red-200 rounded p-2 bg-red-50">
              {error}
            </div>
          )}
          <div ref={bottomRef} />
        </div>
      </ScrollArea>

      <MessageInput onSend={send} disabled={sending} />
    </div>
  );
}
