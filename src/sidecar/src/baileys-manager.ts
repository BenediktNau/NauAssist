import makeWASocket, {
  DisconnectReason,
  fetchLatestBaileysVersion,
  useMultiFileAuthState,
} from "@whiskeysockets/baileys";
import { randomUUID } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import QRCode from "qrcode";
import type { Logger } from "pino";
import type { MessageBuffer } from "./buffer";

export type SessionState = "pairing" | "connected" | "loggedOut" | "disconnected";

interface Session {
  id: string;
  sock?: ReturnType<typeof makeWASocket>;
  state: SessionState;
  qr?: string; // Data-URL (PNG)
  phone?: string;
  chats: Map<string, string>; // chatId (JID) -> Anzeigename
  starting: boolean;
}

const RECONNECT_DELAY_MS = 2000;

/**
 * Verwaltet pro `sessionId` eine persistente WhatsApp-Web-Verbindung (Baileys).
 * Auth-State liegt je Session unter <dataDir>/sessions/<id> und überlebt Restarts.
 * Eingehende Text-Nachrichten (nur Live, type="notify") landen im MessageBuffer.
 */
export class BaileysManager {
  private sessions = new Map<string, Session>();

  constructor(
    private dataDir: string,
    private buffer: MessageBuffer,
    private logger: Logger,
  ) {}

  private sessionsDir(): string {
    return path.join(this.dataDir, "sessions");
  }

  private authDir(id: string): string {
    return path.join(this.sessionsDir(), id);
  }

  /** Beim Start: alle vorhandenen Session-Verzeichnisse reconnecten. */
  async restoreAll(): Promise<void> {
    const dir = this.sessionsDir();
    if (!fs.existsSync(dir)) return;
    for (const id of fs.readdirSync(dir)) {
      if (!fs.statSync(path.join(dir, id)).isDirectory()) continue;
      this.logger.info({ id }, "restoring session");
      try {
        await this.startSession(id);
      } catch (e) {
        this.logger.error({ id, err: e }, "restore failed");
      }
    }
  }

  async createSession(id?: string): Promise<{ sessionId: string; state: SessionState }> {
    const sessionId = id ?? randomUUID();
    const existing = this.sessions.get(sessionId);
    if (existing) {
      return { sessionId, state: existing.state };
    }
    await this.startSession(sessionId);
    return { sessionId, state: this.sessions.get(sessionId)!.state };
  }

  private async startSession(id: string): Promise<void> {
    let session = this.sessions.get(id);
    if (!session) {
      session = { id, state: "disconnected", chats: new Map(), starting: false };
      this.sessions.set(id, session);
    }
    for (const c of this.buffer.listChats(id)) {
      if (!session.chats.has(c.chatId)) session.chats.set(c.chatId, c.name);
    }
    if (session.starting) return;
    session.starting = true;

    fs.mkdirSync(this.authDir(id), { recursive: true });
    const { state, saveCreds } = await useMultiFileAuthState(this.authDir(id));
    const { version } = await fetchLatestBaileysVersion();

    const sock = makeWASocket({
      version,
      auth: state,
      printQRInTerminal: false,
      logger: this.logger.child({ session: id }) as never,
      browser: ["NauAssist", "Chrome", "1.0.0"],
      syncFullHistory: false,
      markOnlineOnConnect: false,
    });
    session.sock = sock;
    session.starting = false;

    sock.ev.on("creds.update", saveCreds);

    sock.ev.on("connection.update", async (update) => {
      const { connection, lastDisconnect, qr } = update;
      const s = this.sessions.get(id);
      if (!s) return;

      if (qr) {
        s.state = "pairing";
        try {
          s.qr = await QRCode.toDataURL(qr);
        } catch (e) {
          this.logger.warn({ id, err: e }, "qr encode failed");
        }
      }

      if (connection === "open") {
        s.state = "connected";
        s.qr = undefined;
        s.phone = sock.user?.id;
        this.logger.info({ id, phone: s.phone }, "session connected");
        void this.refreshGroups(id);
      } else if (connection === "close") {
        const code = (lastDisconnect?.error as { output?: { statusCode?: number } } | undefined)
          ?.output?.statusCode;
        if (code === DisconnectReason.loggedOut) {
          s.state = "loggedOut";
          s.sock = undefined;
          this.logger.warn({ id }, "logged out — QR re-pairing required");
        } else {
          s.state = "disconnected";
          this.logger.info({ id, code }, "connection closed, reconnecting");
          setTimeout(() => {
            this.startSession(id).catch((e) =>
              this.logger.error({ id, err: e }, "reconnect failed"),
            );
          }, RECONNECT_DELAY_MS);
        }
      }
    });

    // Chat-Namen aus diversen Quellen sammeln (für die Allowlist-Auswahl im UI).
    const rememberChats = (chats: Array<{ id?: string | null; name?: string | null }>) => {
      const s = this.sessions.get(id);
      if (!s) return;
      for (const c of chats) {
        if (c.id) {
          const name = c.name ?? c.id;
          s.chats.set(c.id, name);
          this.buffer.upsertChat(id, c.id, name);
        }
      }
    };
    sock.ev.on("chats.upsert", rememberChats as never);
    sock.ev.on("messaging-history.set", ((arg: { chats?: Array<{ id?: string | null; name?: string | null }> }) =>
      rememberChats(arg.chats ?? [])) as never);
    sock.ev.on("contacts.upsert", ((contacts: Array<{ id?: string | null; name?: string | null; notify?: string | null }>) => {
      const s = this.sessions.get(id);
      if (!s) return;
      for (const c of contacts) {
        if (c.id) {
          const name = c.name ?? c.notify ?? c.id;
          s.chats.set(c.id, name);
          this.buffer.upsertChat(id, c.id, name);
        }
      }
    }) as never);

    sock.ev.on("messages.upsert", (payload) => {
      if (payload.type !== "notify") return; // nur Live-Nachrichten puffern, keine History
      const s = this.sessions.get(id);
      if (!s) return;

      for (const m of payload.messages) {
        const text = m.message?.conversation ?? m.message?.extendedTextMessage?.text ?? "";
        const chatId = m.key.remoteJid ?? "";
        if (!text || !chatId || chatId === "status@broadcast") continue;

        // pushName direkter 1:1-Chats als Anzeigename merken.
        if (m.pushName && chatId.endsWith("@s.whatsapp.net")) {
          s.chats.set(chatId, m.pushName);
          this.buffer.upsertChat(id, chatId, m.pushName);
        }

        this.buffer.insert({
          sessionId: id,
          msgId: m.key.id ?? "",
          chatId,
          chatName: s.chats.get(chatId) ?? m.pushName ?? null,
          sender: m.key.participant ?? m.key.remoteJid ?? null,
          senderName: m.pushName ?? null,
          text,
          ts: toMillis(m.messageTimestamp),
          fromMe: !!m.key.fromMe,
        });
      }
    });
  }

