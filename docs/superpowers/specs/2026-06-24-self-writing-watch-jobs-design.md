# Self-Writing Tools — Watch-Jobs & asynchroner Agent

> Status: **Konzept / Design-Idee** (noch keine Implementierung).
> Branch: `claude/nauassist-self-writing-tools-6k4qff`.

## Ziel

NauAssist soll sich auf Zuruf **selbst kleine, dauerhaft laufende „Tools" schreiben**
und sich damit erweitern. Leitbeispiel des Nutzers:

> „Bitte schaue, wann online wieder Midea-PortaSplit-Anlagen verfügbar sind, dann
> benachrichtige mich."

Daraufhin soll der Assistent **selbständig einen kleinen Watcher anlegen**, der
regelmäßig im Web nach Verfügbarkeit sucht, **gründlich und mehrquellig** prüft
und **sofort per Push (Web-Push + Pushover)** meldet, sobald die Anlage bestellbar ist.

Drei harte Anforderungen, an denen sich das Design messen lassen muss:

1. **Selbst-Erweiterung** — der Assistent definiert aus natürlicher Sprache eine
   *neue, persistente, autonome Fähigkeit* (nicht nur eine einmalige Antwort).
2. **Gründliche Prüfung + sofortige Benachrichtigung** — keine Fehlalarme, aber
   ohne Verzögerung melden, sobald das Ziel erreicht ist; Kanal u.a. **Pushover**.
3. **Parallelität & Asynchronität** — während ein oder mehrere Watcher im
   Hintergrund laufen, bleibt der Chat-Agent voll bedienbar (Kalender, Mail …),
   und der Assistent kann **von sich aus** asynchron melden.

## Kernentscheidung: deklarative Watch-Jobs statt LLM-generiertem Code

„Der Assistent schreibt sich ein Tool" lässt sich auf zwei Arten lesen:

- **(A) Wörtlich:** Das LLM generiert C#/Script-Code, der Container kompiliert und
  führt ihn aus.
- **(B) Sinngemäß:** Der Assistent **komponiert** aus geprüften, sandboxed
  Bausteinen (*Skills*) eine neue **deklarative Job-Spezifikation**, die der Agent
  fortan eigenständig ausführt.

Wir wählen **(B)** als Hauptweg. Begründung:

Variante (A) bedeutet **beliebige Code-Ausführung (RCE) genau in dem Prozess**, der
die sensibelsten Geheimnisse des Nutzers hält: Google-OAuth-Token, IMAP/SMTP-
Credentials, die WhatsApp-Session, die VAPID-Push-Keys und die komplette SQLite-DB
(`/app/data/nauassist.db`). Für einen **persönlichen, selbst-gehosteten** Assistenten
ist dieser Blast-Radius nicht vertretbar — ein halluzinierter oder
prompt-injizierter Codepfad (z.B. aus einer gefetchten Shop-Seite!) hätte vollen
Zugriff.

Variante (B) liefert denselben *gefühlten* Effekt — „ich sage etwas in Worten, und
NauAssist baut sich dafür ein dauerhaftes Werkzeug" — aber jeder ausführbare
Baustein ist vorab vom Entwickler vettet und sandboxed. Der Assistent erweitert
sich real (neue, benannte, persistente Hintergrund-Capabilities aus Freitext),
ohne dass je ungeprüfter Code läuft. Neue **Skill-Arten** (Bausteine) kommen über
die Zeit per Entwickler-PR dazu; der Assistent **kombiniert** sie frei.

> Eine bewusst eng begrenzte Variante (A) — sandboxed User-Script-Skill — ist als
> optionale, abgeschottete Phase 4 skizziert, falls die deklarativen Bausteine
> irgendwann nicht reichen. Sie ist **nicht** Teil des MVP.

## Was ist ein „Watch-Job"?

Ein **WatchJob** ist eine in SQLite persistierte, deklarative Job-Spezifikation:
„prüfe regelmäßig *Ziel X* über *Skill Y*, und wenn *Bedingung Z* erfüllt ist,
benachrichtige über *Kanäle K*." Das LLM erzeugt diese Spec aus dem Gespräch — das
ist das „selbstgeschriebene Tool".

