import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";
import type { SlotInfo } from "@/api/types";
import type { MessageBubble } from "@/hooks/useChat";
import { SlotSuggestions, formatSlot } from "./SlotCard";

interface ChatBubbleProps {
  bubble: MessageBubble;
  onPickSlot: (slot: SlotInfo) => void;
  pickDisabled: boolean;
}

const MARKDOWN_COMPONENTS: Components = {
  p: ({ children }) => <p className="m-0 whitespace-pre-wrap">{children}</p>,
  strong: ({ children }) => <strong className="font-semibold text-nau-fg">{children}</strong>,
  em: ({ children }) => <em className="italic">{children}</em>,
  code: ({ children }) => (
    <code className="rounded-sm bg-white/10 px-1 py-0.5 font-mono text-[0.9em]">{children}</code>
  ),
  pre: ({ children }) => (
    <pre className="my-2 overflow-x-auto rounded-sm border border-nau-line bg-white/[0.03] p-2 text-sm">
      {children}
    </pre>
  ),
  ul: ({ children }) => <ul className="my-1 list-disc pl-5">{children}</ul>,
  ol: ({ children }) => <ol className="my-1 list-decimal pl-5">{children}</ol>,
  li: ({ children }) => <li>{children}</li>,
  a: ({ children, href }) => (
    <a href={href} target="_blank" rel="noreferrer" className="text-nau-accent underline">
      {children}
    </a>
  ),
  h1: ({ children }) => <h1 className="text-lg font-semibold text-nau-fg">{children}</h1>,
  h2: ({ children }) => <h2 className="text-base font-semibold text-nau-fg">{children}</h2>,
  h3: ({ children }) => <h3 className="text-sm font-semibold text-nau-fg">{children}</h3>,
  blockquote: ({ children }) => (
    <blockquote className="border-l-2 border-white/20 pl-2 italic opacity-90">
      {children}
    </blockquote>
  ),
  hr: () => <hr className="my-2 border-nau-line" />,
};

function formatStamp(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "--:--";
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
}

export function ChatBubble({ bubble, onPickSlot, pickDisabled }: ChatBubbleProps) {
  const isUser = bubble.role === "user";
  const stamp = formatStamp(bubble.createdAt);

  if (isUser) {
    return (
      <div className="mb-7 flex justify-end">
        <div className="max-w-[78%]">
          <div className="mb-2 text-right font-mono text-[11px] tracking-mono text-nau-fg-dim">
            YOU · {stamp}
          </div>
          <div className="rounded-[4px] border border-nau-line bg-white/[0.04] px-5 py-3.5 font-sans text-base leading-relaxed text-nau-fg">
            {bubble.content}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="mb-7 max-w-[88%]">
      <div className="mb-2.5 flex items-center gap-2.5">
        <span
          className="h-2 w-2 rounded-full"
          style={{ background: "#facc15", boxShadow: "0 0 8px #facc15" }}
        />
        <span className="font-mono text-[11px] tracking-mono-wide text-nau-accent">
          NAU · {stamp}
        </span>
      </div>
      <div className="rounded-[4px] border border-nau-line bg-white/[0.02] px-5 py-3.5 font-sans text-[17px] leading-relaxed text-nau-fg">
        {bubble.content ? (
          <ReactMarkdown remarkPlugins={[remarkGfm]} components={MARKDOWN_COMPONENTS}>
            {bubble.content}
          </ReactMarkdown>
        ) : bubble.streaming ? (
          <span className="opacity-50">…</span>
        ) : null}
        {bubble.incomplete && (
          <div className="mt-2 inline-block rounded-[3px] border border-nau-danger px-2 py-0.5 font-mono text-[11px] tracking-mono-wide text-nau-danger">
            ! UNVOLLSTÄNDIG
          </div>
        )}
      </div>
      {bubble.proposals && bubble.proposals.length > 0 && (
        <div className="mt-4">
          <SlotSuggestions
            slots={bubble.proposals}
            onPick={onPickSlot}
            disabled={pickDisabled}
          />
        </div>
      )}
    </div>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export { formatSlot };
