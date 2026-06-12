-- User-scoped Settings (zunächst: calendar.*). Lesen läuft mit Fallback auf
-- app_settings → bestehende globale Werte wirken als Seed/Default, bis ein User
-- eigene Werte speichert. Schreiben geht ausschließlich hierhin.
CREATE TABLE user_settings (
    user_id  TEXT NOT NULL,
    key      TEXT NOT NULL,
    value    TEXT NOT NULL,
    PRIMARY KEY (user_id, key)
);
