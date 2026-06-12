-- Multi-User (Keycloak): users-Tabelle + user_id auf allen per-User-Tabellen.
-- Bestehende Daten wandern über den DEFAULT lückenlos auf den Default-/Owner-User.
-- Global bleiben: rules, app_settings, Persona-Tabellen.
CREATE TABLE users (
    id            TEXT PRIMARY KEY,   -- Keycloak sub; Default-User: 'nauassist-default'
    username      TEXT NULL,
    email         TEXT NULL,
    created_at    TEXT NOT NULL,
    last_seen_at  TEXT NULL
);

INSERT INTO users(id, username, created_at)
VALUES ('nauassist-default', 'default', STRFTIME('%Y-%m-%dT%H:%M:%fZ', 'now'));

ALTER TABLE messages           ADD COLUMN user_id TEXT NOT NULL DEFAULT 'nauassist-default';
ALTER TABLE chat_clear_markers ADD COLUMN user_id TEXT NOT NULL DEFAULT 'nauassist-default';
ALTER TABLE audit_log          ADD COLUMN user_id TEXT NOT NULL DEFAULT 'nauassist-default';
ALTER TABLE suggestions        ADD COLUMN user_id TEXT NOT NULL DEFAULT 'nauassist-default';
ALTER TABLE source_accounts    ADD COLUMN user_id TEXT NOT NULL DEFAULT 'nauassist-default';
ALTER TABLE web_push_subscriptions ADD COLUMN user_id TEXT NOT NULL DEFAULT 'nauassist-default';

-- Indizes um user_id erweitern (DROP/CREATE, SQLite kennt kein ALTER INDEX).
DROP INDEX idx_messages_session_created;
CREATE INDEX idx_messages_session_created ON messages(user_id, session_id, created_at);

DROP INDEX idx_clear_markers_session_created;
CREATE INDEX idx_clear_markers_session_created ON chat_clear_markers(user_id, session_id, created_at);

DROP INDEX idx_audit_created;
CREATE INDEX idx_audit_created ON audit_log(user_id, created_at);

DROP INDEX idx_suggestions_status_created;
CREATE INDEX idx_suggestions_status_created ON suggestions(user_id, status, created_at DESC);

DROP INDEX idx_suggestions_source_ref;
CREATE INDEX idx_suggestions_source_ref ON suggestions(user_id, source, source_ref);

DROP INDEX idx_source_accounts_kind;
CREATE INDEX idx_source_accounts_kind ON source_accounts(user_id, kind, enabled);

CREATE INDEX idx_web_push_user ON web_push_subscriptions(user_id);

-- source_cursors: user_id gehört in den Primärschlüssel → Tabelle neu aufbauen.
CREATE TABLE source_cursors_new (
    user_id     TEXT NOT NULL DEFAULT 'nauassist-default',
    source      TEXT NOT NULL,
    account_id  INTEGER NULL,
    cursor      TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    PRIMARY KEY (user_id, source, account_id)
);

INSERT INTO source_cursors_new(user_id, source, account_id, cursor, updated_at)
SELECT 'nauassist-default', source, account_id, cursor, updated_at FROM source_cursors;

DROP TABLE source_cursors;
ALTER TABLE source_cursors_new RENAME TO source_cursors;
