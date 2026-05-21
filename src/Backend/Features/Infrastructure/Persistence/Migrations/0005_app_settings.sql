CREATE TABLE app_settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT INTO app_settings (key, value) VALUES
    ('llm.provider', 'ollama'),
    ('llm.ollama.model', 'gemma4:26b'),
    ('llm.gemini.model', 'gemini-2.5-flash'),
    ('llm.gemini.api_key', '');
