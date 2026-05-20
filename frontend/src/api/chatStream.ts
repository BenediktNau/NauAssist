import { fetchEventSource } from "@microsoft/fetch-event-source";
import type { SseEventData, SseEventName } from "./types";

export type ChatStreamEvent = SseEventData;

export interface SendMessageOptions {
  message: string;
  onEvent: (ev: ChatStreamEvent) => void;
  signal: AbortSignal;
}

const KNOWN_EVENTS: ReadonlySet<SseEventName> = new Set([
  "token",
  "tool_started",
  "tool_finished",
  "proposals",
  "done",
  "error",
]);

function isKnownEvent(name: string): name is SseEventName {
  return KNOWN_EVENTS.has(name as SseEventName);
}

export async function sendMessage({ message, onEvent, signal }: SendMessageOptions): Promise<void> {
  await fetchEventSource("/api/chat", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ message }),
    signal,
    openWhenHidden: true, // kein Auto-Pause im Hintergrund-Tab
    async onopen(response) {
      if (!response.ok || !response.headers.get("content-type")?.includes("text/event-stream")) {
        throw new Error(`SSE-Open fehlgeschlagen: HTTP ${response.status}`);
      }
    },
    onmessage(msg) {
      if (!isKnownEvent(msg.event)) {
        return; // unbekannte Events schlucken statt zu crashen
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(msg.data);
      } catch {
        return;
      }
      // Type-narrowing pro Event-Name
      onEvent({ event: msg.event, data: parsed } as ChatStreamEvent);
    },
    onerror(err) {
      // Werfen → fetch-event-source beendet den Stream und propagiert nach außen.
      throw err;
    },
  });
}
