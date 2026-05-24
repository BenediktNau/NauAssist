export interface LlmSettings {
  ollamaModel: string;
}

export interface UpdateLlmSettingsPayload {
  ollamaModel: string;
}

export const OLLAMA_MODELS = [
  "gemma4:26b",
  "gemma4:e2b",
  "qwen3.5:4b",
  "qwen2.5:7b-instruct",
  "llama3.2:3b",
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

export interface OllamaSettings {
  host: string;
  hasApiKey: boolean;
  numCtx: number;
  temperature: number;
}

export interface UpdateOllamaSettingsPayload {
  host: string;
  apiKey: string | null;
  numCtx: number;
  temperature: number;
}

export async function getOllamaSettings(): Promise<OllamaSettings> {
  const res = await fetch("/api/settings/ollama");
  if (!res.ok) throw new Error(`GET /api/settings/ollama failed: ${res.status}`);
  return res.json();
}

export async function updateOllamaSettings(
  payload: UpdateOllamaSettingsPayload,
): Promise<void> {
  const res = await fetch("/api/settings/ollama", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `PUT /api/settings/ollama failed: ${res.status}`);
  }
}

export interface OllamaTestResult {
  ok: boolean;
  models?: string[] | null;
  error?: string | null;
}

export async function testOllamaConnection(
  host: string,
  apiKey: string | null,
): Promise<OllamaTestResult> {
  const res = await fetch("/api/settings/ollama/test", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ host, apiKey }),
  });
  return res.json();
}
