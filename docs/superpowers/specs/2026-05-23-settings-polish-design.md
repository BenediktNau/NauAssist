# Settings-Polish: Mockup entfernen, Ollama- + Kalender-Konfiguration in die UI

## Ziel

Die Settings-Page soll auf die zwei real verdrahteten Bereiche reduziert werden:
**KI-Provider** und **Kalender**. Heute liegt ein Großteil der relevanten Konfiguration
in `appsettings.json` (Ollama-Host/-API-Key, Google-OAuth-Client-Secret als Datei,
Calendar-Defaults), was beim Container-Deployment unbequem ist und Secrets als Files
auf den Server zwingt. Diese Werte sollen in `app_settings` (SQLite) wandern und über
die UI editierbar sein. Mockup-Sektionen werden komplett entfernt.

## Scope

- `SettingsPage.tsx` reduzieren auf zwei Sektionen, Mockup-Helper-Komponenten löschen.
- Ollama: `Host`, `ApiKey`, `NumCtx`, `Temperature` in `app_settings`, in UI editierbar.
- Google-Calendar: `ClientId`, `ClientSecret`, `CalendarId`, `WorkingHoursStart`,
  `WorkingHoursEnd`, `DefaultDurationMinutes`, `SearchHorizonDays` in `app_settings`,
  in UI editierbar.
- OAuth-Flow aus UI startbar: Auth-URL anzeigen, Code-Input entgegennehmen, Token
  persistieren (Console-Code-Flow ohne Redirect-URI-Setup).
- Beim Ändern von `ClientId`/`ClientSecret`: bestehende Tokens in `google_oauth`
  automatisch löschen.
- Ollama-Host: optionaler "Verbindung testen"-Button (ruft `GET {Host}/api/tags`,
  Ollamas Standard-Endpoint zum Auflisten installierter Modelle).

Explizit **nicht** in diesem Scope:

- SystemPrompt, Timeouts, Gemini-Defaults aus `appsettings.json` migrieren — die
  bleiben Bootstrap-Defaults.
- Profil / Tonalität / Pufferzeit / Wochenende-Toggle / Darstellung / Shortcuts /
  Datenschutz — die Mockup-Sektionen werden gelöscht, nicht durch reale Features
  ersetzt.
- Vollwertiger Redirect-basierter OAuth-Flow (User Console-Code-Flow reicht).
- Multi-Account (mehrere Google-Kalender gleichzeitig).
- CLI-`auth`-Command entfernen — bleibt als Fallback.

## Datenmodell

Neue Migration `0006_settings_expansion.sql`:

```sql
INSERT INTO app_settings (key, value) VALUES
    ('ollama.host',              'http://localhost:11434'),
    ('ollama.api_key',           ''),
    ('ollama.num_ctx',           '16384'),
    ('ollama.temperature',       '0.3'),
    ('calendar.google.client_id',     ''),
    ('calendar.google.client_secret', ''),
    ('calendar.google.calendar_id',   'primary'),
    ('calendar.working_hours_start',  '09:00'),
    ('calendar.working_hours_end',    '18:00'),
    ('calendar.default_duration_min', '60'),
    ('calendar.search_horizon_days',  '14');
```

Werte sind `TEXT` (bestehendes Schema), numerische Konversion findet im Repository
statt. Defaults entsprechen den heutigen `appsettings.json`-Werten.

`appsettings.json`:
- `Ollama:Host`, `Ollama:ApiKey`, `Ollama:NumCtx`, `Ollama:Temperature` werden
  entfernt (zur Vermeidung von zwei Sources of Truth).
- `Ollama:SystemPrompt`, `Ollama:InitialTimeoutSeconds`, `Ollama:TokenTimeoutSeconds`,
  `Gemini:*`, `Agent:*`, `Persistence:*`, `Time:*` bleiben.
- `Calendar:`-Sektion wird komplett entfernt; `CalendarOptions.cs` wird gelöscht
  oder zu einem reinen Settings-DTO.

## Architektur

### Backend

**`IAppSettingsRepository` wird erweitert:**

```csharp
Task<OllamaUserSettings>   GetOllamaAsync(CancellationToken ct);
Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct);

Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct);
Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct);

Task<GoogleCredentials?>   GetGoogleCredentialsAsync(CancellationToken ct);
// Liefert null, wenn ClientId leer ist (= "nicht konfiguriert"), sonst gefülltes Record.
Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct);
// Löscht intern alle Einträge aus google_oauth in derselben Transaktion.
```

Neue Records in `Features/Settings/`:

- `OllamaUserSettings(string Host, string? ApiKey, int NumCtx, double Temperature)`
- `CalendarUserSettings(string CalendarId, TimeOnly WorkingHoursStart, TimeOnly WorkingHoursEnd, int DefaultDurationMinutes, int SearchHorizonDays)`
- `GoogleCredentials(string ClientId, string ClientSecret)` — leerer ClientId
  bedeutet "nicht konfiguriert".

