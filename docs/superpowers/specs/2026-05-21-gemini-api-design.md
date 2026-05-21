# Gemini API Anbindung mit UI-Provider-Switch

## Ziel

NauAssist soll neben Ollama (lokal) auch die Gemini API (Google AI Studio) als LLM-Provider unterstützen. Der Wechsel zwischen Providern und die Modellauswahl erfolgen zur Laufzeit über die SettingsPage — ohne Backend-Restart. Der API-Key wird in der lokalen SQLite-DB gespeichert.

## Scope (schmaler Pfad)

- Neue Persistenz-Tabelle `app_settings` (generisches key/value) für die LLM-Settings.
- Refactor `OllamaLlmClient` → `OpenAICompatibleLlmClient` (parametrisiert: Endpoint, Auth, Model, Options).
- Neue `LlmClientFactory`, die pro Request den aktuell konfigurierten Provider baut.
- Neue Endpoints `GET /api/settings/llm` und `PUT /api/settings/llm`.
- SettingsPage bekommt eine echte Sektion für Provider/Modell/API-Key. Der Rest der SettingsPage bleibt UI-Mockup.

Explizit **nicht** in diesem Scope: Profil-Daten, Kalender-Liste, Tonalität/Standard-Dauer, Darstellung, Datenschutz — diese Mockup-Sektionen bleiben Mockup.

## Architektur

```
Settings-Layer (neu)        LLM-Layer (refactor)            API-Layer (neu)
─────────────────────       ──────────────────────          ─────────────────
app_settings (DB)   ◄────┐  OpenAICompatibleLlmClient       GET  /api/settings/llm
AppSettingsRepository    │  (rename + parametrisiert        PUT  /api/settings/llm
LlmSettings (Record)     │   aus OllamaLlmClient)
                         │
                         └─ LlmClientFactory (Scoped)
                            └─ liest pro Request Settings
                               aus DB, baut konfigurierten
                               Client (Ollama oder Gemini)
```

`ILlmClient` wird im DI als **Scoped** mit Factory-Lambda registriert. Bei jeder Chat-Anfrage liest die Factory den aktuellen Provider aus der DB → Hot-Reload ohne Restart. `AgentRunner` und alle anderen Konsumenten bleiben unverändert.

## Datenmodell

### Migration `0005_app_settings.sql`

```sql
CREATE TABLE app_settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT INTO app_settings (key, value) VALUES
    ('llm.provider', 'ollama'),
    ('llm.ollama.model', 'gemma4:26b'),
    ('llm.gemini.model', 'gemini-2.5-flash'),
    ('llm.gemini.api_key', '');
```

Generisches key/value statt typisierter Spalten, damit spätere Settings (Tonalität, etc.) ohne neue Migration anhängbar sind.

### C#-Record

```csharp
public sealed record LlmSettings(
    string Provider,        // "ollama" | "gemini"
    string OllamaModel,
    string GeminiModel,
    string? GeminiApiKey);  // null bedeutet "noch nicht gesetzt"
```

### Repository

```csharp
public interface IAppSettingsRepository {
    LlmSettings GetLlm();
    void SetLlm(LlmSettings settings);
}
```

`AppSettingsRepository` als Scoped-Service, nutzt `AppDb` direkt (konsistent zu `RuleRepository`, `MessageRepository`). Bei `GetLlm` werden alle vier Keys in einem Query gelesen. `SetLlm` schreibt sie in einer Transaktion.

**DB-zu-Record-Mapping**: Die DB-Spalte `value` ist `NOT NULL`. Beim Lesen wird ein leerer String für `llm.gemini.api_key` zu `null` im Record gemappt (`value == "" ? null : value`). Beim Schreiben wird `null` zu `""` (DB-NULL ist verboten, leerer String ist das Sentinel für "kein Key").

### DB-Permissions-Härtung

In `DbInitializer.Initialize()` **bei jedem Start** (idempotent, deckt neue UND bestehende DBs ab) auf Linux:

```csharp
if (OperatingSystem.IsLinux() && File.Exists(dbPath))
    File.SetUnixFileMode(dbPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
```

Setzt `chmod 0600`. Bei nicht-Linux-Systemen no-op. Bestandsinstallationen mit `0644` werden beim nächsten Start automatisch gehärtet.

## LLM-Layer

