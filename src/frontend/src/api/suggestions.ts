export interface SuggestionSlotDto {
  start: string;
  end: string;
  note: string | null;
}

export type SuggestionStatus = "pending" | "responded" | "dismissed";

export interface SuggestionDto {
  id: number;
  source: string;
  sourceRef: string;
  intent: string;
  topic: string | null;
  requester: string | null;
  quotedText: string | null;
  slots: SuggestionSlotDto[];
  draftReply: string;
  status: SuggestionStatus;
  pickedSlot: number | null;
  createdAt: string;
  updatedAt: string;
  respondedAt: string | null;
}

export interface PollResult {
  skipped: boolean;
  signalCount: number;
  expiredCount: number;
  errorCount: number;
}

export async function listSuggestions(
  status?: SuggestionStatus,
): Promise<SuggestionDto[]> {
  const url = status ? `/api/suggestions/?status=${status}` : "/api/suggestions/";
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`Suggestions-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SuggestionDto[];
}

export async function pickSuggestionSlot(
  id: number,
  slotIndex: number,
): Promise<SuggestionDto> {
  const res = await fetch(`/api/suggestions/${id}/pick`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ slotIndex }),
  });
  if (!res.ok) {
    throw new Error(`Pick fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SuggestionDto;
}

export async function dismissSuggestion(id: number): Promise<void> {
  const res = await fetch(`/api/suggestions/${id}/dismiss`, { method: "POST" });
  if (!res.ok) {
    throw new Error(`Dismiss fehlgeschlagen: HTTP ${res.status}`);
  }
}

export async function updateSuggestionDraft(
  id: number,
  text: string,
): Promise<SuggestionDto> {
  const res = await fetch(`/api/suggestions/${id}/draft`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text }),
  });
  if (!res.ok) {
    throw new Error(`Draft-Update fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SuggestionDto;
}

export async function sendSuggestion(
  id: number,
  text: string,
): Promise<SuggestionDto> {
  const res = await fetch(`/api/suggestions/${id}/send`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `Senden fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SuggestionDto;
}

export async function pollSuggestionsNow(): Promise<PollResult> {
  const res = await fetch("/api/suggestions/poll-now", { method: "POST" });
  if (!res.ok) {
    throw new Error(`Poll-Now fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as PollResult;
}
