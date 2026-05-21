CREATE TABLE chat_clear_markers (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT NOT NULL,
    created_at  TEXT NOT NULL
);

CREATE INDEX idx_clear_markers_session_created ON chat_clear_markers(session_id, created_at);
