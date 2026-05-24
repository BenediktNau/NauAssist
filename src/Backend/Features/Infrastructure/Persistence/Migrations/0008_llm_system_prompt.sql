-- System-Prompt ist editierbar (Settings → LLM).
-- Leerer Wert bedeutet: kein eigener Prompt gesetzt, Fallback auf appsettings.Ollama:SystemPrompt.
INSERT OR IGNORE INTO app_settings (key, value) VALUES ('llm.system_prompt', '');
