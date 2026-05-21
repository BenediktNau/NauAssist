import type { ChatHistoryDto, ClearMarkerDto, RuleDto } from "./types";

export const HEADERS_JSON = { "Content-Type": "application/json" } as const; // wird in chatStream.ts genutzt

export async function getHistory(): Promise<ChatHistoryDto> {
  const res = await fetch("/api/chat/history");
  if (!res.ok) {
    throw new Error(`History-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as ChatHistoryDto;
}

export async function clearSession(): Promise<ClearMarkerDto> {
  const res = await fetch("/api/chat/clear", { method: "POST" });
  if (!res.ok) {
    throw new Error(`Clear fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as ClearMarkerDto;
}

export async function listRules(): Promise<RuleDto[]> {
  const res = await fetch("/api/rules/");
  if (!res.ok) {
    throw new Error(`Rules-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as RuleDto[];
}

export async function deleteRule(id: number): Promise<void> {
  const res = await fetch(`/api/rules/${id}`, { method: "DELETE" });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Rule-Delete fehlgeschlagen: HTTP ${res.status}`);
  }
}
