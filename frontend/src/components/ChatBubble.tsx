import type { SlotInfo } from "@/api/types";
import type { ChatBubble as ChatBubbleData } from "@/hooks/useChat";
import { SlotCard, formatSlot } from "./SlotCard";

interface ChatBubbleProps {
  bubble: ChatBubbleData;
  onPickSlot: (slot: SlotInfo) => void;
  pickDisabled: boolean;
}

export function ChatBubble({ bubble, onPickSlot, pickDisabled }: ChatBubbleProps) {
  const isUser = bubble.role === "user";
  const align = isUser ? "items-end" : "items-start";
  const bg = isUser ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-900";

  return (
    <div className={`flex flex-col gap-2 ${align}`}>
      <div className={`max-w-[80%] rounded-2xl px-4 py-2 ${bg}`}>
        {bubble.content || (bubble.streaming ? <span className="opacity-50">…</span> : null)}
        {bubble.incomplete && (
          <div className="text-xs italic opacity-70 mt-1">(Antwort unvollständig)</div>
        )}
      </div>
      {bubble.proposals && bubble.proposals.length > 0 && (
        <div className="flex flex-col gap-2 max-w-[80%]">
          {bubble.proposals.map((slot, idx) => (
            <SlotCard
              key={`${bubble.id}-${idx}-${slot.start}`}
              slot={slot}
              onPick={onPickSlot}
              disabled={pickDisabled}
            />
          ))}
        </div>
      )}
    </div>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export { formatSlot };