**`LlmClientFactory.BuildOllama`:**
- Liest `Host`, `ApiKey`, `NumCtx`, `Temperature` aus `IAppSettingsRepository`.
- `SystemPrompt`, `InitialTimeoutSeconds`, `TokenTimeoutSeconds` bleiben aus
  `IOptions<OllamaOptions>`.

**`GoogleAuthService`:**
- Filesystem-Lesen entfernen. Stattdessen:
  ```csharp
  var creds = await _settings.GetGoogleCredentialsAsync(ct);
  if (creds is null || string.IsNullOrEmpty(creds.ClientId))
      throw new InvalidOperationException("Google-OAuth-Credentials nicht konfiguriert. Bitte in den Settings eintragen.");
  var clientSecrets = new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret };
  ```
- Für den UI-Flow wird `GoogleWebAuthorizationBroker` nicht mehr verwendet;
  stattdessen wird `AuthorizationCodeFlow` direkt instanziiert (siehe Auth-Flow
  unten). `ConsoleCodeReceiver` bleibt nur für den CLI-Pfad `-- auth` relevant.

**`FreeSlotCalculator`:**
- Heute Singleton mit gebauten `TimeOnly`-Werten aus `CalendarOptions`. Wird zu
  Scoped umgestellt; Factory liest `WorkingHoursStart`/`End` aus
  `IAppSettingsRepository` bei jedem Scope (= jeder HTTP-Request).

**`CalendarContextBuilder`:**
- `IOptions<CalendarOptions>` → DB-Lookup (`SearchHorizonDays`).

**Calendar-ID-Konsumenten** (`GoogleCalendarProvider`):
- Liest `CalendarId` aus DB statt Options.

**Neue Endpoints (`SettingsEndpoints.cs` + neuer `CalendarAuthEndpoints.cs`):**

```
GET  /api/settings/ollama        → { host, hasApiKey, numCtx, temperature }
PUT  /api/settings/ollama        ← { host, apiKey?: string | null, numCtx, temperature }

GET  /api/settings/calendar      → { calendarId, workingHoursStart, workingHoursEnd,
                                     defaultDurationMinutes, searchHorizonDays,
                                     hasGoogleCredentials, isConnected }
PUT  /api/settings/calendar      ← { ...allOf, googleClientId?: string,
                                     googleClientSecret?: string | null }

POST /api/calendar/auth/start    → { authUrl, sessionId }
POST /api/calendar/auth/complete ← { sessionId, code } → { ok }
POST /api/calendar/auth/disconnect → { ok }   // löscht google_oauth-Tokens

POST /api/settings/ollama/test   ← { host, apiKey?: string }
                                 → { ok, models?: string[], error?: string }
```

`apiKey` / `googleClientSecret` folgen demselben Tri-State-Pattern wie heute
`geminiApiKey`: `null` = unchanged, `""` = clear, sonst = set. `hasApiKey` /
`hasGoogleCredentials` werden im GET zurückgegeben statt Klartext.

### Auth-Flow im Detail

Da der Console-Code-Flow nicht passt zu einem stateless HTTP-Backend, wird er
nachgebaut, **ohne** `GoogleWebAuthorizationBroker`:

1. **Start** (`POST /api/calendar/auth/start`):
   - Server baut `AuthorizationCodeFlow` mit den DB-Credentials und
     `RedirectUri = "http://localhost"` (Out-of-Band/Pseudo, akzeptiert von
     Google für Desktop-Clients).
   - Generiert eine `sessionId` (GUID), speichert in einem `MemoryCache` mit
     5min TTL: `{ flowInstance, codeVerifier? }`.
   - Liefert `authUrl` (= `flow.CreateAuthorizationCodeRequest(redirectUri).Build()`)
     + `sessionId` an UI.

2. **Complete** (`POST /api/calendar/auth/complete`):
   - UI sendet `sessionId` + `code`.
   - Server holt Flow aus Cache, `flow.ExchangeCodeForTokenAsync(userId, code, redirectUri, ct)`
     → speichert in `SqliteDataStore` unter `userId = "nauassist-default"`.
   - Cache-Eintrag löschen.

3. **Status-Check** (im `GET /api/settings/calendar`):
   - `isConnected = true` wenn `SqliteDataStore` einen Eintrag für
     `nauassist-default` enthält. Implementierungsdetail: leise ein
     `GetAsync<TokenResponse>("nauassist-default")` und prüfen, ob nicht-null.

4. **Disconnect** (`POST /api/calendar/auth/disconnect`):
   - `SqliteDataStore.ClearAsync()`.

