// Spiegelt die Backend-DTOs aus ChatEndpoints.cs und RulesEndpoints.cs.

export interface SlotInfo {
  start: string; // ISO-8601 mit Offset
  end: string;
  note: string | null;
}

export type MessageRole = "user" | "assistant";

export interface MessageDto {
  id: number;
  sessionId: string;
  role: MessageRole;
  content: string;
  proposalsJson: string | null;
  incomplete: boolean;
  createdAt: string;
}

export interface ClearMarkerDto {
  id: number;
  createdAt: string;
}

export interface ChatHistoryDto {
  messages: MessageDto[];
  markers: ClearMarkerDto[];
}

export interface RuleDto {
  id: number;
  text: string;
  daysOfWeek: number;
  timeRangeStart: string | null;
  timeRangeEnd: string | null;
  hardness: "hard" | "soft";
  createdAt: string;
}

// SSE-Event-Union (Wire-Format laut SseWriter.cs)

export type SseEventName =
  | "token"
  | "tool_started"
  | "tool_finished"
  | "proposals"
  | "done"
  | "error";

export type SseEventData =
  | { event: "token"; data: { text: string } }
  | { event: "tool_started"; data: { name: string } }
  | { event: "tool_finished"; data: { name: string; ok: boolean } }
  | { event: "proposals"; data: SlotInfo[] }
  | { event: "done"; data: { messageId: number } }
  | { event: "error"; data: { message: string; correlationId: string | null } };
