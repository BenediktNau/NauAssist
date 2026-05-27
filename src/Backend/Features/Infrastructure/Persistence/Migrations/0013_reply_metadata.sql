-- Reply-Metadaten (Message-ID, From, Subject, Refs) für Mail-Antworten.
-- Optional, befüllt durch Source-spezifische Observer (IMAP), gelesen von Sendern (SMTP).
-- Matrix lässt das Feld NULL und nutzt source_ref direkt.
ALTER TABLE suggestions ADD COLUMN reply_metadata_json TEXT NULL;
