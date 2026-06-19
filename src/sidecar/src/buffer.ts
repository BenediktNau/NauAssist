import Database from "better-sqlite3";
import fs from "node:fs";
import path from "node:path";

/** Eine im Sidecar gepufferte WhatsApp-Nachricht (Übergabe-Speicher, keine Historie). */
export interface BufferedMessage {
  seq: number;
  sessionId: string;
  msgId: string;
  chatId: string;
  chatName: string | null;
  sender: string | null;
  senderName: string | null;
  text: string;
  ts: number;
  fromMe: boolean;
}

/** Was der Baileys-Manager beim Empfang einliefert. */
export interface IncomingMessage {
  sessionId: string;
  msgId: string;
  chatId: string;
  chatName?: string | null;
  sender?: string | null;
  senderName?: string | null;
  text: string;
  ts: number;
  fromMe: boolean;
}

interface MessageRow {
  seq: number;
  session_id: string;
  msg_id: string;
  chat_id: string;
  chat_name: string | null;
  sender: string | null;
  sender_name: string | null;
  text: string;
  ts: number;
  from_me: number;
}

/**
 * SQLite-Puffer mit global monotoner `seq` als Cursor. Das .NET-Backend pollt
 * `getSince(sessionId, cursor)` im 20-min-Tick — analog zu Matrix-Sync-Token /
 * IMAP-UID. Der Puffer ist bewusst flüchtig (Retention nach Zeit + Zeilenkappung).
 */
export class MessageBuffer {
  private db: Database.Database;
  private maxRows: number;
  private retentionMs: number;

  constructor(dataDir: string, opts?: { maxRows?: number; retentionDays?: number }) {
    fs.mkdirSync(dataDir, { recursive: true });
    this.db = new Database(path.join(dataDir, "buffer.db"));
    this.db.pragma("journal_mode = WAL");
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS messages (
        seq         INTEGER PRIMARY KEY AUTOINCREMENT,
        session_id  TEXT NOT NULL,
        msg_id      TEXT NOT NULL,
        chat_id     TEXT NOT NULL,
        chat_name   TEXT,
        sender      TEXT,
        sender_name TEXT,
        text        TEXT NOT NULL,
        ts          INTEGER NOT NULL,
        from_me     INTEGER NOT NULL DEFAULT 0
      );
      CREATE INDEX IF NOT EXISTS idx_messages_session_seq ON messages(session_id, seq);
      CREATE TABLE IF NOT EXISTS chats (
        session_id  TEXT NOT NULL,
        chat_id     TEXT NOT NULL,
        name        TEXT NOT NULL,
        PRIMARY KEY (session_id, chat_id)
      );
    `);
    this.maxRows = opts?.maxRows ?? 5000;
    this.retentionMs = (opts?.retentionDays ?? 14) * 24 * 60 * 60 * 1000;
  }

  insert(m: IncomingMessage): void {
    this.db
      .prepare(
        `INSERT INTO messages
           (session_id, msg_id, chat_id, chat_name, sender, sender_name, text, ts, from_me)
         VALUES
           (@sessionId, @msgId, @chatId, @chatName, @sender, @senderName, @text, @ts, @fromMe)`,
      )
      .run({
        sessionId: m.sessionId,
        msgId: m.msgId,
        chatId: m.chatId,
        chatName: m.chatName ?? null,
        sender: m.sender ?? null,
        senderName: m.senderName ?? null,
        text: m.text,
        ts: m.ts,
        fromMe: m.fromMe ? 1 : 0,
      });
    this.trim();
  }

  /** Nachrichten mit seq > since, aufsteigend, gedeckelt. Cursor = letzte gelieferte seq. */
  getSince(
    sessionId: string,
    since: number,
    limit: number,
  ): { messages: BufferedMessage[]; cursor: number } {
    const rows = this.db
      .prepare(
        `SELECT seq, session_id, msg_id, chat_id, chat_name, sender, sender_name, text, ts, from_me
           FROM messages
          WHERE session_id = ? AND seq > ?
          ORDER BY seq ASC
          LIMIT ?`,
      )
      .all(sessionId, since, limit) as MessageRow[];

    const messages = rows.map(mapRow);
    const cursor = messages.length > 0 ? messages[messages.length - 1].seq : since;
    return { messages, cursor };
  }

  /** Aktueller Höchst-Cursor einer Session (für Initial-Sync-Baseline). */
  maxSeq(sessionId: string): number {
    const row = this.db
      .prepare(`SELECT MAX(seq) AS m FROM messages WHERE session_id = ?`)
      .get(sessionId) as { m: number | null } | undefined;
    return row?.m ?? 0;
  }

  upsertChat(sessionId: string, chatId: string, name: string): void {
    this.db
      .prepare(
        `INSERT INTO chats (session_id, chat_id, name) VALUES (?, ?, ?)
         ON CONFLICT(session_id, chat_id) DO UPDATE SET name = excluded.name`,
      )
      .run(sessionId, chatId, name);
  }

  listChats(sessionId: string): Array<{ chatId: string; name: string }> {
    const rows = this.db
      .prepare(`SELECT chat_id AS chatId, name FROM chats WHERE session_id = ?`)
      .all(sessionId) as Array<{ chatId: string; name: string }>;
    return rows;
  }

  private trim(): void {
    const cutoff = Date.now() - this.retentionMs;
    this.db.prepare(`DELETE FROM messages WHERE ts < ?`).run(cutoff);
    // Nur die neuesten maxRows behalten. OFFSET liefert die seq an Position maxRows
    // von neu→alt; alles <= davon wird gelöscht. Bei <maxRows Zeilen: no-op.
    this.db
      .prepare(
        `DELETE FROM messages
          WHERE seq <= (SELECT seq FROM messages ORDER BY seq DESC LIMIT 1 OFFSET ?)`,
      )
      .run(this.maxRows);
  }
}

function mapRow(r: MessageRow): BufferedMessage {
  return {
    seq: r.seq,
    sessionId: r.session_id,
    msgId: r.msg_id,
    chatId: r.chat_id,
    chatName: r.chat_name,
    sender: r.sender,
    senderName: r.sender_name,
    text: r.text,
    ts: r.ts,
    fromMe: r.from_me !== 0,
  };
}