### Refactor: `OpenAICompatibleLlmClient`

`OllamaLlmClient` wird umbenannt und parametrisiert. Payload-Building bleibt im OpenAI-Schema (`/v1/chat/completions`). Einzige Änderung: der Ollama-spezifische `options`-Block (mit `num_ctx`) wird nur angehängt, wenn `NumCtx != null`.

```csharp
public sealed class OpenAICompatibleLlmClient : ILlmClient
{
    public OpenAICompatibleLlmClient(
        HttpClient http,                          // BaseAddress + Auth-Header von Factory gesetzt
        OpenAICompatibleLlmOptions options,
        ILogger<OpenAICompatibleLlmClient> logger) { ... }
}

public sealed record OpenAICompatibleLlmOptions(
    string Model,
    int InitialTimeoutSeconds,
    int TokenTimeoutSeconds,
    string? SystemPrompt,
    double? Temperature,
    int? NumCtx);            // Ollama-only; bei Gemini null → wird im Payload weggelassen
```

### Factory

```csharp
public interface ILlmClientFactory {
    ILlmClient Create();
}

public sealed class LlmClientFactory : ILlmClientFactory
{
    public LlmClientFactory(
        IHttpClientFactory httpFactory,
        IAppSettingsRepository settings,
        IOptions<OllamaOptions> ollamaDefaults,
        IOptions<GeminiOptions> geminiDefaults,
        ILoggerFactory loggerFactory) { ... }

    public ILlmClient Create()
    {
        var s = _settings.GetLlm();
        return s.Provider switch
        {
            "ollama" => BuildOllama(s),
            "gemini" => BuildGemini(s),
            _ => throw new InvalidOperationException($"Unknown provider: {s.Provider}")
        };
    }
}
```

- `BuildOllama`: HttpClient mit `OllamaOptions.Host`, keine Auth-Header, `NumCtx` aus Defaults.
- `BuildGemini`: HttpClient mit `https://generativelanguage.googleapis.com/v1beta/openai/`, `Authorization: Bearer <key>` aus DB, `NumCtx = null`.
- Gemini gewählt, aber Key leer → `InvalidOperationException("Gemini-Provider aktiviert, aber kein API-Key konfiguriert")`.

### Neue Options-Klasse

```csharp
public sealed class GeminiOptions
{
    public int InitialTimeoutSeconds { get; set; } = 60;
    public int TokenTimeoutSeconds { get; set; } = 30;
    public string? SystemPrompt { get; set; }    // bewusst nullable: Default = OllamaOptions.SystemPrompt
    public double? Temperature { get; set; } = 0.3;
}
```

`SystemPrompt` wird in der ersten Iteration nicht in `appsettings.json` für Gemini dupliziert; die Factory liest fallback-mäßig den Ollama-SystemPrompt. Begründung: System-Prompt ist Modell-agnostisch.

### DI-Wiring (ersetzt `Program.cs:62-66`)

```csharp
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddHttpClient("Ollama");
builder.Services.AddHttpClient("Gemini");
builder.Services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
builder.Services.AddScoped<ILlmClientFactory, LlmClientFactory>();
builder.Services.AddScoped<ILlmClient>(sp => sp.GetRequiredService<ILlmClientFactory>().Create());
```

## API-Endpoints

Neue Route-Gruppe `SettingsEndpoints.MapSettingsEndpoints(app)` (analog zu `ChatEndpoints`).

### `GET /api/settings/llm`

Response:
```json
{
  "provider": "ollama",
  "ollamaModel": "gemma4:26b",
  "geminiModel": "gemini-2.5-flash",
  "hasGeminiApiKey": false
}
```

Der API-Key geht **nie** zum Client zurück.

### `PUT /api/settings/llm`

Request:
```json
{
  "provider": "gemini",
  "ollamaModel": "gemma4:26b",
  "geminiModel": "gemini-2.5-flash",
  "geminiApiKey": "AIza..."
}
```

Drei Fälle für `geminiApiKey`:
- `null` / Feld fehlt → Bestand bleibt.
- `""` (leer) → Key löschen.
- nicht-leer → Key überschreiben.

### Validierung

