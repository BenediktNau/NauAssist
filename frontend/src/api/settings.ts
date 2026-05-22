export interface LlmSettings {
  provider: "ollama" | "gemini";
  ollamaModel: string;
  geminiModel: string;
  hasGeminiApiKey: boolean;
}

export interface UpdateLlmSettingsPayload {
  provider: "ollama" | "gemini";
  ollamaModel: string;
  geminiModel: string;
  geminiApiKey: string | null;
}

export const OLLAMA_MODELS = [
  "gemma4:26b",
  "qwen3.5:4b",
  "qwen2.5:7b-instruct",
  "llama3.2:3b",
] as const;

export const GEMINI_MODELS = [
  "gemini-2.5-flash",
  "gemini-2.5-pro",
  "gemma-4-31b-it",
] as const;

export async function getLlmSettings(): Promise<LlmSettings> {
  const res = await fetch("/api/settings/llm");
  if (!res.ok) throw new Error(`GET /api/settings/llm failed: ${res.status}`);
  return res.json();
}

export async function updateLlmSettings(
  payload: UpdateLlmSettingsPayload,
): Promise<void> {
  const res = await fetch("/api/settings/llm", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `PUT /api/settings/llm failed: ${res.status}`);
  }
}