  getStatus(id: string): { state: SessionState; qr?: string; phone?: string } | null {
    const s = this.sessions.get(id);
    if (!s) return null;
    return { state: s.state, qr: s.qr, phone: s.phone };
  }

  /** Lädt alle teilnehmenden Gruppen aktiv (ohne auf Live-Events zu warten). */
  private async refreshGroups(id: string): Promise<void> {
    const s = this.sessions.get(id);
    if (!s?.sock || s.state !== "connected") return;
    try {
      const groups = await s.sock.groupFetchAllParticipating();
      for (const [jid, meta] of Object.entries(groups)) {
        const name = meta.subject || jid;
        s.chats.set(jid, name);
        this.buffer.upsertChat(id, jid, name);
      }
      this.logger.info({ id, count: Object.keys(groups).length }, "groups refreshed");
    } catch (e) {
      this.logger.warn({ id, err: e }, "group refresh failed");
    }
  }

  async listChats(id: string): Promise<Array<{ chatId: string; name: string }> | null> {
    const s = this.sessions.get(id);
    if (!s) return null;
    await this.refreshGroups(id);
    return [...s.chats.entries()]
      .filter(([chatId]) => chatId !== "status@broadcast")
      .map(([chatId, name]) => ({ chatId, name }));
  }

  async sendMessage(id: string, chatId: string, text: string): Promise<boolean> {
    const s = this.sessions.get(id);
    if (!s?.sock || s.state !== "connected") return false;
    await s.sock.sendMessage(chatId, { text });
    return true;
  }

  /** Validiert eine Telefonnummer per onWhatsApp und liefert kanonische JID + LID. */
  async resolveChat(
    id: string,
    phone: string,
  ): Promise<{ chatId: string; lid: string | null; exists: boolean } | null> {
    const s = this.sessions.get(id);
    if (!s?.sock || s.state !== "connected") return null;
    const digits = phone.replace(/\D/g, "");
    if (!digits) return { chatId: "", lid: null, exists: false };

    const res = await s.sock.onWhatsApp(digits);
    const hit = res?.[0];
    if (!hit?.exists) return { chatId: "", lid: null, exists: false };

    const chatId = hit.jid;
    // Baileys 7.0: onWhatsApp liefert kein `lid` mehr — die LID↔PN-Zuordnung lebt im
    // internen LIDMappingStore. Von dort holen (kann null sein, solange noch unbekannt).
    // Optional-Chaining schützt vor noch nicht initialisiertem signalRepository/lidMapping
    // (interne Struktur, kein stabiler Vertrag): kein synchroner Throw, sondern null.
    const lid =
      (await s.sock.signalRepository?.lidMapping
        ?.getLIDForPN(chatId)
        .catch((e) => {
          this.logger.warn({ id, chatId, err: e }, "LID lookup failed");
          return null;
        })) ?? null;
    // Sofort in der Auswahlliste sichtbar machen (Name = Nummer als Fallback).
    if (!s.chats.has(chatId)) {
      s.chats.set(chatId, digits);
      this.buffer.upsertChat(id, chatId, digits);
    }
    return { chatId, lid, exists: true };
  }

  async deleteSession(id: string): Promise<boolean> {
    const s = this.sessions.get(id);
    if (s?.sock) {
      try {
        await s.sock.logout();
      } catch {
        /* schon getrennt — egal */
      }
      try {
        s.sock.end(undefined);
      } catch {
        /* egal */
      }
    }
    this.sessions.delete(id);
    fs.rmSync(this.authDir(id), { recursive: true, force: true });
    return true;
  }
}

function toMillis(ts: number | { toNumber?: () => number } | null | undefined): number {
  if (ts == null) return Date.now();
  if (typeof ts === "number") return ts * 1000;
  if (typeof ts.toNumber === "function") return ts.toNumber() * 1000;
  return Date.now();
}