- `provider` muss `"ollama"` oder `"gemini"` sein → sonst 400.
- Modell-Namen non-empty → sonst 400.
- Modell-Namen müssen in der hardcoded Whitelist enthalten sein (siehe Frontend-Sektion) → sonst 400.
- Provider=`"gemini"` UND nach Update kein Key → 400 mit "Gemini benötigt einen API-Key — bitte eintragen, bevor du wechselst."

### Mediator-Pattern

- `GetLlmSettingsRequest` / `GetLlmSettingsHandler`
- `UpdateLlmSettingsRequest` / `UpdateLlmSettingsHandler`

Konsistent zum Rest des Codes.

### Audit-Log

Provider- und Modell-Wechsel werden in `AuditLogRepository` geschrieben (ohne Key-Inhalt, nur Event-Typ + neue Werte). Hilft beim Debuggen von "warum hat das LLM plötzlich anders geantwortet".

## Frontend

### Neue Sektion in SettingsPage

Sektion "03 · AI · Verhalten" wird in zwei sichtbar getrennte Gruppen aufgeteilt:
- **Oben (neu, echt)**: Provider / Modell / API-Key.
- **Unten (bestehend, Mockup)**: Tonalität, Standard-Dauer, etc.

Eine zweite SectionHead-Sublabel-Linie trennt sichtbar Mockup von Realität.

### Drei Rows

```
Row 1 — "AI-Provider"
  hint: "Welche AI Nau für seine Antworten nutzt."
  control: SegRadio  [ OLLAMA (LOKAL)  |  GEMINI (CLOUD) ]

Row 2 — "Modell"
  hint (provider-abhängig): "Welches Modell verwendet wird."
  control: select-Dropdown
    - bei Ollama:  gemma4:26b | qwen2.5:7b-instruct | llama3.2:3b
    - bei Gemini:  gemini-2.5-flash | gemini-2.5-pro

Row 3 — "Gemini API-Key"   (nur sichtbar wenn provider == "gemini")
  hint: "Wird sicher lokal gespeichert. Hol dir einen Key bei aistudio.google.com."
  control:
    - Falls hasGeminiApiKey=false: leeres Input-Feld (placeholder "AIza...") + Save-Button "ÜBERNEHMEN ↵"
    - Falls hasGeminiApiKey=true: Anzeige "•••••••••• GESPEICHERT" + "ÄNDERN"-Button (öffnet Eingabe) + "ENTFERNEN"-Button (setzt geminiApiKey="")
```

### Modell-Whitelists

Hardcoded — keine Live-Probe via `/api/tags`. YAGNI für jetzt.

- Ollama: `["gemma4:26b", "qwen2.5:7b-instruct", "llama3.2:3b"]`
- Gemini: `["gemini-2.5-flash", "gemini-2.5-pro"]`

Whitelist wird sowohl im Frontend (Dropdown) als auch im Backend (Validierung) hardcoded — zwei separate Listen, doppelte Pflege akzeptiert für Single-User-Hobbyprojekt.

### State

```tsx
const [settings, setSettings] = useState<LlmSettings | null>(null);
const [draftKey, setDraftKey] = useState<string | null>(null);
const [saving, setSaving] = useState(false);
const [error, setError] = useState<string | null>(null);

useEffect(() => { fetch('/api/settings/llm').then(...).then(setSettings); }, []);
```

Kein React-Query / Zustand-Library — Fetch + useState reicht, konsistent zur aktuellen Frontend-Schlichtheit (keine globalen State-Libs im Codebase).

### UX-Feedback

Nach erfolgreichem `PUT`: dezenter Mono-Hinweis unter den Buttons:
```
// PROVIDER AKTUALISIERT — WIRD AB DEINER NÄCHSTEN NACHRICHT GENUTZT
```

Bei Fehler: rote Variante mit Server-Message:
```
// FEHLER: <message>
```

Keine Toasts.

### Aufräumen am Mockup

Footer-Text `// ALLE ÄNDERUNGEN WERDEN AUTOMATISCH GESPEICHERT` wird entfernt (irreführend, solange nur eine Sektion echt ist).

## Error-Handling

### Backend

