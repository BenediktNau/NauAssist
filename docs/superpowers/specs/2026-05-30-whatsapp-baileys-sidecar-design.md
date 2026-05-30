# WhatsApp-Source via Baileys-Sidecar (opt-in)

## Ziel

Der autonome Agent soll WhatsApp als Nachrichten-Quelle nutzen — analog zu Matrix und IMAP, aber ohne deren Konfigurationsaufwand. Statt Matrix (Homeserver, Pantalaimon, Access-Token) genügt ein **einmaliger QR-Scan**. Technisch geschieht das über [Baileys](https://github.com/WhiskeySockets/Baileys) (das Node-Original), das die WhatsApp-Web-Schnittstelle spricht.

WhatsApp ist **durchgängig opt-in**: Wer es nicht will, bekommt keinen zusätzlichen Container, keine Endpoints und keine UI-Section. Ein einziger Schalter aktiviert Container + Backend-Feature + UI gemeinsam.

> **ToS-Hinweis (bewusst akzeptiert):** Baileys ist eine inoffizielle Reverse-Engineering-Lib der WhatsApp-Web-Schnittstelle. Die genutzte Telefonnummer kann von Meta gesperrt werden. Empfehlung an den Nutzer: Zweitnummer verwenden, nicht die private Hauptnummer.

## Scope (schmaler Pfad)

- Node-**Sidecar** (`sidecar/`) mit Baileys: hält die persistente WhatsApp-Session, puffert eingehende Nachrichten, bietet eine schmale HTTP-API (Session/QR/Chats/Messages/Send).
- .NET-**Adapter** unter `Sources/WhatsApp/`: `WhatsAppObserver : ISourceObserver` + `WhatsAppSender : ISourceSender`, strukturell parallel zu `Matrix/*` und `Imap/*`. Spricht den Sidecar per HTTP.
- **Feature-Flag** `AutonomousAgent:WhatsApp:Enabled` gated DI-Registrierung + Endpoints.
- **Capabilities-Endpoint** `GET /api/capabilities`, damit das Frontend die WhatsApp-Section nur zeigt, wenn aktiv.
- **WhatsAppSection.tsx**: QR-Pairing-Flow + Chat-Allowlist (analog zu Räumen/Ordnern).
- **docker-compose** mit Compose-**Profil** `whatsapp` — Sidecar startet nur, wenn das Profil aktiv ist.
- Matrix-Entfernung als finaler, separater PR (Nutzer will weg von Matrix).

Explizit **nicht** in diesem Scope:

- Echtzeit-Antworten / Sub-20-min-Latenz. Der Reasoner bleibt am 20-min-Tick (siehe „Latenz" unten).
- Medien (Bilder/Sprachnachrichten/Dokumente) — nur Text-Nachrichten.
- Gruppen-Awareness über einzelne Chats hinaus, Reaktionen, Read-Receipts.
- Multi-Device-Verwaltung jenseits „eine Nummer = eine Session".
- Offizielle WhatsApp Cloud API (verworfen: mehr Setup als Matrix).

## Architektur

```
┌─────────────────────── nauassist (.NET, bestehend) ───────────────────────┐
│  AutonomousAgentScheduler (20-min-Tick)                                    │
│    └─ ISourceObserver[]                                                     │
│         ├─ ImapObserver        (bleibt)                                     │
│         ├─ MatrixObserver      (entfällt in PR 12)                          │
│         └─ WhatsAppObserver ───┐  PollAsync: GET /sessions/:id/messages     │
│                                │            ?since=<cursor>                  │
│  ISourceSender[]               │                                            │
│         └─ WhatsAppSender ─────┤  SendAsync: POST /sessions/:id/send        │
│                                │                                            │
│  Endpoints (nur wenn Enabled)  │  WhatsAppSidecarClient (typed HttpClient)  │
│    /api/source-accounts/whatsapp/*  ── Bearer <SharedSecret> ──┐            │
│    /api/capabilities  → { whatsapp: true|false }               │           │
└────────────────────────────────────────────────────────────────┼──────────┘
                                                                   │ HTTP (internes Compose-Netz)
┌──────────────────── nauassist-wa (Node, NEU, Profil: whatsapp) ─┼──────────┐
│  Fastify-API  (Bearer-Auth via SIDECAR_TOKEN)                   ◀┘          │
│    POST /sessions            GET /sessions/:id (status+qr)                  │
│    GET  /sessions/:id/chats  GET /sessions/:id/messages?since=              │
│    POST /sessions/:id/send   DELETE /sessions/:id   GET /health            │
│                                                                            │
│  Baileys-Manager: pro Session 1 persistente WA-WebSocket-Verbindung        │
│    ├─ Auth-State: /data/sessions/<id>/  (useMultiFileAuthState)            │
│    └─ message.upsert  →  Buffer (SQLite /data/buffer.db, monotone seq)     │
│                                                                            │
│  VOLUME /data  (Sessions + Buffer überleben Restart)                        │
└────────────────────────────────────────────────────────────────────────────┘
```

### Warum ein Sidecar und kein In-Process

Matrix/IMAP sind **zustandslos pollbar**: Verbindung auf → „alles seit Cursor X" → zu. Baileys ist das Gegenteil — eine **persistente WebSocket-Session**, die dauerhaft offen sein muss, um Nachrichten als Echtzeit-Events zu empfangen, plus rotierende Krypto-Schlüssel. Das passt nicht in den 20-min-Tick eines `ISourceObserver`.

Lösung: Der Sidecar hält die Live-Session **dauerhaft** und puffert eingehende Nachrichten. Der `WhatsAppObserver` ruft beim Tick `GET /messages?since=<cursor>` ab — damit sieht die .NET-Seite **exakt wie Matrix/IMAP** aus (Poll + Cursor), ohne Umbau am Scheduler. Außerdem: Node-Crash reißt das .NET-Backend nicht mit, und die Runtimes bleiben getrennt.

### Latenz (bewusste Konsequenz)

Der Sidecar *empfängt* live, aber der Reasoner (Klassifikation → Suggestion → Push) läuft nur im 20-min-Tick. Für E-Mail ok, für WhatsApp evtl. träge. Konsistent mit der bestehenden Architektur-Entscheidung „20-min-Tick statt Long-Poll". Eine spätere Entkopplung (Sidecar pusht Webhook → sofortiger Tick) ist möglich, aber nicht in diesem Scope.

## Sidecar (`sidecar/`)

**Stack:** Node 22 + TypeScript, [`@whiskeysockets/baileys`](https://github.com/WhiskeySockets/Baileys), `fastify` (HTTP), `better-sqlite3` (Buffer), `qrcode` (QR → Data-URL), `pino` (Logging).

### Auth zwischen Backend und Sidecar

Shared Secret über `Authorization: Bearer <SIDECAR_TOKEN>`. Der Sidecar lauscht nur im internen Compose-Netz (`expose`, kein `ports`) — kein externer Zugriff. Fehlt/falsch das Token → 401.

### Session-Lifecycle

- **Auth-State** je Session in `/data/sessions/<sessionId>/` via `useMultiFileAuthState` (Baileys-nativ; Schlüssel rotieren ständig → Datei-State ist der robusteste Weg).
- Beim Sidecar-Start: `/data/sessions/*` scannen und alle vorhandenen Sessions automatisch reconnecten.
- `sessionId` wird vom Sidecar bei `POST /sessions` erzeugt (z. B. `randomUUID()`), persistiert in `source_accounts.credentials_json` auf der .NET-Seite (siehe Datenmodell).
- Disconnect-Handling: bei `DisconnectReason.loggedOut` Auth-State löschen + Status `loggedOut`; sonst Reconnect mit Backoff.

### Message-Buffer (`/data/buffer.db`)

```sql
CREATE TABLE messages (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,  -- monotoner Cursor
    session_id  TEXT NOT NULL,
    msg_id      TEXT NOT NULL,
    chat_id     TEXT NOT NULL,        -- JID, z.B. "4915...@s.whatsapp.net" oder "...@g.us"
    chat_name   TEXT,
    sender      TEXT,                 -- Sender-JID (bei Gruppen ≠ chat_id)
    sender_name TEXT,                 -- pushName
    text        TEXT NOT NULL,
    ts          INTEGER NOT NULL,     -- Unix-ms
    from_me     INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_messages_session_seq ON messages(session_id, seq);
```

- `message.upsert`-Event → Text extrahieren (`conversation` / `extendedTextMessage.text`), Non-Text überspringen, Zeile einfügen. `from_me`-Nachrichten werden **gespeichert aber markiert** (der Observer filtert sie, analog zu „eigene Nachrichten ignorieren" bei Matrix).
- **Cursor** = höchstes `seq`. `GET /messages?since=<seq>` liefert `WHERE seq > since ORDER BY seq LIMIT n` + neuen `cursor`.
- **Retention:** beim Insert ältere Zeilen kappen (z. B. `seq <` (max-seq − 5000) ODER `ts` älter als 14 Tage) — der Puffer ist nur Übergabe-Speicher, keine Historie.

### HTTP-API

| Methode & Pfad | Zweck | Response (Kern) |
| --- | --- | --- |
| `POST /sessions` | Neue Session starten (oder bestehende renutzen) | `{ sessionId, state }` |
| `GET /sessions/:id` | Status fürs QR-Polling | `{ state, qr?, phone? }` |
| `GET /sessions/:id/chats` | Chats für Allowlist-Auswahl | `[{ chatId, name }]` |
| `GET /sessions/:id/messages?since=&limit=` | Gepufferte Nachrichten | `{ messages: [...], cursor }` |
| `POST /sessions/:id/send` `{ chatId, text }` | Antwort senden | `{ ok: true }` |
| `DELETE /sessions/:id` | Logout + Auth-State löschen | `{ ok: true }` |
| `GET /health` | Compose-Healthcheck | `200 "ok"` |

`state` ∈ `pairing` (QR offen) · `connected` · `loggedOut` · `disconnected`. `qr` ist eine **Data-URL** (PNG), die das Frontend direkt in `<img>` rendert. `phone` ist die verbundene Nummer (JID), sobald `connected`.

### Sidecar-Dockerfile

`node:22-alpine`, `npm ci`, `npm run build` (tsc), `node dist/index.js`. `better-sqlite3` braucht Build-Tools (`apk add --no-cache python3 make g++` im Build-Stage, Multi-Stage damit das Runtime-Image schlank bleibt). Non-root, `VOLUME /data`, `EXPOSE 3000`, Healthcheck auf `/health`.

## .NET-Adapter (`src/Backend/Features/AutonomousAgent/Sources/WhatsApp/`)

### Credentials & Options

```csharp
// WhatsAppCredentials.cs  — was in source_accounts.credentials_json liegt
public sealed class WhatsAppCredentials
{
    public string SessionId { get; init; } = "";   // verweist auf Sidecar-Session
    public string PhoneLabel { get; init; } = "";   // nur Anzeige, z.B. "+49 151 …"
    public static WhatsAppCredentials Parse(string json) { /* wirft bei leerem SessionId */ }
}

// WhatsAppOptions.cs  — appsettings / ENV
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }                  // Default: false (opt-in!)
    public string SidecarBaseUrl { get; set; } = "";   // z.B. http://nauassist-wa:3000
    public string SharedSecret { get; set; } = "";
    public int MaxBodyChars { get; set; } = 2000;
    public int MessageBatchLimit { get; set; } = 200;
}
```

Kein WhatsApp-Geheimnis liegt in der NauAssist-DB — der Auth-State wohnt komplett im Sidecar-Volume. `credentials_json` hält nur `{ sessionId, phoneLabel }`.

### WhatsAppSidecarClient

Typisierter `HttpClient`-Wrapper (named client `"WhatsApp"`, BaseAddress = `SidecarBaseUrl`, Default-Header `Authorization: Bearer <SharedSecret>`). Methoden: `CreateSessionAsync`, `GetSessionAsync`, `ListChatsAsync`, `GetMessagesAsync(sessionId, since, limit)`, `SendAsync(sessionId, chatId, text)`, `DeleteSessionAsync`.

### WhatsAppObserver : ISourceObserver

`SourceKey = "whatsapp"`. Pro enabled Account:
- `WhatsAppCredentials.Parse` → `sessionId`. Leere Allowlist → überspringen (wie Matrix/IMAP).
- Cursor aus `source_cursors` (`source="whatsapp"`, `accountId`) lesen.
- `GetMessagesAsync(sessionId, since: cursor, limit)` → neuen Cursor sofort persistieren.
- **Initial-Sync** (kein Cursor): nur Cursor auf aktuellen Stand setzen, alte Nachrichten verwerfen — identisch zur Matrix/IMAP-Logik.
- Mapping → `RawSignal`:
  - `SourceRef = chatId` (Thread-Awareness pro Chat, wie Matrix-Raum).
  - `Sender = senderName ?? sender`.
  - `Text` auf `MaxBodyChars` kürzen.
  - `Metadata = { chatId, messageId, senderJid, chatName }`.
  - `from_me`-Nachrichten überspringen (sonst loopt der Agent auf sich selbst).
- Allowlist-Filter: nur Signale, deren `chatId` in `account.Allowlist`.

### WhatsAppSender : ISourceSender

`Source = "whatsapp"`. `SendAsync` → `WhatsAppCredentials.Parse(account)` → `client.SendAsync(sessionId, targetRef, body)`. `targetRef` ist die `chatId` (= `RawSignal.SourceRef`). `metadata` ungenutzt (kein Threading-Header bei WhatsApp).

### DI-Registrierung (`Program.cs`) — bedingt

```csharp
builder.Services.Configure<WhatsAppOptions>(
    builder.Configuration.GetSection("AutonomousAgent:WhatsApp"));

var waOptions = builder.Configuration
    .GetSection("AutonomousAgent:WhatsApp").Get<WhatsAppOptions>() ?? new();
if (waOptions.Enabled)
{
    builder.Services.AddHttpClient("WhatsApp");
    builder.Services.AddScoped<WhatsAppSidecarClient>();
    builder.Services.AddScoped<ISourceObserver, WhatsAppObserver>();
    builder.Services.AddScoped<ISourceSender, WhatsAppSender>();
}
```

Ist `Enabled=false`, existiert kein WhatsApp-Observer/Sender → der Scheduler ignoriert WhatsApp vollständig, und die Endpoints (s. u.) werden nicht gemappt.

## API-Endpoints

### Capabilities (immer vorhanden)

`GET /api/capabilities` → `{ "whatsapp": true|false }`. Liest `IOptions<WhatsAppOptions>.Enabled`. Frontend fragt das einmal beim Settings-Load ab.

### WhatsApp-Helfer (nur wenn `Enabled`, in `SourceAccountsEndpoints`)

| Endpoint | Zweck |
| --- | --- |
| `POST /api/source-accounts/whatsapp/start` | Sidecar-Session starten → `{ sessionId, qr, state }` |
| `GET  /api/source-accounts/whatsapp/session/{sessionId}` | Pairing-Status pollen → `{ state, qr?, phone? }` |
| `GET  /api/source-accounts/whatsapp/session/{sessionId}/chats` | Chats listen (für Allowlist) |
| `GET  /api/source-accounts/{id}/whatsapp/chats` | Chats für gespeicherten Account neu laden |

Anlegen läuft über den **bestehenden** `POST /api/source-accounts` mit `kind="whatsapp"`, `credentials={ sessionId, phoneLabel }`, `allowlist=[chatId,…]`. Im Create-Handler kommt ein Validierungs-Zweig `kind == "whatsapp" → WhatsAppCredentials.Parse` hinzu; in `RedactCredentials` ein Zweig, der `sessionId` + `phoneLabel` durchreicht (keine Geheimnisse).

Sind die Helfer-Endpoints nicht gemappt (Flag aus), liefert das Frontend sie nie an — die Section ist dann ohnehin ausgeblendet.

## Frontend

### Capability-Gating

- `api/capabilities.ts`: `getCapabilities(): Promise<{ whatsapp: boolean }>`.
- `SettingsPage.tsx`: Capabilities beim Mount laden. Nav-Item + `<WhatsAppSection>` nur rendern, wenn `caps.whatsapp`. Nav-Nummerierung passt sich an (nach PR 12 ohne Matrix neu durchnummeriert).

### WhatsAppSection.tsx (QR-Pairing-Flow)

Aufbau analog zu `MatrixSection`/`ImapSection` (gleiche Mono/Tailwind-Optik, AccountCard + AddForm). Der Unterschied steckt im Anlege-Flow:

```
[+ WHATSAPP VERBINDEN]
   → POST /whatsapp/start  → { sessionId, qr }
   → QR anzeigen (<img src={qr}/>), alle 2 s GET /whatsapp/session/:id pollen
   → state="connected": Telefonnummer anzeigen, GET …/chats laden
   → Chats als Checkbox-Liste (Allowlist), Anzeigename setzen
   → [SPEICHERN] → POST /api/source-accounts {kind:"whatsapp", credentials:{sessionId,phoneLabel}, allowlist}
```

AccountCard: Anzeigename, Telefon-Label, Allowlist-Count, „CHATS VERWALTEN" (neu laden + Allowlist editieren), AKTIVIEREN/DEAKTIVIEREN, LÖSCHEN (löscht Account; optional zusätzlich Sidecar-Session via `DELETE`). Erklärtext mit ToS-/Zweitnummer-Hinweis.

`api/source-accounts.ts`: WhatsApp-Typen + `startWhatsAppSession`, `getWhatsAppSession`, `listWhatsAppChats(sessionId)`, `listWhatsAppChatsForAccount(id)`, `createWhatsAppAccount(...)`.

## Deployment

### docker-compose.yml (neu, Repo-Root)

```yaml
services:
  nauassist:
    build: .
    image: ghcr.io/benediktnau/nauassist:latest
    ports: ["8080:8080"]
    volumes: ["nauassist-data:/app/data"]
    environment:
      - Time__Zone=Europe/Berlin
      - AutonomousAgent__WhatsApp__Enabled=${WHATSAPP_ENABLED:-false}
      - AutonomousAgent__WhatsApp__SidecarBaseUrl=http://nauassist-wa:3000
      - AutonomousAgent__WhatsApp__SharedSecret=${WHATSAPP_SHARED_SECRET:-}
    restart: unless-stopped

  nauassist-wa:
    build: ./sidecar
    image: ghcr.io/benediktnau/nauassist-wa:latest
    profiles: ["whatsapp"]          # startet NUR bei aktivem Profil
    volumes: ["nauassist-wa:/data"]
    environment:
      - SIDECAR_TOKEN=${WHATSAPP_SHARED_SECRET:-}
      - PORT=3000
    expose: ["3000"]                # nur internes Netz, kein Host-Port
    restart: unless-stopped

volumes:
  nauassist-data:
  nauassist-wa:
```

### Der eine Schalter (`.env`)

`.env.example` dokumentiert die WhatsApp-Aktivierung als **einen logischen Schalter** (zwei Zeilen, da Compose-Profile nicht aus Werten ableitbar sind):

```bash
# WhatsApp deaktiviert (Default): Sidecar startet nicht, Backend-Feature aus, UI-Section weg.

# --- WhatsApp aktivieren: diese Zeilen einkommentieren ---
# COMPOSE_PROFILES=whatsapp        # startet den nauassist-wa-Container automatisch mit
# WHATSAPP_ENABLED=true            # schaltet Backend-Feature + Endpoints + UI-Section frei
# WHATSAPP_SHARED_SECRET=<openssl rand -hex 24>
```

- **Aus (Default):** `docker compose up` startet nur `nauassist`. `WHATSAPP_ENABLED` ist `false` → keine WA-Registrierung, `/api/capabilities` meldet `whatsapp:false` → UI versteckt die Section. Genau das geforderte Verhalten.
- **An:** beide Zeilen gesetzt → `docker compose up` zieht das `whatsapp`-Profil und startet den Sidecar automatisch mit; Backend + UI sind frei. „Auto-deployed, wenn gewünscht."

### Release-Pipeline

`.github/workflows/release.yml` bekommt einen zweiten Build-/Push-Step für `ghcr.io/benediktnau/nauassist-wa` (Context `./sidecar`). Damit ist das Sidecar-Image bei Bedarf einfach ziehbar, ohne lokalen Build.

## Datenmodell

Keine neue NauAssist-Migration nötig — `source_accounts` ist bereits generisch (`kind`, `credentials_json`, `allowlist_json`). WhatsApp ist nur ein neuer `kind`-Wert:

```
source_accounts:
  kind             = "whatsapp"
  credentials_json = {"sessionId":"<uuid>","phoneLabel":"+49 151 …"}
  allowlist_json   = ["4915...@s.whatsapp.net", "...@g.us"]

source_cursors:
  source="whatsapp", account_id=<id>, cursor="<letztes seq aus Sidecar-Buffer>"
```

Der einzige neue persistente Zustand ist der **Sidecar-Buffer + Auth-State** (eigenes Volume, außerhalb der NauAssist-DB).

## PR-Aufteilung

Fortsetzung der „Autonomer Agent"-Nummerierung. Jeder PR ist für sich baubar/testbar; das Feature ist erst ab PR 10 (UI) end-to-end nutzbar, vorher per `curl` gegen den Sidecar.

| PR | Inhalt |
| --- | --- |
| **7 — Sidecar-Grundgerüst** | `sidecar/`: Baileys-Session-Manager, QR, SQLite-Buffer, Fastify-API, Bearer-Auth, Dockerfile, Health. Standalone via `curl` testbar. |
| **8 — .NET-Adapter** | `WhatsAppOptions/Credentials/SidecarClient/Observer/Sender`, bedingte DI, Unit-Tests (Observer-Mapping, Credentials.Parse, Sender). Flag default aus. |
| **9 — Endpoints + Capabilities** | WhatsApp-Helfer-Endpoints, Create-Validierung + Redaction-Zweig, `GET /api/capabilities`. Tests. |
| **10 — Frontend** | `WhatsAppSection` (QR-Flow), `api/capabilities.ts`, `api/source-accounts.ts`-Erweiterung, `SettingsPage`-Gating. |
| **11 — Compose + Profil + Release** | `docker-compose.yml`, `.env.example`, Sidecar-Image in `release.yml`, Doku. |
| **12 — Matrix entfernen** | Backend `Sources/Matrix/*` + DI + Endpoints raus; `MatrixSection.tsx` + API-Calls raus; Nav neu nummeriert. `source_accounts`-Tabelle bleibt generisch. Memory/Doku aktualisieren. |

## Tests

### Backend (`src/Backend.Tests/`)

| Test | Prüft |
| --- | --- |
| `WhatsAppCredentialsTests` | Parse-Roundtrip, leere `sessionId` wirft |
| `WhatsAppObserverTests` | Mapping Sidecar-DTO → `RawSignal` (SourceRef=chatId, Metadata, Body-Kürzung), Allowlist-Filter, `from_me`-Skip, Initial-Sync setzt nur Cursor, Cursor-Persistenz |
| `WhatsAppSenderTests` | `SendAsync` ruft Sidecar mit chatId+body (HttpClient gemockt) |
| `CapabilitiesEndpointTests` | `whatsapp:true` wenn Enabled, sonst `false`; WA-Helfer-Endpoints liefern 404 wenn aus |
| `SourceAccountsEndpointsTests` | Create mit `kind="whatsapp"` validiert, Redaction gibt keine Geheimnisse preis |

Sidecar selbst: leichter Smoke-Test (Buffer-Insert/Cursor-Query gegen besser-sqlite3) — kein E2E gegen echtes WhatsApp im CI.

### Manuelle Verifikation (vor Merge von PR 10/11)

1. `.env` mit WhatsApp-Zeilen → `docker compose up`. Beide Container laufen, `nauassist-wa` nur wegen Profil.
2. Settings öffnen → WhatsApp-Section sichtbar. „Verbinden" → QR erscheint → mit Zweitnummer scannen → `connected` + Nummer.
3. Chats laden, einen Chat in die Allowlist, speichern.
4. Vom Handy in den freigegebenen Chat eine Termin-Anfrage schreiben → `POST /api/suggestions/poll-now` → Suggestion entsteht → Push.
5. Antwort-Flow: Slot picken → Direkt-Send → Nachricht erscheint im WhatsApp-Chat.
6. Container neu starten → Session bleibt verbunden (kein erneuter QR), Cursor überlebt.
7. `.env`-Zeilen wieder auskommentieren → `docker compose up` → Sidecar weg, Section weg, Backend ohne WA — keine Fehler.

## Risiken & offene Punkte

- **Ban-Risiko (ToS):** akzeptiert; UI weist auf Zweitnummer hin.
- **Baileys-Bruch bei WA-Updates:** Node-Original wird am schnellsten gefixt → `npm update` im Sidecar. Versions-Pinning + gelegentliches Update einplanen.
- **`better-sqlite3` native build:** braucht Build-Tools im Alpine-Build-Stage; Multi-Stage hält das Runtime-Image schlank. Alternative bei Ärger: `node:22-slim` (Debian).
- **QR-Timeout:** WhatsApp rotiert den QR alle ~20 s und gibt nach mehreren ungescannt auf. Der Sidecar liefert beim Polling stets den **aktuellen** QR; das Frontend zeigt immer den letzten `qr` aus dem Status.
- **Latenz:** 20-min-Tick (s. o.) — bewusst, später entkoppelbar.
- **Multi-Account:** Sidecar unterstützt mehrere Sessions (mehrere Nummern) bereits über `sessionId`; UI im ersten Wurf auf „eine Nummer" ausgelegt, mehr ist additiv.