`GoogleAuthService.GetCredentialAsync` (vom existierenden CalendarProvider
verwendet) wird umgebaut: lädt nur noch Tokens aus `SqliteDataStore` und
refreshed. Wenn nichts da → wirft `NotAuthenticatedException`, die der Caller
sauber als 401 / Hinweis "bitte verbinden" weiterreichen kann.

Der bestehende CLI-Pfad (`-- auth`) bleibt funktional, baut intern auf demselben
Flow auf, aber nutzt `ConsoleCodeReceiver` für den Code-Empfang (falls man ohne
laufendes Frontend autorisieren will).

### Frontend

**Dateien gelöscht:**
- Helper-Components in `SettingsPage.tsx`: `Toggle`, `SegRadio`, `Stepper`,
  `ColorSwatchRow`, `CalRow`, `TxtField` (falls nicht mehr verwendet).
- Komplette Mockup-Sektionen 01 Profil, 02 Kalender (alt, CalRow-basiert),
  04 Darstellung, 05 Shortcuts, 06 Datenschutz, plus AI-Verhalten ab "Tonalität"
  und die `// MOCKUP — NOCH NICHT VERDRAHTET`-Trennlinie.
- `shortcuts`-Array, `navItems`-Array umbauen.

**Neue Struktur `SettingsPage.tsx`:**

```
┌─── Aside (260px) ───────┐  ┌─── Main (max 980px) ──────────────┐
│  N  NauAssist           │  │ — EINSTELLUNGEN —                 │
│                         │  │ Provider & Kalender.              │
│  // EINSTELLUNGEN       │  │                                   │
│                         │  │ ── 01 · KI-PROVIDER ──            │
│  01 KI-PROVIDER  ●      │  │   AI-Provider [Ollama|Gemini]     │
│  02 KALENDER            │  │   Modell      [select]            │
│                         │  │   ▼ Erweiterte Ollama-Einstellungen
│                         │  │     Host [____] [TESTEN]          │
│                         │  │     API-Key [____]                │
│                         │  │     NumCtx [____] Temperature [_] │
│                         │  │   Gemini API-Key [...]            │
│                         │  │   [SPEICHERN]   (sticky)          │
│                         │  │                                   │
│  // STATUS              │  │ ── 02 · KALENDER ──               │
│  v0.4 · build_2147      │  │   Status: ● VERBUNDEN / ○ NICHT…  │
│  …                      │  │   Google Client-ID   [____]       │
│                         │  │   Google Client-Secret [____]     │
│                         │  │   Calendar-ID  [primary]          │
│                         │  │   Arbeitszeiten 09:00 → 18:00     │
│                         │  │   Standard-Dauer 60 MIN           │
│                         │  │   Suchzeitraum   14 TAGE          │
│                         │  │   [SPEICHERN]                     │
│                         │  │   [MIT GOOGLE VERBINDEN] / [TRENN.]│
└─────────────────────────┘  └───────────────────────────────────┘
```

- Linke Nav: zwei Einträge, Klick = smooth-scroll zur Section.
- "Erweiterte Ollama-Einstellungen" ist ein Disclosure-Toggle (Details-Element
  oder eigenes Toggle mit `▶ / ▼`).
- Provider und Modell speichern weiterhin **auto-on-change** (bestehendes UX).
- Alle anderen Felder (Host, ApiKey, NumCtx, Temperature, Calendar-Felder)
  verwenden lokalen Draft-State + expliziten "Speichern"-Button. Vor dem
  Speichern: Button disabled wenn nichts dirty.
