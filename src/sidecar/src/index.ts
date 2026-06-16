import Fastify from "fastify";
import pino from "pino";
import { BaileysManager } from "./baileys-manager";
import { MessageBuffer } from "./buffer";

const PORT = Number(process.env.PORT ?? 3000);
const DATA_DIR = process.env.DATA_DIR ?? "/data";
const TOKEN = process.env.SIDECAR_TOKEN ?? "";
const MAX_ROWS = Number(process.env.MAX_BUFFER_ROWS ?? 5000);
const RETENTION_DAYS = Number(process.env.RETENTION_DAYS ?? 14);
const MESSAGE_LIMIT_CAP = 1000;

const logger = pino({ level: process.env.LOG_LEVEL ?? "info" });
const buffer = new MessageBuffer(DATA_DIR, {
  maxRows: MAX_ROWS,
  retentionDays: RETENTION_DAYS,
});
const manager = new BaileysManager(DATA_DIR, buffer, logger);

const app = Fastify({ logger: false });

// Bearer-Auth (außer /health). Leeres Token = Auth deaktiviert (nur lokale Entwicklung).
app.addHook("onRequest", async (req, reply) => {
  if (req.url === "/health") return;
  if (!TOKEN) return;
  if (req.headers["authorization"] !== `Bearer ${TOKEN}`) {
    await reply.code(401).send({ error: "unauthorized" });
  }
});

app.get("/health", async () => "ok");

app.post("/sessions", async (req) => {
  const body = (req.body ?? {}) as { sessionId?: string };
  return manager.createSession(body.sessionId);
});

app.get("/sessions/:id", async (req, reply) => {
  const { id } = req.params as { id: string };
  const status = manager.getStatus(id);
  if (!status) return reply.code(404).send({ error: "not_found" });
  return status;
});

app.get("/sessions/:id/chats", async (req, reply) => {
  const { id } = req.params as { id: string };
  const chats = manager.listChats(id);
  if (!chats) return reply.code(404).send({ error: "not_found" });
  return chats;
});

app.get("/sessions/:id/messages", async (req) => {
  const { id } = req.params as { id: string };
  const q = req.query as { since?: string; limit?: string };
  const since = Number(q.since ?? 0) || 0;
  const limit = Math.min(Number(q.limit ?? 200) || 200, MESSAGE_LIMIT_CAP);
  return buffer.getSince(id, since, limit);
});

app.post("/sessions/:id/send", async (req, reply) => {
  const { id } = req.params as { id: string };
  const body = (req.body ?? {}) as { chatId?: string; text?: string };
  if (!body.chatId || !body.text) {
    return reply.code(400).send({ error: "chatId_and_text_required" });
  }
  const ok = await manager.sendMessage(id, body.chatId, body.text);
  if (!ok) return reply.code(409).send({ error: "session_not_connected" });
  return { ok: true };
});

app.delete("/sessions/:id", async (req) => {
  const { id } = req.params as { id: string };
  await manager.deleteSession(id);
  return { ok: true };
});

async function main(): Promise<void> {
  await manager.restoreAll();
  await app.listen({ port: PORT, host: "0.0.0.0" });
  logger.info({ port: PORT, authEnabled: TOKEN !== "" }, "nauassist-wa sidecar listening");
}

main().catch((e) => {
  logger.error(e, "fatal startup error");
  process.exit(1);
});
