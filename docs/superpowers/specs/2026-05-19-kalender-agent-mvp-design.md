# NauAssist — Kalender-Agent MVP

**Datum:** 2026-05-19
**Status:** Entwurf (zur Review beim User)
**Sprache:** Deutsch

## 1. Zweck

NauAssist ist ein persönlicher Kalender-Agent, der eingehende Terminanfragen (zunächst per manuellem Paste in einem lokalen Web-Chat, später aus echten Quellen wie WhatsApp oder E-Mail) entgegennimmt, im Google Kalender nach passenden Slots sucht, dem User 2–3 Vorschläge unterbreitet und nach Bestätigung den Termin direkt anlegt. Der Agent passt sich durch explizite, vom User formulierte Regeln an dessen Präferenzen an (z. B. „abends nach 18 Uhr lieber frei").

## 2. MVP-Scope

Am Ende des MVP läuft folgender Workflow vollständig durch:

1. User paste-t eine Terminanfrage als Klartext in das lokale React-Chat-UI.
2. Der Agent parst die Anfrage (Anfragesteller, ggf. Dauer, ggf. Zeitbereich) und schaut in den verknüpften Google Kalender.
3. Der Agent berücksichtigt die im System hinterlegten Regeln (deterministisch) und schlägt 2–3 passende Slots vor.
4. Der User bestätigt einen Slot im Chat.
5. Der Agent legt den Termin im Google Kalender an, schreibt einen Audit-Eintrag und bestätigt.
6. Der User kann Regeln im Chat hinzufügen, auflisten und löschen.

## 3. Nicht-Ziele (MVP)

- Keine Anbindung an WhatsApp, E-Mail, Signal, Matrix, Teams o. Ä. — Quellen-Adapter sind als Provider-Interface vorbereitet, aber MVP-Implementierung ist „Manual Paste".
- Kein automatisches Antworten an den Anfragesteller. Der User trägt das Ergebnis selbst zurück in seinen Original-Chat.
- Kein implizites Lernen aus Verhalten. Nur explizit formulierte Regeln, vom User per Chat eingegeben.
- Kein Tentative-Status / Pending-Workflow im Kalender. Bestätigung führt direkt zum finalen Eintrag.
- Kein Multi-User, kein Login, keine Authentifizierung am Frontend.
- Keine proaktive Reflektion / kein Background-Job, der den Kalender autonom umordnet.
- Kein Voice (STT/TTS).
- Kein Container-Setup als MVP-Pflicht. `dotnet run` + `npm run dev` reichen für die Entwicklung; Docker kommt als optionales letztes Issue.

## 4. Technologie-Entscheidungen

| Bereich | Wahl | Begründung |
|---|---|---|
| Backend-Sprache & Runtime | .NET 10 (`net10.0`) | Aktuelle LTS-Generation, neues `.slnx`-Format |
| Web-Stack | ASP.NET Core Minimal API | Schlank, passt zu „eine Aufgabe, ein Endpoint" |
| Mediator | `martinothamar/Mediator` | Source-generiert, keine Runtime-Reflection, sehr schnell |
| Agent-Framework | Microsoft Agent Framework | Native .NET-Integration, Tool-Calling, kompatibel mit OpenAI-Endpoints |
| LLM-Hosting | Ollama lokal auf Host | Datenschutz, kein Cloud-Round-Trip, kostenlos |
| LLM-Modell | `qwen2.5:7b-instruct` oder `llama3.1:8b-instruct` (Default offen) | Deutschfähig, unterstützen Tool-Calling in Ollama; finale Wahl nach Hardware-Test |
| Persistenz | SQLite via `Microsoft.Data.Sqlite` + Dapper | Eine Datei, kein Server, ausreichend für Single-User |
| Kalender (MVP) | Google Calendar via `Google.Apis.Calendar.v3` | Privates Konto des Users; Provider-Interface offen für CalDAV/M365 |
| Frontend-Stack | Vite + React + TypeScript (strict) | Schnell, klein, gut tooling |
| Frontend-UI-Bibliothek | shadcn/ui + Tailwind CSS | Vom User vorgegeben; nur diese Komponenten |
| Test-Framework | xUnit | Standard im .NET-Ökosystem |

## 5. Solution-Struktur

```
/
├── src/
│   ├── Backend/                  ← Minimal API + Core AI + Mediator (alles hier)
│   ├── Backend.Tests/            ← xUnit, automatisierte Tests
│   └── NauAssist.slnx
├── frontend/                     ← Vite + React + TS, getrennt vom .NET-Build
├── tests/
│   └── manual-scenarios/         ← Markdown-Goldfiles für LLM-Verhalten (manuell)
└── docs/
    └── superpowers/specs/
```

### Backend-Ordnerstruktur (Vertical Slices)

```
src/Backend/
├── Program.cs                    ← Host, DI, Mediator-Setup
├── appsettings.json
├── Backend.csproj
├── Endpoints/
│   ├── ChatEndpoints.cs          ← POST /api/chat, GET /api/chat/history
│   └── RulesEndpoints.cs         ← GET/POST/DELETE /api/rules
└── Features/
    ├── Chat/
    │   ├── SendMessage/          ← Request + Handler + Response
    │   └── ChatHistory/
    ├── Agent/
    │   ├── AgentRunner.cs
    │   └── Tools/                ← Tool-Adapter, rufen Mediator.Send
    ├── Calendar/
    │   ├── ICalendarProvider.cs
    │   ├── Google/               ← GoogleCalendarProvider, OAuth
    │   ├── LookupFreeSlots/
    │   └── CreateEvent/
    ├── Rules/
    │   ├── Rule.cs
    │   ├── AddRule/
    │   ├── ListRules/
    │   ├── DeleteRule/
    │   └── RuleApplicator.cs
    └── Infrastructure/
        ├── Persistence/          ← SQLite + Schema-Migrationen
        ├── Llm/                  ← Ollama-Client (OpenAI-kompatibel)
        └── Audit/
```

### Mediator-Nutzungsregel

Jede fachliche Aktion ist ein Mediator-Request — gleich, ob sie von einem HTTP-Endpoint oder einem Agent-Tool ausgelöst wird. Endpoints und Tools sind dünne Adapter, die `Mediator.Send(...)` aufrufen. Damit haben Tests, Endpoints und Tools dieselben Einstiegspunkte.

## 6. Komponenten

### 6.1 Chat Surface

- **Endpoint:** `POST /api/chat` — nimmt `{ message: string }`, gibt `{ reply: string, proposals?: Slot[] }` zurück. `GET /api/chat/history` liefert die letzten 50 Nachrichten für den Initial-Render.
- **Synchrones Request/Response**, kein Streaming im MVP. Loading-Spinner im Frontend reicht.
- **Single Session:** `session_id` ist eine Konstante in der Config; kein Multi-Tab- oder Multi-User-State.

### 6.2 Agent Runner (Microsoft Agent Framework)

- Orchestriert eine Konversation gegen das LLM mit den registrierten Tools.
- Stateless pro Aufruf: lädt sich die letzten ~15 Nachrichten aus der Persistence, baut den Prompt, lässt das LLM laufen, gibt die finale Antwort zurück.
- **Tool-Loop-Limit:** Maximal 5 Tool-Iterationen pro User-Message, dann muss eine finale Text-Antwort kommen.
- **Tool-Adapter rufen Mediator:** Jeder im Agent registrierte Tool-Adapter ist ein dünner Wrapper, der die Argumente in einen Mediator-Request umwandelt und `Mediator.Send(...)` ausführt. Die fachliche Logik lebt im jeweiligen Handler, nicht im Tool-Adapter selbst.

**Registrierte Tools:**

| Tool | Zweck |
|---|---|
| `lookup_free_slots(from, to, duration_minutes)` | Sucht freie Slots im Range, filtert gegen Regeln, liefert ~5–8 Kandidaten |
| `create_event(title, start, end, description?)` | Legt Termin im Kalender an |
| `get_calendar_range(from, to)` | Roh-Lookup für Kontextfragen („was steht morgen an?") |
| `list_rules()` | Listet aktive Regeln |
| `add_rule(natural_text)` | Parst Klartext-Regel in strukturierte Form und speichert |
| `delete_rule(rule_id)` | Entfernt Regel |

### 6.3 LLM Client

- Schmaler Wrapper um Ollamas OpenAI-kompatibles Endpoint (`/v1/chat/completions`), inkl. Tool-Calling.
- `Task<LlmResponse> ChatAsync(messages, tools, ct)` — Antwort ist entweder Text oder eine Liste von Tool-Calls.
- Konfiguration (Host, Modell, Timeout) aus `appsettings.json`.
- Hard Timeout 60 s pro Call, 1× Retry nach 2 s bei kurzzeitigem Netzfehler.

### 6.4 Calendar Provider

```csharp
interface ICalendarProvider {
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CT ct);
    Task<string> CreateEventAsync(NewEvent ev, CT ct);  // returns provider event ID
}
```

- MVP-Implementierung: `GoogleCalendarProvider`
- Auth: Desktop-OAuth-Flow von Google; Token in SQLite gespeichert, Refresh automatisch
- **Initial-Setup:** beim ersten Start druckt das Backend eine OAuth-URL ins Log; User klickt sie an, autorisiert, Token wird persistiert. Einmal pro Account.

### 6.5 Rules

```
Rule {
  id, text, daysOfWeek[], timeRangeStart, timeRangeEnd, hardness ∈ {hard, soft}, createdAt
}
```

- **AddRule:** ein Sub-LLM-Call extrahiert die strukturierte Form aus dem Klartext. Speicherung passiert sofort; der Agent zeigt im Chat zurück, **was er extrahiert hat** (Tage, Zeit-Range, Härte). Falsch interpretiert → User löscht und reformuliert. Kein „Pending"-Zwischenstand.
- **RuleApplicator:** rein deterministisch. Input: Slot-Kandidaten + aktive Regeln. Output: annotierte Liste (`passt` / `verstößt gegen Regel X (hard|soft)`). Harte Verstöße werden ausgefiltert, weiche markiert.
- **Fallback** wenn nach harter Filterung weniger als 2 Slots übrig sind: weiche Regeln werden ebenfalls angezeigt, mit Hinweis im Chat.

### 6.6 Persistence (SQLite)

Tabellen:
- `messages` — Chat-History (session_id, role, content, proposals_json, created_at)
- `rules` — Regeln (siehe oben)
- `audit_log` — append-only (triggering_message_id, tool_name, tool_args_json, result_json, provider_event_id?, created_at)
- `google_oauth` — Tokens (access_token, refresh_token, expires_at)

Schema-Migrationen: schlanke `schema_version`-Tabelle + manuelle SQL-Migrationen aus Embedded Resources, ausgeführt beim Startup. Schlägt eine Migration fehl, startet die App nicht.

DB-Pfad: konfigurierbar, Default `./data/nauassist.db`.

### 6.7 Audit Log

Jede Außenwirkung (Termin angelegt, Regel geändert) schreibt einen Audit-Eintrag mit: auslösender User-Message-ID, Tool-Name, Tool-Argumenten, Provider-Antwort, Zeitstempel.

Audit-Schreiben passiert **nach** der externen Aktion. Wenn das Audit-Schreiben fehlschlägt, wird das im Log auf Warning-Level vermerkt, die User-Aktion gilt aber als erfolgreich. Wir lassen lieber einen unauditierten Termin stehen, als dem User vorzulügen, er sei nicht angelegt.

## 7. Datenfluss

### Szenario A — Terminanfrage rein, Vorschläge raus

```
React POST /api/chat
   → ChatEndpoint → Mediator.Send(SendMessageRequest)
   → SendMessageHandler
       ├─ User-Message in DB
       ├─ History laden (letzte ~15)
       └─ AgentRunner.HandleAsync
            └─ Ollama-LLM → Tool-Call: lookup_free_slots(from, to, duration)
                 → LookupFreeSlots-Tool → Mediator.Send(LookupFreeSlotsRequest)
                    → Handler
                       ├─ aktive Rules aus DB
                       ├─ ICalendarProvider.GetEventsAsync (Google API)
                       ├─ freie Lücken berechnen, ≥duration, in Arbeitszeit
                       └─ RuleApplicator: harte raus, weiche annotieren
                          → ~5–8 Kandidaten
                 → LLM bekommt Tool-Result
                 → LLM formuliert Antwort: „Ich hab drei Slots für dich" + 3 strukturierte Vorschläge (ausgewählt aus den Kandidaten)
       └─ Agent-Message in DB
   → Response
```

### Szenario B — Bestätigung

```
„Ja, Slot 2"
   → AgentRunner
   → LLM hat History → versteht den Bezug
   → Tool-Call: create_event(title, start, end)
   → CreateEventHandler
       ├─ optional: Re-Check Slot noch frei (Race-Schutz)
       ├─ ICalendarProvider.CreateEventAsync (Google API)
       └─ Audit-Eintrag
   → LLM-Antwort: „Erledigt"
```

### Szenario C — Regel hinzufügen

```
„Übrigens, abends nach 18 Uhr keine Termine"
   → AgentRunner
   → LLM erkennt Regel-Eingabe
   → Tool-Call: add_rule(natural_text="...")
   → AddRuleHandler
       ├─ Sub-LLM-Call mit Schema-Prompt → struktur
       ├─ speichern
       └─ struktur zurück
   → LLM-Antwort: „Verstanden: Mo–So nach 18:00, hart. Gespeichert."
```

### Konversations-History

- Letzte ~15 Nachrichten in jedem LLM-Aufruf
- Alle Nachrichten persistiert; ältere sind für das LLM „vergessen", bleiben aber in der DB

## 8. Fehlerbehandlung

Leitprinzip: Fehler landen im Chat, nicht in HTTP-500-Stack-Traces.

| Fehlerklasse | Reaktion |
|---|---|
| Ollama unerreichbar / Timeout | 1× Retry nach 2 s, dann Chat-Hinweis. Kein Audit. |
| Google OAuth abgelaufen + Refresh tot | Chat-Hinweis: „Bitte einmal `dotnet run -- auth` ausführen." Kein Hintergrund-Re-Auth. |
| Google Rate Limit | 1× Retry mit Backoff, dann Chat-Hinweis |
| Google 5xx / Netzfehler | 1× Retry. Bei `create_event` kein automatischer Retry (potenzielle Doppel-Buchung). |
| Slot inzwischen belegt | Chat: „Der Slot ist nicht mehr frei. Neue suchen?" |
| LLM-Tool-Call mit invaliden Args | Handler liefert strukturierten Fehler als Tool-Result zurück; LLM korrigiert sich selbst oder fragt nach |
| LLM ruft nicht-existentes Tool | Strukturierter Fehler ans LLM, Selbstkorrektur |
| Tool-Loop > 5 Iterationen | Hard Stop. Chat: „Ich komme da gerade nicht weiter." |
| SQLite-Migration schlägt fehl | App startet nicht (lautes Crashen) |
| Audit-Write schlägt fehl | Log-Warning, User-Aktion bleibt erfolgreich |

Logging: `Microsoft.Extensions.Logging` Default, stdout/stderr, Levels via `appsettings.json`. Jede Außenwirkung loggt auf Info mit Correlation-ID.

## 9. Tests

**Test-Pyramide:**
- ~80% Unit-Tests: pro Mediator-Handler 1 Happy Path + 1–2 Edge Cases. `RuleApplicator` ausgiebig.
- ~15% Integration-Tests: `ChatEndpoint` end-to-end via `WebApplicationFactory` mit Fake-Doubles. DB-Migration-Smoketest.
- ~5% Smoke-Tests gegen echte Dienste: skippen sauber, wenn Ollama/Google nicht erreichbar. Nicht Teil des Default-Test-Runs.

**Test-Doubles:**
- `FakeCalendarProvider` — in-memory Event-Liste, `.Seed(...)` in Tests
- `FakeLlmClient` — gescriptete Antworten auf bestimmte Inputs
- SQLite in Tests: pro Test eine Temp-Datei, nach Fehlern inspizierbar

**Test-Benennung:** Szenario-basiert, nicht implementierungsbasiert. Beispiel: `LookupFreeSlots_RespectsHardRules_NoEveningSlotsReturned`.

**TDD als Default-Disziplin:** jede Handler-Implementierung beginnt mit dem fehlschlagenden Test.

**Frontend-Tests (MVP):** Keine automatisierten. TypeScript-strict, ESLint, Prettier. Manuelles Browser-Testing. Sobald das Frontend echte Logik bekommt, neu bewerten.

**LLM-Verhalten:** Nicht automatisiert. Manuelle Smoke-Scenarios als Markdown unter `tests/manual-scenarios/`, die bei Modell- oder Prompt-Änderungen durchgespielt werden.

## 10. Annahmen / offene Detail-Entscheidungen

Diese Punkte sind bewusst nicht im Detail festgelegt und werden in der Implementierung mit sinnvollen Defaults belegt, sind aber später konfigurierbar:

- **Zeitzone:** Default `Europe/Berlin`
- **Arbeitszeiten** für „freie Slots": Default Mo–Fr 09:00–18:00 (überschreibbar per Regel)
- **Default-Termin-Dauer** wenn nicht in der Anfrage erwähnt: 60 Minuten
- **Such-Horizont** für `lookup_free_slots` ohne explizite Range: nächste 14 Tage
- **Frontend-Port / Backend-Port:** Default-Vite-Port (5173) bzw. ASP.NET-Default (5000/5001), CORS für `localhost` offen
- **Ollama-Modell-Default:** wird nach Hardware-Test gewählt (Kandidaten: `qwen2.5:7b-instruct`, `llama3.1:8b-instruct`)

## 11. Spätere Etappen (explizit nicht MVP)

In dieser Reihenfolge denkbar, jeweils als eigene Spec wenn relevant:

1. **Streaming der LLM-Antworten** via SSE
2. **Implizites Lernen:** „du lehnst Slots immer zwischen 17–18 ab — Regel daraus machen?" (mit Bestätigung)
3. **Inbound-Provider** für E-Mail (IMAP)
4. **Inbound-Provider** für WhatsApp (über Matrix-Bridge, mit oder ohne Zweitnummer)
5. **Antwort-Vorlagen** an den Anfragesteller (Vorlage-Mechanismus)
6. **CalDAV-** und **Microsoft-365-Calendar-Provider** als weitere Implementierungen des bestehenden Interfaces
7. **Container-Setup** (Dockerfile + Compose) wenn der MVP läuft und produktiv auf der Box als Daemon laufen soll
8. **Multi-Kalender-Sicht** (Privat + Beruflich vereint)
