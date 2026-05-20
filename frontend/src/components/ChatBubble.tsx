import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";
import type { SlotInfo } from "@/api/types";
import type { ChatBubble as ChatBubbleData } from "@/hooks/useChat";
import { SlotCard, formatSlot } from "./SlotCard";

interface ChatBubbleProps {
  bubble: ChatBubbleData;
  onPickSlot: (slot: SlotInfo) => void;
  pickDisabled: boolean;
}

const MARKDOWN_COMPONENTS: Components = {
  p: ({ children }) => <p className="m-0 whitespace-pre-wrap">{children}</p>,
  strong: ({ children }) => <strong className="font-semibold">{children}</strong>,
  em: ({ children }) => <em className="italic">{children}</em>,
  code: ({ children }) => (
    <code className="rounded bg-black/10 px-1 py-0.5 font-mono text-[0.9em]">{children}</code>
  ),
  pre: ({ children }) => (
    <pre className="my-2 overflow-x-auto rounded bg-black/10 p-2 text-sm">{children}</pre>
  ),
  ul: ({ children }) => <ul className="my-1 list-disc pl-5">{children}</ul>,
  ol: ({ children }) => <ol className="my-1 list-decimal pl-5">{children}</ol>,
  li: ({ children }) => <li>{children}</li>,
  a: ({ children, href }) => (
    <a href={href} target="_blank" rel="noreferrer" className="underline">
      {children}
    </a>
  ),
  h1: ({ children }) => <h1 className="text-lg font-semibold">{children}</h1>,
  h2: ({ children }) => <h2 className="text-base font-semibold">{children}</h2>,
  h3: ({ children }) => <h3 className="text-sm font-semibold">{children}</h3>,
  blockquote: ({ children }) => (
    <blockquote className="border-l-2 border-current/30 pl-2 italic opacity-90">
      {children}
    </blockquote>
  ),
  hr: () => <hr className="my-2 border-current/20" />,
};

export function ChatBubble({ bubble, onPickSlot, pickDisabled }: ChatBubbleProps) {
  const isUser = bubble.role === "user";
  const align = isUser ? "items-end" : "items-start";
  const bg = isUser ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-900";

  return (
    <div className={`flex flex-col gap-2 ${align}`}>
      <div className={`max-w-[80%] rounded-2xl px-4 py-2 ${bg}`}>
        {bubble.content ? (
          <ReactMarkdown remarkPlugins={[remarkGfm]} components={MARKDOWN_COMPONENTS}>
            {bubble.content}
          </ReactMarkdown>
        ) : bubble.streaming ? (
          <span className="opacity-50">…</span>
        ) : null}
        {bubble.incomplete && (
          <div className="mt-1 text-xs italic opacity-70">(Antwort unvollständig)</div>
        )}
      </div>
      {bubble.proposals && bubble.proposals.length > 0 && (
        <div className="flex max-w-[80%] flex-col gap-2">
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