```
WatchJob
  Id            (guid)
  UserId        (Multi-User-Trennung wie überall)
  Title         "Midea PortaSplit Verfügbarkeit"      ← LLM-generiert
  Goal          "Midea-PortaSplit-Klimaanlage wieder bestellbar/lieferbar"
  Kind          web_availability | web_change | web_search   (erweiterbar)
  SpecJson      { searchQueries[], targetUrls[], judgeQuestion, successCriteria }
  Schedule      { intervalSeconds, jitterSeconds, maxIntervalSeconds, quietHours? }
  Notify        { channels: [pushover, webpush], fireOnce: true }
  Budget        { maxChecks, maxLlmCalls, expiresAt }
  Status        active | paused | fired | completed | failed | expired
  Bookkeeping   lastCheckedAt, nextDueAt, checkCount, consecutiveErrors,
                lastResultJson, firedHash, createdAt
```

`Kind` wählt die **Skill-Pipeline** (siehe Executor); `SpecJson` parametrisiert sie.
Das Schema von `SpecJson` ist pro Kind fix und validiert — das LLM füllt nur
erlaubte Felder.

## Architektur — andockt an das Bestehende

Das Muster existiert bereits zweifach im Code und wird wiederverwendet:

- **Tool-Registry** (`ITool`, `AgentRunner`): der Chat-Agent legt Jobs über ein
  neues Tool an — strukturell wie `LookupFreeSlotsTool` & Co.
- **Hintergrund-Scheduler** (`AutonomousAgentScheduler : BackgroundService`):
  periodischer Tick, pro-User-DI-Scope, Audit-Log, Push am Ende — exakt die
  Vorlage für den `WatchJobScheduler`.
- **Push** (`WebPushSender`, VAPID): wird hinter eine Kanal-Abstraktion gezogen und
  um **Pushover** ergänzt.

```
┌──────────────────────── nauassist (.NET, bestehend) ───────────────────────────┐
│                                                                                 │
│  Chat-Agent  (AgentRunner + ITool[], SSE)          ──┐ läuft pro Chat-Turn      │
│    └─ NEU:  create_watch_job / list_watch_jobs /     │ (unverändert, blockt nie)│
│             cancel_watch_job / pause_watch_job  ─────┼──▶ WatchJobRepository     │
│                                                      │        (SQLite/Dapper)    │
│                                                      ▼                           │
│  NEU: WatchJobScheduler : BackgroundService   ── eigener, schneller Tick ───────│
│    ├─ wählt fällige Jobs (nextDueAt <= now), pro User ein DI-Scope               │
│    ├─ SemaphoreSlim: max. N parallele Executions                                │
│    └─ je Job ──▶ WatchJobExecutor                                               │
│                    1. Gather   IWebSearch + IWebFetch  (Such-Queries + URLs)     │
│                    2. Judge    LLM bewertet Evidenz vs. Goal → {met,conf,...}    │
│                    3. Confirm  optional 2. Quelle, bevor gefeuert wird           │
│                    4a. nicht erfüllt → nextDueAt = backoff(); persist            │
│                    4b. erfüllt       → NotificationDispatcher + proaktive Msg    │
│                                                                                 │
│  NEU: NotificationDispatcher                                                     │
│    ├─ WebPushSender  (bestehend, VAPID)                                          │
│    └─ PushoverSender (NEU)                                                       │
│                                                                                 │
│  NEU: /api/events (SSE)  ── server-initiierte Nachrichten an die offene PWA      │
│                                                                                 │
│  SQLite  /app/data/nauassist.db   (+ Tabelle watch_jobs)                         │
└─────────────────────────────────────────────────────────────────────────────────┘
        │ extern:  Ollama (Judge-LLM) · Web-Suche/-Fetch · Pushover-API · Web-Push
```

### Neue Bausteine im Überblick

