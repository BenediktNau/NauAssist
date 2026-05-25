import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { clearSession, getHistory } from "@/api/client";
import { sendMessage } from "@/api/chatStream";
import type { ClearMarkerDto, MessageDto, SlotInfo } from "@/api/types";

export interface MessageBubble {
  kind: "message";
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
  /** ISO-8601 Timestamp der Nachricht (server-seitig bei History, sonst Client-Zeit). */
  createdAt: string;
}

export interface ClearMarkerBubble {
  kind: "clear-marker";
  id: number;
  createdAt: string;
}

export type ChatBubble = MessageBubble | ClearMarkerBubble;

export function isMessageBubble(b: ChatBubble): b is MessageBubble {
  return b.kind === "message";
}

export interface ToolStatus {
  name: string;
  state: "started" | "finished";
  ok?: boolean;
}

export interface ActiveProposals {
  /** ID der Assistant-Bubble, deren Proposals das sind — wechselt nur bei neuen Vorschlägen. */
  messageId: number;
  slots: SlotInfo[];
}

export interface ChatState {
  bubbles: ChatBubble[];
  toolStatus: ToolStatus | null;
  error: string | null;
  sending: boolean;
  activeProposals: ActiveProposals | null;
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

const DAY_START_HOUR = 5;

/**
 * Jüngste vergangene 5-Uhr-Marke in der Browser-Lokalzeit.
 * Vor 5 Uhr morgens zählt der Vortag noch zum aktuellen "Chat-Tag".
 */
function currentDayStart(now: Date): Date {
  const cutoff = new Date(now);
  cutoff.setHours(DAY_START_HOUR, 0, 0, 0);
  if (now.getTime() < cutoff.getTime()) {
    cutoff.setDate(cutoff.getDate() - 1);
  }
  return cutoff;
}

const CLEAR_MARKER_ID_BIAS = 1_000_000; // Marker-IDs werden offset, um Kollision mit message.id zu vermeiden

function mapHistory(msgs: MessageDto[], markers: ClearMarkerDto[]): ChatBubble[] {
  const cutoff = currentDayStart(new Date()).getTime();

  const messageBubbles: ChatBubble[] = msgs
    .filter((m) => new Date(m.createdAt).getTime() >= cutoff)
    .map((m): MessageBubble => ({
      kind: "message",
      id: m.id,
      role: m.role,
      content: m.content,
      proposals: parseProposals(m.proposalsJson),
      incomplete: m.incomplete,
      streaming: false,
      createdAt: m.createdAt,
    }));

  const markerBubbles: ChatBubble[] = markers
    .filter((m) => new Date(m.createdAt).getTime() >= cutoff)
    .map((m): ClearMarkerBubble => ({
      kind: "clear-marker",
      id: m.id + CLEAR_MARKER_ID_BIAS,
      createdAt: m.createdAt,
    }));

  return [...messageBubbles, ...markerBubbles].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
  );
}

export function useChat(): ChatState & {
  send: (text: string) => void;
} {
  const [bubbles, setBubbles] = useState<ChatBubble[]>([]);
  const [toolStatus, setToolStatus] = useState<ToolStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  const abortRef = useRef<AbortController | null>(null);

  const reloadHistory = useCallback(async () => {
    const dto = await getHistory();
    setBubbles(mapHistory(dto.messages, dto.markers));
  }, []);

  // Initial-History laden
  useEffect(() => {
    let cancelled = false;
    getHistory()
      .then((dto) => {
        if (!cancelled) setBubbles(mapHistory(dto.messages, dto.markers));
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

      const trimmed = text.trim();

      if (trimmed === "/clear") {
        setError(null);
        setSending(true);
        clearSession()
          .then(() => reloadHistory())
          .catch((e: unknown) => {
            setError(e instanceof Error ? e.message : "Clear fehlgeschlagen");
          })
          .finally(() => setSending(false));
        return;
      }

      const nowIso = new Date().toISOString();
      const userBubble: MessageBubble = {
        kind: "message",
        id: freshTempId(),
        role: "user",
        content: text,
        proposals: null,
        incomplete: false,
        streaming: false,
        createdAt: nowIso,
      };
      const agentBubble: MessageBubble = {
        kind: "message",
        id: freshTempId(),
        role: "assistant",
        content: "",
        proposals: null,
        incomplete: false,
        streaming: true,
        createdAt: nowIso,
      };
      const agentTempId = agentBubble.id;

      setBubbles((prev) => [...prev, userBubble, agentBubble]);
      setError(null);
      setToolStatus(null);
      setSending(true);

      const ctrl = new AbortController();
      abortRef.current = ctrl;

      let doneSeen = false;

      sendMessage({
        message: text,
        signal: ctrl.signal,
        onEvent: (ev) => {
          switch (ev.event) {
            case "token":
              setBubbles((prev) =>
                prev.map((b) =>
                  isMessageBubble(b) && b.id === agentTempId
                    ? { ...b, content: b.content + ev.data.text }
                    : b,
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
                prev.map((b) =>
                  isMessageBubble(b) && b.id === agentTempId
                    ? { ...b, proposals: ev.data }
                    : b,
                ),
              );
              break;
            case "done":
              doneSeen = true;
              setBubbles((prev) =>
                prev.map((b) =>
                  isMessageBubble(b) && b.id === agentTempId
                    ? { ...b, id: ev.data.messageId, streaming: false }
                    : b,
                ),
              );
              setToolStatus(null);
              break;
            case "error":
              setError(ev.data.message);
              setBubbles((prev) =>
                prev.map((b) =>
                  isMessageBubble(b) && b.id === agentTempId
                    ? { ...b, streaming: false, incomplete: true }
                    : b,
                ),
              );
              break;
          }
        },
      })
        .then(() => {
          if (!doneSeen) {
            setError("Verbindung wurde unerwartet beendet.");
            setBubbles((prev) =>
              prev.map((b) =>
                isMessageBubble(b) && b.id === agentTempId
                  ? { ...b, streaming: false, incomplete: true }
                  : b,
              ),
            );
          }
        })
        .catch((e: unknown) => {
          setError(e instanceof Error ? e.message : "Stream-Fehler");
          setBubbles((prev) =>
            prev.map((b) =>
              isMessageBubble(b) && b.id === agentTempId
                ? { ...b, streaming: false, incomplete: true }
                : b,
            ),
          );
        })
        .finally(() => {
          setSending(false);
          setToolStatus(null);
          abortRef.current = null;
        });
    },
    [sending, reloadHistory],
  );

  const activeProposals = useMemo<ActiveProposals | null>(() => {
    for (let i = bubbles.length - 1; i >= 0; i--) {
      const b = bubbles[i];
      if (b.kind === "clear-marker") return null;
      if (!isMessageBubble(b)) continue;
      if (b.role === "assistant" && b.proposals && b.proposals.length > 0) {
        return { messageId: b.id, slots: b.proposals };
      }
    }
    return null;
  }, [bubbles]);

  return { bubbles, toolStatus, error, sending, send, activeProposals };
}
