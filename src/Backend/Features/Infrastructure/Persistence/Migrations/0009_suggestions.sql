-- Autonomer Agent: Empfehlungen aus externen Quellen (Matrix, Gmail, ...).
-- Eine Suggestion entspricht einer erkannten Termin-Anfrage. Slots + Antwort-Entwurf
-- werden als JSON in der Zeile gehalten (konsistent mit messages.proposals_json).
CREATE TABLE suggestions (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    source        TEXT NOT NULL,                       -- 'matrix' | 'gmail' | ...
    source_ref    TEXT NOT NULL,                       -- z.B. "!room:server/$event_id"
    intent        TEXT NOT NULL,                       -- 'schedule_request' (erweiterbar)
    topic         TEXT NULL,                           -- "Volleyball nächste Woche"
    requester     TEXT NULL,                           -- "Lukas"
    quoted_text   TEXT NULL,                           -- Originaltext-Schnipsel
    slots_json    TEXT NOT NULL,                       -- [{"start":"…","end":"…","note":"…"}, …]
    draft_reply   TEXT NOT NULL DEFAULT '',            -- vorformulierter Antworttext
    status        TEXT NOT NULL,                       -- 'pending' | 'responded' | 'dismissed'
    picked_slot   INTEGER NULL,                        -- 0..n nach Auswahl
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL,
    responded_at  TEXT NULL
);

CREATE INDEX idx_suggestions_status_created ON suggestions(status, created_at DESC);
CREATE INDEX idx_suggestions_source_ref ON suggestions(source, source_ref);

-- Cursor pro (Source-Account, Source-Typ) für inkrementelles Polling.
-- account_id ist NULL für Singleton-Quellen ohne Account-Konzept; ansonsten
-- referenziert es eine spätere source_accounts.id (Migration 0010+).
CREATE TABLE source_cursors (
    source      TEXT NOT NULL,
    account_id  INTEGER NULL,
    cursor      TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    PRIMARY KEY (source, account_id)
);
