-- Self-Writing Watch-Jobs (Phase 1): persistente, deklarative Beobachtungs-Aufträge.
-- Ein WatchJob beschreibt "prüfe regelmäßig Ziel X über Skill Y, und wenn Bedingung Z
-- erfüllt ist, benachrichtige über Kanäle K". Spec/Schedule/Notify/Budget liegen als JSON
-- in der Zeile (konsistent mit suggestions.slots_json). Tabelle wird nach 0014_multi_user
-- erstellt, daher user_id direkt in der CREATE-Anweisung.
CREATE TABLE watch_jobs (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id            TEXT    NOT NULL,
    title              TEXT    NOT NULL,
    goal               TEXT    NOT NULL,
    kind               TEXT    NOT NULL,                 -- 'web_availability' (erweiterbar)
    spec_json          TEXT    NOT NULL,                 -- { searchQueries[], targetUrls[], judgeQuestion, successCriteria }
    schedule_json      TEXT    NOT NULL,                 -- { intervalSeconds, maxIntervalSeconds }
    notify_json        TEXT    NOT NULL,                 -- { channels[], fireOnce }
    budget_json        TEXT    NOT NULL,                 -- { maxChecks?, expiresAt? }
    status             TEXT    NOT NULL DEFAULT 'active', -- active|paused|fired|completed|failed|expired
    last_checked_at    TEXT,
    next_due_at        TEXT    NOT NULL,
    check_count        INTEGER NOT NULL DEFAULT 0,
    consecutive_errors INTEGER NOT NULL DEFAULT 0,
    last_result_json   TEXT,
    fired_hash         TEXT,
    created_at         TEXT    NOT NULL
);

CREATE INDEX ix_watch_jobs_due  ON watch_jobs(status, next_due_at);
CREATE INDEX ix_watch_jobs_user ON watch_jobs(user_id, status);