| Fehlerfall | Handling |
|---|---|
| Gemini gewählt + Key leer | `LlmClientFactory.Create()` wirft `InvalidOperationException`. ChatEndpoint fängt und mappt auf SSE-Event `{type: "error", message: "..."}` → Frontend zeigt das im Chat. |
| Gemini-API: 401/403 | `EnsureSuccessStatusCode()` wirft `HttpRequestException`. AgentRunner-Catch wrappt zu "Gemini lehnt den Key ab — bitte in den Einstellungen prüfen." |
| Gemini-API: 429 (Rate Limit) | Gleiche Catch-Stelle, Meldung "Gemini-Quota erschöpft, später erneut versuchen." |
| Gemini-API: Tool-Call-Format weicht ab | Bestehende `JsonException`-Catches im Tool-Call-Buffer (`OpenAICompatibleLlmClient`) loggen Warning und überspringen — User sieht das als "Antwort ohne Aktion". Falls häufig: später nativen Endpoint erwägen. |
| DB-Schreibfehler im PUT-Endpoint | Existing Mediator-Pipeline-Error-Handling → 500. |
| Settings-Tabelle leer | Migration läuft beim Startup (`DbInitializer.Initialize()`) — Default-Werte aus dem `INSERT`. |

### Frontend

- `GET /api/settings/llm` failed → Page zeigt eine einzelne Mono-Zeile `// SETTINGS NICHT LADBAR — BACKEND OFFLINE?` statt der Sektion-Inhalte.
- `PUT` failed → roter Hinweis unter Buttons mit Server-Message.

## Tests

### Backend (`src/Backend.Tests/`)

| Test | Was es prüft |
|---|---|
| `AppSettingsRepositoryTests` | Roundtrip Get/Set, Default-Werte aus Migration, leerer API-Key vs. null |
| `LlmClientFactoryTests` | Ollama-Build (kein Auth-Header), Gemini-Build (Bearer-Header, BaseAddress), Gemini-ohne-Key wirft, unbekannter Provider wirft |
| `SettingsEndpointsTests` | GET liefert `hasGeminiApiKey` korrekt, PUT mit `geminiApiKey=null` lässt Key intakt, PUT mit `""` löscht, PUT mit Provider=gemini ohne Key liefert 400, ungültiges Modell liefert 400 |
| `OpenAICompatibleLlmClientTests` | Bestehende Ollama-Tests umbenennen + erweitern: Payload enthält `options.num_ctx` nur wenn gesetzt. Wir mocken keinen echten Gemini-Endpoint — wir prüfen nur, dass die Payload das richtige OpenAI-Schema sendet. |

Kein Integrations-Test gegen den echten Gemini-Endpoint. Manuelle Smoke-Verifikation (siehe unten) ist Pflicht vor Merge.

### Frontend

Keine neuen Tests — Status Quo des Repos hat keine Frontend-Test-Infrastruktur. Manuelle Smoke-Tests reichen.

### Manuelle Verifikation

1. Mit Ollama-Provider (Default): bestehendes Verhalten funktioniert weiter (Regression-Check, gleicher Slot-Vorschlag-Flow wie bisher).
2. Provider auf Gemini umschalten ohne Key → klare Fehlermeldung im Chat.
3. API-Key aus AI Studio holen, eintragen, Chat-Anfrage senden → Antwort kommt von Gemini.
4. Tool-Call testen: Termin-Anfrage stellen, prüfen ob `lookup_free_slots` + `present_proposals` + `create_event` korrekt durchlaufen.
5. Provider zurück auf Ollama → läuft wieder lokal.

## Migrationsstrategie

- Bei Bestandsinstallation läuft Migration 0005 beim ersten Start und seedet Ollama als Default-Provider mit dem aktuellen Modell `gemma4:26b`. Existierendes Verhalten ändert sich nicht.
- Nach diesem Plan wird die `Ollama`-Sektion in `appsettings.json` zum **Default-Container** für Host/Timeouts/NumCtx/Temperature/SystemPrompt — das Modell wird aber nicht mehr von dort gelesen, sondern aus der DB.

## Architektur-Erweiterungspunkte (nicht in diesem Scope)

- `IApiKeyStore` als optionale spätere Abstraktion über die Key-Persistenz, falls man auf libsecret/OS-Keyring umstellen will.
- `app_settings`-Tabelle für weitere Mockup-Sektionen (Tonalität, Standard-Dauer, etc.) wiederverwenden, sobald diese real werden sollen.
- Live-Modellprobe via Ollama `/api/tags` für Dropdown — wenn die hardcoded Liste lästig wird.
