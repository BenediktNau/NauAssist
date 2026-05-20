import { useCallback, useEffect, useRef, useState } from "react";
import { getHistory } from "@/api/client";
import { sendMessage } from "@/api/chatStream";
import type { MessageDto, SlotInfo } from "@/api/types";

export interface ChatBubble {
  /**
   * Server-ID, sobald bekannt (nach `done`). Bis dahin temporäre Negativ-IDs.
   */
  id: number;
  role: "user" | "assistant";
  content: string;
  proposals: SlotInfo[] | null;
  incomplete: boolean;
  /** true, solange die Antwort live reinkommt. */
  streaming: boolean;
}

export interface ToolStatus {
  name: string;
  state: "started" | "finished";
  ok?: boolean;
}

export interface ChatState {
  bubbles: ChatBubble[];
  toolStatus: ToolStatus | null;
  error: string | null;
  sending: boolean;
}

const TEMP_ID_OFFSET = -1; // temporäre IDs sind negativ, dann gibt es keine Kollision mit DB-IDs
let nextTempId = TEMP_ID_OFFSET;
const freshTempId = () => nextTempId--;

function parseProposals(json: string | null): SlotInfo[] | null {
  if (!json) return null;
  try {
    return JSON.parse(json) as SlotInfo[];
  } catch {
    return null;
  }
}

function mapHistory(msgs: MessageDto[]): ChatBubble[] {
  return msgs.map((m) => ({
    id: m.id,
    role: m.role,
    content: m.content,
    proposals: parseProposals(m.proposalsJson),
    incomplete: m.incomplete,
    streaming: false,
  }));
}

export function useChat(): ChatState & {
  send: (text: string) => void;
} {
  const [bubbles, setBubbles] = useState<ChatBubble[]>([]);
  const [toolStatus, setToolStatus] = useState<ToolStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  const abortRef = useRef<AbortController | null>(null);

  // Initial-History laden
  useEffect(() => {
    let cancelled = false;
    getHistory()
      .then((dto) => {
        if (!cancelled) setBubbles(mapHistory(dto.messages));
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "History-Load fehlgeschlagen");
      });
    return () => {
      cancelled = true;
      abortRef.current?.abort();
    };
  }, []);

  const send = useCallback(
    (text: string) => {
      if (sending || !text.trim()) return;

      const userBubble: ChatBubble = {
        id: freshTempId(),
        role: "user",
        content: text,
        proposals: null,
        incomplete: false,
        streaming: false,
      };
      const agentBubble: ChatBubble = {
        id: freshTempId(),
        role: "assistant",
        content: "",
        proposals: null,
        incomplete: false,
        streaming: true,
      };
      const agentTempId = agentBubble.id;

      setBubbles((prev) => [...prev, userBubble, agentBubble]);
      setError(null);
      setToolStatus(null);
      setSending(true);

      const ctrl = new AbortController();
      abortRef.current = ctrl;

      sendMessage({
        message: text,
        signal: ctrl.signal,
        onEvent: (ev) => {
          switch (ev.event) {
            case "token":
              setBubbles((prev) =>
                prev.map((b) =>
                  b.id === agentTempId ? { ...b, content: b.content + ev.data.text } : b,
                ),
              );
              break;
            case "tool_started":
              setToolStatus({ name: ev.data.name, state: "started" });
              break;
            case "tool_finished":
              setToolStatus({ name: ev.data.name, state: "finished", ok: ev.data.ok });
              break;
            case "proposals":
              setBubbles((prev) =>
                prev.map((b) => (b.id === agentTempId ? { ...b, proposals: ev.data } : b)),
              );
              break;
            case "done":
              setBubbles((prev) =>
                prev.map((b) =>
                  b.id === agentTempId ? { ...b, id: ev.data.messageId, streaming: false } : b,
                ),
              );
              setToolStatus(null);
              break;
            case "error":
              setError(ev.data.message);
              setBubbles((prev) =>
                prev.map((b) =>
                  b.id === agentTempId ? { ...b, streaming: false, incomplete: true } : b,
                ),
              );
              break;
          }
        },
      })
        .catch((e: unknown) => {
          setError(e instanceof Error ? e.message : "Stream-Fehler");
          setBubbles((prev) =>
            prev.map((b) =>
              b.id === agentTempId ? { ...b, streaming: false, incomplete: true } : b,
            ),
          );
        })
        .finally(() => {
          setSending(false);
          abortRef.current = null;
        });
    },
    [sending],
  );

  return { bubbles, toolStatus, error, sending, send };
}