- "Mit Google verbinden":
  1. Klick → `POST /auth/start` → Modal/Inline-Card zeigt URL als kopierbaren
     Block + Anleitung ("Öffne diese URL, klicke Erlauben, kopiere `code=` aus
     der Adresszeile").
  2. Input-Feld + Button "Code übermitteln" → `POST /auth/complete`.
  3. Erfolg: Status flippt auf "● VERBUNDEN", Card schließt.
- "Verbindung testen" (Ollama): Button neben Host-Feld, ruft `POST /ollama/test`
  mit dem **aktuellen Draft-Wert** (nicht dem gespeicherten), zeigt
  Inline-Feedback (`// ERREICHBAR · 5 MODELLE` oder `// FEHLER: …`).

**`api/settings.ts`:**
- Bestehende `getLlmSettings` / `updateLlmSettings` bleiben.
- Neu: `getOllamaSettings`, `updateOllamaSettings`, `testOllama`,
  `getCalendarSettings`, `updateCalendarSettings`, `startGoogleAuth`,
  `completeGoogleAuth`, `disconnectGoogle`.

## Daten-Fluss

**Settings-Speichern (Ollama-Beispiel):**
```
UI(SettingsPage) ──PUT /api/settings/ollama──► SettingsEndpoints
                                                  │
                                                  ▼
                                          UpdateOllamaHandler
                                                  │
                                                  ▼
                                          AppSettingsRepository.SetOllamaAsync
                                                  │
                                                  ▼ (SQLite UPSERT je key)
                                              app_settings
```

**Chat mit neuem Ollama-Host:**
```
Chat-Request → DI-Scope → LlmClientFactory.CreateAsync
                              │
                              ▼
                       Liest aktuelle settings aus AppSettingsRepository
                              │
                              ▼
                       Baut HttpClient mit BaseAddress = host + /v1/
```

Da `ILlmClient` Scoped ist, gilt der neue Host ab dem nächsten Request — kein
Server-Restart nötig.

**OAuth-Flow:**
```
[1] User klickt "Verbinden"
    UI ──POST /auth/start──► Server
                              │
                              ▼ baut Flow mit DB-Credentials, cached unter sessionId
    UI ◄────{authUrl, sessionId}── Server

[2] User öffnet authUrl im Browser, Google → Redirect auf http://localhost?code=…
    User kopiert code in UI-Feld

[3] UI ──POST /auth/complete──► Server
                                  │
                                  ▼ Flow.ExchangeCodeForTokenAsync
                                  │
                                  ▼ SqliteDataStore.StoreAsync(…)
                                  │
                                  ▼ Cache.Remove(sessionId)
    UI ◄────{ok: true}── Server  → reload calendar-status
```

## Fehlerbehandlung

- **Ollama-Host ungültig (nicht-URL):** Validierung serverseitig in
  `UpdateOllamaHandler` (`Uri.TryCreate`). 400 mit Inline-Fehler.
- **NumCtx negativ / Temperature außerhalb [0, 2]:** 400 mit Hinweis.
- **Test-Verbindung fehlgeschlagen:** Endpoint liefert `{ ok: false, error }`,
  UI rendert als Mono-Text. Keine 5xx.
- **Auth-Flow abgelaufen (sessionId nicht im Cache):** 410 Gone, UI zeigt
  "Sitzung abgelaufen, bitte neu starten".
- **Code falsch / abgelaufen:** Google-Exception fängt Endpoint ab, liefert 400
  mit "Code ungültig — Bitte erneut autorisieren".
- **Calendar-Aufruf ohne Tokens:** `GoogleAuthService` wirft
  `NotAuthenticatedException`, `GoogleCalendarProvider` reicht durch, Chat-Tool
  liefert sauberen Hinweis "Bitte Kalender in Einstellungen verbinden".
- **Migration auf bestehender DB:** Default-Werte werden via
  `INSERT … ON CONFLICT(key) DO NOTHING` eingespielt — keine Overwrites
  existierender Werte.

## Tests

- `AppSettingsRepository` Unit-Tests für die neuen Get/Set-Methoden, inkl.
  Typkonversion (TimeOnly, int, double).
- `SetGoogleCredentialsAsync` löscht `google_oauth` in derselben Transaktion —
  Integration-Test über AppDb-Fixture.
- `UpdateOllamaHandler` validiert Host-URL, NumCtx-Range, Temperature-Range.
- `LlmClientFactory.BuildOllama` zieht Host/ApiKey aus DB (Mock-Repository,
  Assert auf `http.BaseAddress`).
- `FreeSlotCalculator` wird scoped — Smoke-Test, dass Test-Suite-Refactor
  durchgeht (Konstruktor-Signatur evtl. neu).
- Backend-Test für `/api/calendar/auth/start` mit `MemoryCache`, prüft dass
  sessionId 5min lebt.

## Migrationspfad

1. **Migration 0006** `INSERT … ON CONFLICT … DO NOTHING` mit den heutigen
   `appsettings.json`-Defaults. Frische DB ⇒ funktioniert sofort.
2. **`appsettings.json`-Cleanup**: `Calendar:`-Sektion komplett raus, aus
   `Ollama:` die Felder `Host`, `ApiKey`, `NumCtx`, `Temperature` entfernen.
3. **`Dockerfile`-Cleanup**: `ENV Calendar__GoogleCredentialsPath=…` entfernen.
   Doku/README-Hinweis: "Beim ersten Start die Settings-Page öffnen und
   Google-Credentials eintragen."
4. **`appsettings.Development.json` / `bin/`-Kopien**: ggf. analog säubern, sind
   aber unkritisch (überschreiben Bootstrap-Defaults nur lokal).
5. **Bestandsuser**: Wer die App schon laufen hat, muss einmalig in den
   Settings die Werte hinterlegen — heutige `google-credentials.json` wird
   ignoriert. Im Spec-Doku oder Migration-Log dokumentieren.