| Komponente | Vorbild im Code | Aufgabe |
|---|---|---|
| `WatchJob` (Modell) | `Suggestion.cs` | Persistente Job-Spec |
| `WatchJobRepository` | `SuggestionRepository.cs` | Dapper-CRUD auf `watch_jobs` |
| `CreateWatchJobTool : ITool` | `LookupFreeSlotsTool.cs` | LLM legt Job an |
| `ListWatchJobsTool` / `CancelWatchJobTool` / `PauseWatchJobTool` | `ListRulesTool.cs` / `DeleteRuleTool.cs` | Verwaltung aus dem Chat |
| `WatchJobScheduler : BackgroundService` | `AutonomousAgentScheduler.cs` | Fällige Jobs ticken, parallel ausführen |
| `WatchJobExecutor` | `AutonomousReasoner.cs` | Gather → Judge → Fire-Pipeline |
| `IWebSearch` / `IWebFetch` | `ILlmClient` / `ILlmClientFactory` | Pluggable Web-Zugriff |
| `INotificationChannel` + `PushoverSender` | `WebPushSender.cs` | Mehrkanal-Benachrichtigung |
| `/api/events` SSE | `ChatEndpoints.cs` / `SseWriter.cs` | Asynchrone Server→Client-Nachrichten |

## Der Executor — „gründlich prüfen, sofort melden"

Pro fälligem Job führt `WatchJobExecutor` eine kleine, feste Pipeline aus. Das ist
die Stelle, an der „ausführlich und gründlich nach Verfügbarkeit prüfen" konkret
wird:

1. **Gather (mehrquellig).** Führe alle `searchQueries` über `IWebSearch` aus **und**
   fetche die bekannten `targetUrls` (Händler-Produktseiten) über `IWebFetch`.
   So hängt das Ergebnis nicht an einer einzelnen Quelle.
2. **Judge (LLM-Urteil mit Begründung).** Das LLM bekommt die gesammelte Evidenz
   plus `judgeQuestion`/`successCriteria` und liefert strukturiert:
   `{ met: bool, confidence: 0..1, evidence: [{shop, price, url, quote}], summary }`.
   Gefeuert wird nur über einer **Confidence-Schwelle** (analog zur 0,6-Schwelle des
   bestehenden autonomen Agenten) — das vermeidet „Cache sagt verfügbar, Seite sagt
   ausverkauft"-Fehlalarme.
3. **Confirm (optional, gegen Fehlalarme).** Bei `met=true` einen frischen
   Direkt-Fetch der genannten Quelle nachziehen, bevor benachrichtigt wird.
4. **Decide.**
   - *Nicht erfüllt:* `nextDueAt` neu setzen (adaptive Kadenz, s.u.), `lastResultJson`
     speichern, `checkCount++`. Bei Fehlern `consecutiveErrors++` mit Backoff.
   - *Erfüllt:* `firedHash` aus der Evidenz bilden (Idempotenz — nicht doppelt für
     dieselbe Beobachtung feuern) → **NotificationDispatcher** (Pushover + Web-Push)
     + **proaktive Chat-Nachricht** einspeisen. Bei `fireOnce=true` → `completed`;
     sonst Cooldown und weiterlaufen.

Jeder Check landet wie gehabt im **`audit_log`** (Pattern aus dem Scheduler), damit
nachvollziehbar bleibt, was der Assistent autonom getan hat.

### Realitäts-Check zur Kadenz („alle paar Sekunden")

Ein echtes Shop-System im Sekundentakt abzufragen führt zu **Rate-Limits/IP-Sperren
und unnötiger Last/Kosten**. Statt stur „alle paar Sekunden" eine **adaptive Kadenz**:

- **Untergrenze** konfigurierbar, default ~30–60 s; **Jitter** gegen Gleichtakt.
- **Conditional Fetch** (ETag/Last-Modified) — unverändert = quasi gratis.
- **Exponentielles Backoff** bei „keine Änderung", Reset bei Teil-Signalen.
- **Hot-Mode:** verdichtet auf ~10–15 s, sobald ein **Teilsignal** auftaucht (z.B.
  Suchindex zeigt Treffer, Produktseite aber noch „ausverkauft") — dann lohnt sich
  enges Pollen kurzzeitig.
- **Identifizierbarer User-Agent**, `robots.txt` respektieren.

So bleibt „sofort melden" erhalten (im relevanten Moment wird eng gepollt), ohne den
Watcher sinnvoll-sekündlich gegen die Wand laufen zu lassen. Die Untergrenze ist
einstellbar — aber mit Schutzschwelle.

## Parallelität & asynchrone Konversation

Die dritte Anforderung ist die wichtigste fürs Gefühl „der Assistent arbeitet
nebenher und redet mit mir asynchron":

- **Entkopplung vom Chat-Turn.** Watch-Jobs laufen **nicht** im SSE-Turn des
  Chats, sondern im eigenen `WatchJobScheduler`. Der Nutzer kann jederzeit weiter
  chatten (Termine, Mail, neue Watcher) — nichts blockiert. N Jobs laufen parallel
  (Semaphore-begrenzt).
- **Server-initiierte Nachrichten.** Heute ist der Chat reines Request/Response. Neu
  ist, dass der **Server von sich aus** eine Nachricht schickt. Lösung:
  1. Beim Feuern wird eine **proaktive Assistant-`Message`** in `MessageRepository`
     persistiert (taucht in der Chat-History auf — Pattern existiert).
  2. Ist die PWA offen, liefert ein schlanker **`/api/events`-SSE-Stream** die
     Nachricht + Job-Status-Updates **live** (wiederverwendbar `SseWriter`).
  3. Ist die App zu, greift **Web-Push + Pushover** (Deep-Link in den Chat).
- **Statusfenster.** Eine „Watcher"-UI-Section (analog zur Empfehlungs-Seite) zeigt
  laufende/erfüllte/pausierte Jobs mit letztem Befund — read-only Überblick,
  Aktionen (pausieren/stoppen) gehen ebenfalls über die Tools.

## Beispiel-Walkthrough (Midea PortaSplit)

1. **Chat:** „Bitte schaue, wann online wieder Midea-PortaSplit-Anlagen verfügbar
   sind, dann benachrichtige mich."
2. **Chat-Agent** erkennt die Intent und ruft `create_watch_job`:
   ```json
   {
     "title": "Midea PortaSplit Verfügbarkeit",
     "goal": "Midea-PortaSplit-Klimaanlage bei seriösem Händler bestellbar/lieferbar",
     "kind": "web_availability",
     "spec": {
       "searchQueries": ["Midea PortaSplit kaufen lieferbar",
                         "Midea Port-a-Split verfügbar Preis"],
       "targetUrls": [],
       "judgeQuestion": "Ist eine Midea-PortaSplit-Klimaanlage bei einem seriösen Händler aktuell bestellbar (nicht 'ausverkauft'/'nicht lieferbar')? Nenne Händler, Preis, Link.",
       "successCriteria": "Mindestens ein vertrauenswürdiger Shop zeigt 'auf Lager'/bestellbar."
     },
     "schedule": { "intervalSeconds": 60, "maxIntervalSeconds": 1800 },
     "notify": { "channels": ["pushover", "webpush"], "fireOnce": true },
     "budget": { "expiresAt": "2026-07-24T00:00:00Z" }
   }
   ```
   (Bei Bedarf fragt der Agent kurz zurück: „Bestimmte Händler bevorzugt?" — dann
   landen die als `targetUrls` in der Spec.)
3. **Sofortige, asynchrone Antwort:** „Mach ich. Ich prüfe das ab jetzt regelmäßig
   und melde mich per Push, sobald eine Anlage bestellbar ist. Du kannst
   zwischendurch ganz normal weiter mit mir reden." Der Chat ist sofort wieder frei.
4. **Hintergrund:** alle ~60 s Gather→Judge. Negativ → Backoff/weiter. Bei einem
   zuversichtlichen Treffer → 2. Quelle bestätigen → **feuern**: Pushover + Web-Push
   „🟢 Midea PortaSplit wieder verfügbar bei *Shop X* für *Y €* → [Link]", dazu eine
   proaktive Chat-Nachricht; Job → `completed`.

## Sicherheit & Guardrails

- **Feature-Flag/Opt-in** `AutonomousAgent:WatchJobs:Enabled` (gated DI-Registrierung
  + Endpoints + UI), wie das WhatsApp-Profil.
- **Globale Caps:** max. aktive Jobs/User, max. parallele Executions, je-Job-TTL
  (`expiresAt`), je-Job-Tagesbudget für LLM- und Fetch-Calls. Verhindert
  Kosten-/Last-Runaway.
- **Prompt-Injection-Härtung:** gefetchte Web-Inhalte sind **Daten, keine
  Instruktionen**. Der Judge-Prompt isoliert Fremdinhalt klar (wie der Hinweis zu
  `<untrusted_external_data>`); der Judge darf **nur** ein strukturiertes Urteil
  zurückgeben, nie Jobs anlegen/ändern oder Tools auslösen.
- **Idempotentes Feuern** über `firedHash` (keine Doppel-Benachrichtigung).
- **Domain-Allowlist** optional; `robots.txt` respektieren; identifizierbarer
  User-Agent.
- **Kill-Switch & Audit:** jeder Check im `audit_log`; ein Schalter stoppt alle
  Watcher.

## IWebSearch / IWebFetch — die eine echte neue Infrastruktur

Das Backend hat heute **keinen** Web-Zugriff (Ollama lokal kann nicht browsen). Es
braucht eine pluggable Abstraktion, analog zu `ILlmClientFactory`:

- **`IWebSearch`** — Optionen: selbst-gehostetes **SearXNG** (passt zum
  self-hosted-Ethos, keine API-Keys nach außen) **oder** ein API-Dienst wie
  **Tavily/Brave** (weniger Betrieb, dafür externer Key).
- **`IWebFetch`** — HTTP-Fetch (`AddHttpClient`, schon im Stack) + HTML→Text-
  Extraktion, Größen-/Timeout-Limits, conditional GET.

Empfehlung: SearXNG als Default (Privatsphäre, kein externer Key), Tavily/Brave als
einfache Alternative — die Wahl gehört in die Settings, wie LLM-Provider.

## Phasen-Vorschlag

- **Phase 1 (MVP, liefert das Midea-Beispiel):** `watch_jobs`-Tabelle + Repository,
  `WatchJobScheduler` + `WatchJobExecutor`, `IWebSearch`/`IWebFetch`, Judge-Schritt,
  `create_watch_job` + `list`/`cancel`, Kind `web_availability`, Benachrichtigung
  über bestehenden Web-Push.
- **Phase 2 (Pushover + Async-UX):** `PushoverSender` hinter `INotificationChannel`,
  proaktive Chat-Nachrichten, `/api/events`-SSE, Watcher-UI-Section, adaptive Kadenz
  + Hot-Mode.
- **Phase 3 (mehr Skills):** weitere Kinds — Preis-Schwelle, RSS/Feed,
  generisches `web_change` (Diff), Quiet-Hours.
- **Phase 4 (optional, abgeschottet):** sandboxed User-Script-Skill für Fälle, die
  die deklarativen Bausteine nicht abdecken — nur mit harten Sandbox-Grenzen
  (kein Dateisystem-/Secret-/Netz-Zugriff außer über vermittelte Skills).

## Offene Fragen für dich

1. **Web-Zugriff:** SearXNG selbst hosten (privat, kein Key) oder Tavily/Brave-API
   (weniger Betrieb)? Default-Empfehlung: SearXNG.
2. **Kadenz-Untergrenze:** hartes Minimum (z.B. 30 s) erzwingen oder frei
   konfigurierbar mit Warnung?
3. **Pushover** als gesetzter Zusatzkanal neben Web-Push — Account/Token vorhanden?
4. **Scope-Disziplin:** MVP bewusst auf `web_availability` beschränken (Midea-Fall)
   und erst danach weitere Skill-Kinds, einverstanden?
