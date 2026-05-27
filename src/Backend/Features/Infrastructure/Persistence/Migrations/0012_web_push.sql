-- Web-Push-Subscriptions für PWA-Benachrichtigungen.
-- Public-/Private-VAPID-Key liegen in app_settings (push.vapid_public_key / push.vapid_private_key)
-- und werden beim ersten Start auto-generiert.
CREATE TABLE web_push_subscriptions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    endpoint    TEXT NOT NULL UNIQUE,
    p256dh      TEXT NOT NULL,
    auth        TEXT NOT NULL,
    user_agent  TEXT NULL,
    created_at  TEXT NOT NULL,
    last_used   TEXT NULL
);

INSERT OR IGNORE INTO app_settings (key, value) VALUES
    ('push.vapid_public_key', ''),
    ('push.vapid_private_key', ''),
    ('push.vapid_subject', 'mailto:agent@nauassist.local');
