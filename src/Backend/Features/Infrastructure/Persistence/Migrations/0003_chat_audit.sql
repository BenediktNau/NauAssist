CREATE TABLE messages (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      TEXT NOT NULL,
    role            TEXT NOT NULL,
    content         TEXT NOT NULL,
    proposals_json  TEXT NULL,
    incomplete      INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL
);

CREATE INDEX idx_messages_session_created ON messages(session_id, created_at);

CREATE TABLE audit_log (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    triggering_message_id   INTEGER NULL,
    tool_name               TEXT NOT NULL,
    tool_args_json          TEXT NOT NULL,
    result_json             TEXT NOT NULL,
    provider_event_id       TEXT NULL,
    created_at              TEXT NOT NULL,
    FOREIGN KEY (triggering_message_id) REFERENCES messages(id)
);

CREATE INDEX idx_audit_created ON audit_log(created_at);
