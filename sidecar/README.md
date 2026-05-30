# nauassist-wa — WhatsApp-Sidecar (Baileys)

Hält die persistente WhatsApp-Web-Session ([Baileys](https://github.com/WhiskeySockets/Baileys)),
puffert eingehende Text-Nachrichten in SQLite und bietet dem .NET-Backend eine
schmale HTTP-API. Das Backend pollt den Puffer im 20-min-Tick (`WhatsAppObserver`)
und sendet Antworten (`WhatsAppSender`).

> ⚠️ Inoffizielle WhatsApp-Web-Anbindung (ToS). Die genutzte Nummer kann gesperrt
> werden — **Zweitnummer** verwenden, nicht die private Hauptnummer.

## Umgebungsvariablen

| Variable | Default | Bedeutung |
| --- | --- | --- |
| `PORT` | `3000` | HTTP-Port |
| `DATA_DIR` | `/data` | Volume: `sessions/<id>/` (Auth-State) + `buffer.db` |
| `SIDECAR_TOKEN` | _(leer)_ | Bearer-Token. Leer = Auth aus (nur lokal!) |
| `MAX_BUFFER_ROWS` | `5000` | Zeilen-Obergrenze des Puffers |
| `RETENTION_DAYS` | `14` | Nachrichten älter als X werden gekappt |
| `LOG_LEVEL` | `info` | pino-Loglevel |

## HTTP-API

Alle Routen außer `/health` erwarten `Authorization: Bearer <SIDECAR_TOKEN>`.

| Methode & Pfad | Zweck |
| --- | --- |
| `POST /sessions` `{ sessionId? }` | Session starten/renutzen → `{ sessionId, state }` |
| `GET /sessions/:id` | Status fürs QR-Polling → `{ state, qr?, phone? }` |
| `GET /sessions/:id/chats` | Chats für Allowlist → `[{ chatId, name }]` |
| `GET /sessions/:id/messages?since=&limit=` | Gepufferte Nachrichten → `{ messages, cursor }` |
| `POST /sessions/:id/send` `{ chatId, text }` | Antwort senden → `{ ok }` |
| `DELETE /sessions/:id` | Logout + Auth-State löschen |
| `GET /health` | `200 "ok"` |

`state` ∈ `pairing` · `connected` · `loggedOut` · `disconnected`. `qr` ist eine
Data-URL (PNG) zum direkten Anzeigen.

## Lokal entwickeln

```bash
npm install
SIDECAR_TOKEN=dev DATA_DIR=./data npm run dev

# Session starten + QR holen
curl -s localhost:3000/sessions -H 'Authorization: Bearer dev' -X POST -d '{}'
curl -s localhost:3000/sessions/<id> -H 'Authorization: Bearer dev' | jq -r .qr
# Data-URL in den Browser kopieren, mit dem Handy scannen.
```

## Docker

```bash
docker build -t nauassist-wa .
docker run --rm -p 3000:3000 -v wa:/data -e SIDECAR_TOKEN=secret nauassist-wa
```

Im Gesamt-Setup wird der Container über das docker-compose-Profil `whatsapp`
gestartet (siehe Repo-Root `docker-compose.yml`).
