DELETE FROM app_settings
WHERE key IN ('llm.gemini.model', 'llm.gemini.api_key', 'llm.provider');
