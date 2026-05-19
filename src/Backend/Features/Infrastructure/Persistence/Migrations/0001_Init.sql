CREATE TABLE schema_version (
    version     TEXT PRIMARY KEY,
    applied_at  TEXT NOT NULL
);

CREATE TABLE rules (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    text            TEXT NOT NULL,
    days_of_week    INTEGER NOT NULL,
    time_range_start TEXT NULL,
    time_range_end   TEXT NULL,
    hardness        TEXT NOT NULL,
    created_at      TEXT NOT NULL
);

CREATE INDEX idx_rules_created_at ON rules(created_at);
