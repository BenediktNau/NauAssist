-- Konfigurierbare Accounts pro Quellen-Typ. Nutzer fügt sie über die Settings-UI
-- hinzu (z.B. mehrere Matrix-Accounts oder später mehrere Gmail-Postfächer).
CREATE TABLE source_accounts (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    kind            TEXT NOT NULL,                   -- 'matrix' | 'gmail' | ...
    display_name    TEXT NOT NULL,
    credentials_json TEXT NOT NULL,                  -- {homeserverUrl, userId, accessToken}
    allowlist_json  TEXT NOT NULL DEFAULT '[]',      -- ["!room:server", ...]
    enabled         INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL
);

CREATE INDEX idx_source_accounts_kind ON source_accounts(kind, enabled);
