# Plan H: Gemini API Anbindung — Implementierungsplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Neben Ollama kann NauAssist die Gemini API (Google AI Studio) als LLM-Provider nutzen. Provider, Modell und API-Key sind zur Laufzeit über die SettingsPage umschaltbar — ohne Backend-Restart.

**Architecture:** Neue `app_settings`-Tabelle (key/value) persistiert die LLM-Konfiguration. `OllamaLlmClient` wird zu `OpenAICompatibleLlmClient` parametrisiert. Eine `LlmClientFactory` baut pro Request den konfigurierten Client (Ollama oder Gemini gegen den OpenAI-Compat-Endpoint von Google AI Studio). Settings-Endpoint (`GET`/`PUT /api/settings/llm`) ist über Mediator gewired, der API-Key bleibt server-seitig (nur `hasGeminiApiKey`-Boolean nach außen). Frontend bekommt eine echte Sektion in der bisherigen Mockup-`SettingsPage`.

**Tech Stack:** .NET 10, xUnit + FluentAssertions, Mediator, Dapper/SQLite, React + Tailwind (Vite).

**Referenz-Spec:** `docs/superpowers/specs/2026-05-21-gemini-api-design.md`

---

## File Structure

**Neu erstellt — Backend:**
- `src/Backend/Features/Infrastructure/Persistence/Migrations/0005_app_settings.sql`
- `src/Backend/Features/Settings/LlmSettings.cs` — Record + Provider-Konstanten.
- `src/Backend/Features/Settings/IAppSettingsRepository.cs`
- `src/Backend/Features/Settings/AppSettingsRepository.cs`
- `src/Backend/Features/Settings/GetLlmSettings/GetLlmSettingsRequest.cs`
- `src/Backend/Features/Settings/GetLlmSettings/GetLlmSettingsHandler.cs`
- `src/Backend/Features/Settings/UpdateLlmSettings/UpdateLlmSettingsRequest.cs`
- `src/Backend/Features/Settings/UpdateLlmSettings/UpdateLlmSettingsHandler.cs`
- `src/Backend/Features/Settings/SupportedModels.cs` — hardcoded Whitelist (Backend-Seite).
- `src/Backend/Endpoints/SettingsEndpoints.cs`
- `src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmClient.cs` (Umbenennung + Refactor)
- `src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmOptions.cs`
- `src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs`
- `src/Backend/Features/Infrastructure/Llm/Gemini/GeminiOptions.cs`

**Modifiziert — Backend:**
- `src/Backend/Features/Infrastructure/Persistence/AppDb.cs` — `DatabasePath`-Property exponieren für Permission-Härtung.
- `src/Backend/Features/Infrastructure/Persistence/DbInitializer.cs` — `chmod 0600` nach Migrationen.
- `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs` — `Model` bleibt als Default-Wert für Migration; bleibt sonst unverändert.
- `src/Backend/Program.cs` — DI-Wiring: `IHttpClientFactory`-named-clients + Factory-basierte Auflösung von `ILlmClient`.
- `src/Backend/appsettings.json` — neue Sektion `Gemini`, `Ollama.Model` Default bleibt für Seed.

**Gelöscht — Backend:**
- `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaLlmClient.cs` (ersetzt durch `OpenAICompatibleLlmClient`).

**Neu erstellt — Tests:**
- `src/Backend.Tests/Features/Settings/AppSettingsRepositoryTests.cs`
- `src/Backend.Tests/Features/Settings/GetLlmSettingsHandlerTests.cs`
- `src/Backend.Tests/Features/Settings/UpdateLlmSettingsHandlerTests.cs`
- `src/Backend.Tests/Features/Infrastructure/Llm/OpenAICompatibleLlmClientTests.cs`
- `src/Backend.Tests/Features/Infrastructure/Llm/LlmClientFactoryTests.cs`
- `src/Backend.Tests/Endpoints/SettingsEndpointsTests.cs`

**Neu erstellt — Frontend:**
- `frontend/src/api/settings.ts` — Fetch-Funktionen + Typen.

**Modifiziert — Frontend:**
- `frontend/src/components/pages/SettingsPage.tsx` — neue LLM-Sektion + State.

---

## Task 1: Migration 0005 + AppDb-Permissions-Härtung

**Files:**
- Create: `src/Backend/Features/Infrastructure/Persistence/Migrations/0005_app_settings.sql`
- Modify: `src/Backend/Features/Infrastructure/Persistence/AppDb.cs`
- Modify: `src/Backend/Features/Infrastructure/Persistence/DbInitializer.cs`

- [ ] **Step 1: Failing-Test — Migration-Auswirkung in `DbInitializerTests` ergänzen**

`src/Backend.Tests/Infrastructure/DbInitializerTests.cs` öffnen und folgenden Test anhängen (vor schließender `}` der Klasse):

```csharp
[Fact]
public void Initialize_CreatesAppSettingsTable_WithSeedValues()
{
    using var db = new TempSqliteDb();

    using var conn = db.AppDb.OpenConnection();
    var rows = conn.Query<(string Key, string Value)>(
        "SELECT key, value FROM app_settings ORDER BY key;").ToList();

    rows.Should().Contain(r => r.Key == "llm.provider" && r.Value == "ollama");
    rows.Should().Contain(r => r.Key == "llm.ollama.model" && r.Value == "gemma4:26b");
    rows.Should().Contain(r => r.Key == "llm.gemini.model" && r.Value == "gemini-2.5-flash");
    rows.Should().Contain(r => r.Key == "llm.gemini.api_key" && r.Value == "");
}
```

Imports oben in der Datei sicherstellen: `using Dapper;`, `using FluentAssertions;`, `using NauAssist.Backend.Tests.Helpers;`.

- [ ] **Step 2: Test laufen lassen — soll fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~DbInitializerTests.Initialize_CreatesAppSettingsTable"`
Expected: FAIL — Tabelle `app_settings` existiert nicht.

- [ ] **Step 3: Migration-Datei erstellen**

`src/Backend/Features/Infrastructure/Persistence/Migrations/0005_app_settings.sql`:

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

Die `.csproj` hat bereits `<EmbeddedResource Include="Features/Infrastructure/Persistence/Migrations/*.sql" />` — keine .csproj-Änderung nötig.

- [ ] **Step 4: Test laufen lassen — soll grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~DbInitializerTests.Initialize_CreatesAppSettingsTable"`
Expected: PASS.

- [ ] **Step 5: `AppDb.DatabasePath` exponieren**

`src/Backend/Features/Infrastructure/Persistence/AppDb.cs` — Klasse so anpassen, dass der Pfad auch außerhalb sichtbar wird (Konstruktor bleibt, neuer Property):

```csharp
public sealed class AppDb
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public AppDb(IOptions<PersistenceOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        DatabasePath = path;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
```

- [ ] **Step 6: Failing-Test für DB-Permissions ergänzen**

In `src/Backend.Tests/Infrastructure/DbInitializerTests.cs` weiteren Test anhängen:

```csharp
[Fact]
public void Initialize_OnLinux_SetsDbPermissionsToOwnerOnly()
{
    if (!OperatingSystem.IsLinux()) return; // Test ist Linux-spezifisch

    using var db = new TempSqliteDb();

    var mode = File.GetUnixFileMode(db.Path);
    var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    mode.Should().Be(ownerOnly);
}
```

- [ ] **Step 7: Test laufen lassen — soll fehlschlagen (auf Linux)**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~DbInitializerTests.Initialize_OnLinux_SetsDbPermissionsToOwnerOnly"`
Expected: FAIL — Default-Permissions sind 0644.

- [ ] **Step 8: Permission-Härtung in `DbInitializer.Initialize` einbauen**

`src/Backend/Features/Infrastructure/Persistence/DbInitializer.cs` — am Ende der `Initialize()`-Methode, nach der `foreach`-Schleife der Migrationen:

```csharp
HardenPermissions();
```

Und unten in der Klasse die neue Methode:

```csharp
private void HardenPermissions()
{
    if (!OperatingSystem.IsLinux()) return;
    if (!File.Exists(_db.DatabasePath)) return;

    try
    {
        File.SetUnixFileMode(
            _db.DatabasePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "DB-Permission-Härtung auf 0600 fehlgeschlagen.");
    }
}
```

- [ ] **Step 9: Tests laufen lassen — alle DB-Tests grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~DbInitializerTests"`
Expected: PASS (alle).

- [ ] **Step 10: Commit**

```bash
git add src/Backend/Features/Infrastructure/Persistence/ \
        src/Backend.Tests/Infrastructure/DbInitializerTests.cs
git commit -m "Plan H Task 1: Migration 0005 (app_settings) + DB-Permissions 0600"
```

---

## Task 2: `LlmSettings`-Record + `IAppSettingsRepository` + `AppSettingsRepository`

**Files:**
- Create: `src/Backend/Features/Settings/LlmSettings.cs`
- Create: `src/Backend/Features/Settings/IAppSettingsRepository.cs`
- Create: `src/Backend/Features/Settings/AppSettingsRepository.cs`
- Create: `src/Backend.Tests/Features/Settings/AppSettingsRepositoryTests.cs`

- [ ] **Step 1: `LlmSettings`-Record + Provider-Konstanten erstellen**

`src/Backend/Features/Settings/LlmSettings.cs`:

```csharp
namespace NauAssist.Backend.Features.Settings;

public static class LlmProviders
{
    public const string Ollama = "ollama";
    public const string Gemini = "gemini";
}

public sealed record LlmSettings(
    string Provider,
    string OllamaModel,
    string GeminiModel,
    string? GeminiApiKey);
```

- [ ] **Step 2: `IAppSettingsRepository`-Interface erstellen**

`src/Backend/Features/Settings/IAppSettingsRepository.cs`:

```csharp
namespace NauAssist.Backend.Features.Settings;

public interface IAppSettingsRepository
{
    Task<LlmSettings> GetLlmAsync(CancellationToken ct);
    Task SetLlmAsync(LlmSettings settings, CancellationToken ct);
}
```

- [ ] **Step 3: Failing-Tests für Repository schreiben**

`src/Backend.Tests/Features/Settings/AppSettingsRepositoryTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryTests
{
    [Fact]
    public async Task GetLlm_ReturnsSeededDefaults()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var settings = await repo.GetLlmAsync(CancellationToken.None);

        settings.Provider.Should().Be("ollama");
        settings.OllamaModel.Should().Be("gemma4:26b");
        settings.GeminiModel.Should().Be("gemini-2.5-flash");
        settings.GeminiApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetLlm_RoundtripsAllFields()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(
            new LlmSettings(
                Provider: "gemini",
                OllamaModel: "qwen2.5:7b-instruct",
                GeminiModel: "gemini-2.5-pro",
                GeminiApiKey: "AIza-testkey"),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);

        loaded.Provider.Should().Be("gemini");
        loaded.OllamaModel.Should().Be("qwen2.5:7b-instruct");
        loaded.GeminiModel.Should().Be("gemini-2.5-pro");
        loaded.GeminiApiKey.Should().Be("AIza-testkey");
    }

    [Fact]
    public async Task SetLlm_NullKey_PersistsAsEmpty_ReadsBackAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: null),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.GeminiApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetLlm_EmptyStringKey_ReadsBackAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: ""),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.GeminiApiKey.Should().BeNull();
    }
}
```

- [ ] **Step 4: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryTests"`
Expected: Build-Error — `AppSettingsRepository` existiert nicht.

- [ ] **Step 5: `AppSettingsRepository` implementieren**

`src/Backend/Features/Settings/AppSettingsRepository.cs`:

```csharp
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Settings;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private const string KeyProvider = "llm.provider";
    private const string KeyOllamaModel = "llm.ollama.model";
    private const string KeyGeminiModel = "llm.gemini.model";
    private const string KeyGeminiApiKey = "llm.gemini.api_key";

    private readonly AppDb _db;

    public AppSettingsRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<LlmSettings> GetLlmAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2, @k3, @k4);",
            new
            {
                k1 = KeyProvider,
                k2 = KeyOllamaModel,
                k3 = KeyGeminiModel,
                k4 = KeyGeminiApiKey,
            },
            cancellationToken: ct));

        var map = rows.ToDictionary(r => r.Key, r => r.Value);

        var apiKeyRaw = map.GetValueOrDefault(KeyGeminiApiKey, "");
        return new LlmSettings(
            Provider: map.GetValueOrDefault(KeyProvider, "ollama"),
            OllamaModel: map.GetValueOrDefault(KeyOllamaModel, "gemma4:26b"),
            GeminiModel: map.GetValueOrDefault(KeyGeminiModel, "gemini-2.5-flash"),
            GeminiApiKey: string.IsNullOrEmpty(apiKeyRaw) ? null : apiKeyRaw);
    }

    public async Task SetLlmAsync(LlmSettings settings, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            await UpsertAsync(conn, tx, KeyProvider, settings.Provider, ct);
            await UpsertAsync(conn, tx, KeyOllamaModel, settings.OllamaModel, ct);
            await UpsertAsync(conn, tx, KeyGeminiModel, settings.GeminiModel, ct);
            await UpsertAsync(conn, tx, KeyGeminiApiKey, settings.GeminiApiKey ?? "", ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static Task UpsertAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        string key,
        string value,
        CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO app_settings(key, value) VALUES(@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            new { key, value },
            transaction: tx,
            cancellationToken: ct));
    }
}
```

- [ ] **Step 6: Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryTests"`
Expected: PASS (alle 4).

- [ ] **Step 7: Commit**

```bash
git add src/Backend/Features/Settings/ src/Backend.Tests/Features/Settings/
git commit -m "Plan H Task 2: AppSettingsRepository + LlmSettings-Record"
```

---

## Task 3: LLM-Options-Klassen (`OpenAICompatibleLlmOptions`, `GeminiOptions`)

**Files:**
- Create: `src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmOptions.cs`
- Create: `src/Backend/Features/Infrastructure/Llm/Gemini/GeminiOptions.cs`

- [ ] **Step 1: `OpenAICompatibleLlmOptions`-Record erstellen**

`src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmOptions.cs`:

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Llm;

/// <summary>
/// Pro Request konstruierter Options-Datensatz für <see cref="OpenAICompatibleLlmClient"/>.
/// Modell-spezifische und Provider-spezifische Werte werden hier vereinheitlicht.
/// </summary>
public sealed record OpenAICompatibleLlmOptions(
    string Model,
    int InitialTimeoutSeconds,
    int TokenTimeoutSeconds,
    string? SystemPrompt,
    double? Temperature,
    int? NumCtx);
```

- [ ] **Step 2: `GeminiOptions`-Klasse erstellen**

`src/Backend/Features/Infrastructure/Llm/Gemini/GeminiOptions.cs`:

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Llm.Gemini;

public sealed class GeminiOptions
{
    public string BaseAddress { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public int InitialTimeoutSeconds { get; set; } = 60;
    public int TokenTimeoutSeconds { get; set; } = 30;
    public string? SystemPrompt { get; set; }
    public double? Temperature { get; set; } = 0.3;
}
```

- [ ] **Step 3: Build prüfen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: SUCCESS.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmOptions.cs \
        src/Backend/Features/Infrastructure/Llm/Gemini/GeminiOptions.cs
git commit -m "Plan H Task 3: OpenAICompatibleLlmOptions + GeminiOptions"
```

---

## Task 4: `OllamaLlmClient` → `OpenAICompatibleLlmClient` umbenennen + parametrisieren

**Files:**
- Create: `src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmClient.cs`
- Delete: `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaLlmClient.cs`
- Create: `src/Backend.Tests/Features/Infrastructure/Llm/OpenAICompatibleLlmClientTests.cs`

Kontext: Bestehende Tests greifen nicht direkt auf `OllamaLlmClient` zu (nur über `ILlmClient`-Interface mit `FakeLlmClient`). Wir bauen eine eigene Test-Klasse für das Payload-Building.

- [ ] **Step 1: Failing-Tests schreiben — Payload-Building**

`src/Backend.Tests/Features/Infrastructure/Llm/OpenAICompatibleLlmClientTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Llm;

public sealed class OpenAICompatibleLlmClientTests
{
    [Fact]
    public async Task Payload_IncludesModelMessagesAndStreamTrue()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions(
                Model: "gemma4:26b",
                InitialTimeoutSeconds: 60,
                TokenTimeoutSeconds: 30,
                SystemPrompt: "You are a test.",
                Temperature: null,
                NumCtx: null),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        var payload = handler.LastBodyJson!;
        payload.GetProperty("model").GetString().Should().Be("gemma4:26b");
        payload.GetProperty("stream").GetBoolean().Should().BeTrue();
        var messages = payload.GetProperty("messages").EnumerateArray().ToList();
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("You are a test.");
        messages[1].GetProperty("role").GetString().Should().Be("user");
        messages[1].GetProperty("content").GetString().Should().Be("Hi");
    }

    [Fact]
    public async Task Payload_OmitsOptions_WhenNumCtxAndTemperatureNull()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions("model", 60, 30, null, null, null),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        handler.LastBodyJson!.TryGetProperty("options", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Payload_IncludesOptionsNumCtx_WhenSet()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions("model", 60, 30, null, null, NumCtx: 16384),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        handler.LastBodyJson!.GetProperty("options").GetProperty("num_ctx").GetInt32().Should().Be(16384);
    }

    [Fact]
    public async Task Payload_IncludesTopLevelTemperature_WhenSet()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions("model", 60, 30, null, Temperature: 0.3, NumCtx: null),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        var payload = handler.LastBodyJson!;
        payload.GetProperty("temperature").GetDouble().Should().BeApproximately(0.3, 0.0001);
        payload.TryGetProperty("options", out _).Should().BeFalse();
    }

    /// <summary>HTTP-Handler, der den Request-Body abgreift und eine leere SSE-Response zurückgibt.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public JsonElement? LastBodyJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var raw = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            LastBodyJson = doc.RootElement.Clone();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
            };
            return response;
        }
    }
}
```

- [ ] **Step 2: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~OpenAICompatibleLlmClientTests"`
Expected: Build-Error — `OpenAICompatibleLlmClient` existiert nicht.

- [ ] **Step 3: `OpenAICompatibleLlmClient` aus `OllamaLlmClient` umbauen**

Neue Datei `src/Backend/Features/Infrastructure/Llm/OpenAICompatibleLlmClient.cs`. Das ist ein parametrisierter Port von `OllamaLlmClient` — wesentliche Unterschiede:
- Klassen-Name + Namespace.
- Konstruktor erwartet direkt `OpenAICompatibleLlmOptions` (kein `IOptions<...>`-Wrapper, weil per-request-konstruiert).
- `BuildPayload` setzt `temperature` als Top-Level-Property (statt nested unter `options`); `num_ctx` bleibt unter `options`.

```csharp
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

public sealed class OpenAICompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OpenAICompatibleLlmOptions _options;
    private readonly ILogger<OpenAICompatibleLlmClient> _logger;

    public OpenAICompatibleLlmClient(
        HttpClient http,
        OpenAICompatibleLlmOptions options,
        ILogger<OpenAICompatibleLlmClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var payload = BuildPayload(messages, tools);
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload),
        };

        using var initialCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        initialCts.CancelAfter(TimeSpan.FromSeconds(_options.InitialTimeoutSeconds));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, initialCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var toolCallBuffer = new Dictionary<int, ToolCallBuilder>();

        while (true)
        {
            using var tokenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tokenCts.CancelAfter(TimeSpan.FromSeconds(_options.TokenTimeoutSeconds));

            var line = await reader.ReadLineAsync(tokenCts.Token);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    yield return new TextDeltaChunk(text);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tcEl in toolCallsEl.EnumerateArray())
                {
                    var index = tcEl.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                    if (!toolCallBuffer.TryGetValue(index, out var builder))
                    {
                        builder = new ToolCallBuilder();
                        toolCallBuffer[index] = builder;
                    }

                    if (tcEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        builder.Id ??= idEl.GetString();

                    if (tcEl.TryGetProperty("function", out var fnEl))
                    {
                        if (fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            builder.Name ??= nameEl.GetString();
                        if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            builder.ArgumentsBuffer.Append(argsEl.GetString());
                    }
                }
            }

            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String && fr.GetString() == "tool_calls")
            {
                foreach (var b in toolCallBuffer.Values)
                {
                    if (b.Name is null) continue;
                    JsonElement args;
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(b.ArgumentsBuffer.Length > 0 ? b.ArgumentsBuffer.ToString() : "{}");
                        args = argsDoc.RootElement.Clone();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Tool-Call-Args sind kein gültiges JSON: '{Raw}'", b.ArgumentsBuffer);
                        continue;
                    }
                    yield return new ToolCallChunk(new LlmToolCall(b.Id ?? Guid.NewGuid().ToString(), b.Name, args));
                }
                toolCallBuffer.Clear();
            }
        }
    }

    private object BuildPayload(IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition> tools)
    {
        var serializedMessages = new List<object>();

        if (!string.IsNullOrEmpty(_options.SystemPrompt))
        {
            serializedMessages.Add(new { role = "system", content = _options.SystemPrompt });
        }

        foreach (var m in messages)
        {
            var dict = new Dictionary<string, object?> { ["role"] = m.Role };
            if (m.Content is not null) dict["content"] = m.Content;
            if (m.ToolCallId is not null) dict["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is not null)
            {
                dict["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments.GetRawText() },
                }).ToArray();
            }
            serializedMessages.Add(dict);
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = serializedMessages,
            ["stream"] = true,
        };

        if (_options.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        if (tools.Count > 0)
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonDocument.Parse(t.ParameterSchema.GetRawText()).RootElement,
                },
            }).ToArray();
        }

        if (_options.NumCtx is { } numCtx)
        {
            payload["options"] = new Dictionary<string, object?> { ["num_ctx"] = numCtx };
        }

        return payload;
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder ArgumentsBuffer { get; } = new();
    }
}
```

Hinweis: Die Request-Url ist jetzt `chat/completions` relativ — der `BaseAddress` des `HttpClient` wird in der Factory gesetzt (Ollama: `http://localhost:11434/v1/`, Gemini: `https://generativelanguage.googleapis.com/v1beta/openai/`).

- [ ] **Step 4: `OllamaLlmClient` löschen**

```bash
git rm src/Backend/Features/Infrastructure/Llm/Ollama/OllamaLlmClient.cs
```

- [ ] **Step 5: Build prüfen — `Program.cs` referenziert noch das alte Symbol**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build-Error in `Program.cs:13` (`using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;`) und Z. 62 (`AddHttpClient<ILlmClient, OllamaLlmClient>`). Lass den Fehler stehen — wir reparieren ihn in Task 7 (Program.cs-Rewire), damit dieser Task fokussiert bleibt. Stattdessen erstmal nur den Test bauen:

Run: `dotnet build src/Backend.Tests/Backend.Tests.csproj`
Expected: Build-Error oder kompiliert? Backend.Tests hängt von Backend ab, also Build-Error. **Hier müssen wir den Build-Bruch im selben Task akzeptieren** — er wird in Task 7 behoben.

- [ ] **Step 6: Provisorisch Program.cs-Referenz auf neuen Client umstellen, damit Tests laufen**

`src/Backend/Program.cs` minimal anpassen, nur damit der Build durchgeht — die richtige Factory-Logik kommt in Task 7:

In Z. 13 `using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;` ersetzen durch nichts (entfernen).

Z. 62–66 (`AddHttpClient<ILlmClient, OllamaLlmClient>(...)`) ersetzen durch:

```csharp
// LLM — provisorisch, finalisiert in Task 7
builder.Services.AddHttpClient<ILlmClient, OpenAICompatibleLlmClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(opts.Host + "/v1/");
}).Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    return new OpenAICompatibleLlmOptions(
        Model: opts.Model,
        InitialTimeoutSeconds: opts.InitialTimeoutSeconds,
        TokenTimeoutSeconds: opts.TokenTimeoutSeconds,
        SystemPrompt: opts.SystemPrompt,
        Temperature: opts.Temperature,
        NumCtx: opts.NumCtx);
});
```

- [ ] **Step 7: Build + alle Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: PASS — bestehende ChatEndpoint- und AgentRunner-Tests grün (sie verwenden `FakeLlmClient`), neue `OpenAICompatibleLlmClientTests` grün.

- [ ] **Step 8: Commit**

```bash
git add -A src/Backend/Features/Infrastructure/Llm/ \
            src/Backend/Program.cs \
            src/Backend.Tests/Features/Infrastructure/Llm/
git commit -m "Plan H Task 4: OllamaLlmClient → OpenAICompatibleLlmClient"
```

---

## Task 5: `ILlmClientFactory` + `LlmClientFactory`

**Files:**
- Create: `src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs`
- Create: `src/Backend.Tests/Features/Infrastructure/Llm/LlmClientFactoryTests.cs`

- [ ] **Step 1: Failing-Tests schreiben**

`src/Backend.Tests/Features/Infrastructure/Llm/LlmClientFactoryTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Llm.Gemini;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Llm;

public sealed class LlmClientFactoryTests
{
    [Fact]
    public async Task Create_OllamaProvider_BuildsClient_WithoutAuth()
    {
        var factory = NewFactory(new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null));
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("http://localhost:11434/v1/");
        http.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Create_GeminiProvider_WithKey_BuildsClient_WithBearer()
    {
        var factory = NewFactory(new LlmSettings("gemini", "gemma4:26b", "gemini-2.5-flash", "AIza-xyz"));
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("https://generativelanguage.googleapis.com/v1beta/openai/");
        http.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        http.DefaultRequestHeaders.Authorization.Parameter.Should().Be("AIza-xyz");
    }

    [Fact]
    public async Task Create_GeminiProvider_WithoutKey_Throws()
    {
        var factory = NewFactory(new LlmSettings("gemini", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: null));

        var act = async () => await factory.CreateAsync(CancellationToken.None);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Gemini*Key*");
    }

    [Fact]
    public async Task Create_UnknownProvider_Throws()
    {
        var factory = NewFactory(new LlmSettings("anthropic", "gemma4:26b", "gemini-2.5-flash", null));

        var act = async () => await factory.CreateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static LlmClientFactory NewFactory(LlmSettings settings)
    {
        var ollama = Options.Create(new OllamaOptions
        {
            Host = "http://localhost:11434",
            Model = "gemma4:26b",
            InitialTimeoutSeconds = 60,
            TokenTimeoutSeconds = 30,
            SystemPrompt = "sys",
            NumCtx = 16384,
            Temperature = 0.3,
        });
        var gemini = Options.Create(new GeminiOptions
        {
            BaseAddress = "https://generativelanguage.googleapis.com/v1beta/openai/",
            InitialTimeoutSeconds = 60,
            TokenTimeoutSeconds = 30,
            SystemPrompt = null,
            Temperature = 0.3,
        });

        var repo = new FakeSettingsRepo(settings);
        var httpFactory = new TestHttpClientFactory();
        var loggerFactory = NullLoggerFactory.Instance;

        return new LlmClientFactory(httpFactory, repo, ollama, gemini, loggerFactory);
    }

    private sealed class FakeSettingsRepo : IAppSettingsRepository
    {
        private LlmSettings _settings;
        public FakeSettingsRepo(LlmSettings s) => _settings = s;
        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) => Task.FromResult(_settings);
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct) { _settings = s; return Task.CompletedTask; }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
```

Wir brauchen einen Test-Hook auf der Factory, um den frisch konstruierten `HttpClient` zu inspizieren. Pragma: Factory bekommt eine `internal`-Methode `CreateInternalForTestAsync()`, die Tuple zurückgibt. Mit dem bestehenden `InternalsVisibleTo`-Attribut in `Backend.csproj` ist das sauber lösbar.

- [ ] **Step 2: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~LlmClientFactoryTests"`
Expected: Build-Error — `LlmClientFactory` existiert nicht.

- [ ] **Step 3: `ILlmClientFactory` + `LlmClientFactory` implementieren**

`src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs`:

```csharp
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm.Gemini;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

public interface ILlmClientFactory
{
    Task<ILlmClient> CreateAsync(CancellationToken ct);
}

public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IAppSettingsRepository _settings;
    private readonly OllamaOptions _ollamaDefaults;
    private readonly GeminiOptions _geminiDefaults;
    private readonly ILoggerFactory _loggerFactory;

    public LlmClientFactory(
        IHttpClientFactory httpFactory,
        IAppSettingsRepository settings,
        IOptions<OllamaOptions> ollamaDefaults,
        IOptions<GeminiOptions> geminiDefaults,
        ILoggerFactory loggerFactory)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _ollamaDefaults = ollamaDefaults.Value;
        _geminiDefaults = geminiDefaults.Value;
        _loggerFactory = loggerFactory;
    }

    public async Task<ILlmClient> CreateAsync(CancellationToken ct)
    {
        var (client, _) = await CreateInternalAsync(ct);
        return client;
    }

    internal async Task<(ILlmClient Client, HttpClient Http)> CreateInternalForTestAsync()
    {
        return await CreateInternalAsync(CancellationToken.None);
    }

    private async Task<(ILlmClient Client, HttpClient Http)> CreateInternalAsync(CancellationToken ct)
    {
        var s = await _settings.GetLlmAsync(ct);
        return s.Provider switch
        {
            LlmProviders.Ollama => BuildOllama(s),
            LlmProviders.Gemini => BuildGemini(s),
            _ => throw new InvalidOperationException($"Unbekannter LLM-Provider: '{s.Provider}'."),
        };
    }

    private (ILlmClient, HttpClient) BuildOllama(LlmSettings s)
    {
        var http = _httpFactory.CreateClient("Ollama");
        http.BaseAddress = new Uri(_ollamaDefaults.Host.TrimEnd('/') + "/v1/");

        var options = new OpenAICompatibleLlmOptions(
            Model: s.OllamaModel,
            InitialTimeoutSeconds: _ollamaDefaults.InitialTimeoutSeconds,
            TokenTimeoutSeconds: _ollamaDefaults.TokenTimeoutSeconds,
            SystemPrompt: _ollamaDefaults.SystemPrompt,
            Temperature: _ollamaDefaults.Temperature,
            NumCtx: _ollamaDefaults.NumCtx);

        var logger = _loggerFactory.CreateLogger<OpenAICompatibleLlmClient>();
        return (new OpenAICompatibleLlmClient(http, options, logger), http);
    }

    private (ILlmClient, HttpClient) BuildGemini(LlmSettings s)
    {
        if (string.IsNullOrEmpty(s.GeminiApiKey))
        {
            throw new InvalidOperationException(
                "Gemini-Provider aktiviert, aber kein API-Key konfiguriert.");
        }

        var http = _httpFactory.CreateClient("Gemini");
        http.BaseAddress = new Uri(_geminiDefaults.BaseAddress);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.GeminiApiKey);

        // Gemini hat kein Konzept von num_ctx; SystemPrompt fällt auf Ollama-Default zurück (Modell-agnostisch).
        var systemPrompt = _geminiDefaults.SystemPrompt ?? _ollamaDefaults.SystemPrompt;

        var options = new OpenAICompatibleLlmOptions(
            Model: s.GeminiModel,
            InitialTimeoutSeconds: _geminiDefaults.InitialTimeoutSeconds,
            TokenTimeoutSeconds: _geminiDefaults.TokenTimeoutSeconds,
            SystemPrompt: systemPrompt,
            Temperature: _geminiDefaults.Temperature,
            NumCtx: null);

        var logger = _loggerFactory.CreateLogger<OpenAICompatibleLlmClient>();
        return (new OpenAICompatibleLlmClient(http, options, logger), http);
    }
}
```

- [ ] **Step 4: Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~LlmClientFactoryTests"`
Expected: PASS (alle 4).

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs \
        src/Backend.Tests/Features/Infrastructure/Llm/LlmClientFactoryTests.cs
git commit -m "Plan H Task 5: LlmClientFactory mit Provider-Switch"
```

---

## Task 6: `GetLlmSettings`- und `UpdateLlmSettings`-Handler + `SupportedModels`

**Files:**
- Create: `src/Backend/Features/Settings/SupportedModels.cs`
- Create: `src/Backend/Features/Settings/GetLlmSettings/GetLlmSettingsRequest.cs`
- Create: `src/Backend/Features/Settings/GetLlmSettings/GetLlmSettingsHandler.cs`
- Create: `src/Backend/Features/Settings/UpdateLlmSettings/UpdateLlmSettingsRequest.cs`
- Create: `src/Backend/Features/Settings/UpdateLlmSettings/UpdateLlmSettingsHandler.cs`
- Create: `src/Backend.Tests/Features/Settings/GetLlmSettingsHandlerTests.cs`
- Create: `src/Backend.Tests/Features/Settings/UpdateLlmSettingsHandlerTests.cs`

- [ ] **Step 1: `SupportedModels`-Whitelist erstellen**

`src/Backend/Features/Settings/SupportedModels.cs`:

```csharp
namespace NauAssist.Backend.Features.Settings;

public static class SupportedModels
{
    public static readonly IReadOnlyList<string> Ollama = new[]
    {
        "gemma4:26b",
        "qwen2.5:7b-instruct",
        "llama3.2:3b",
    };

    public static readonly IReadOnlyList<string> Gemini = new[]
    {
        "gemini-2.5-flash",
        "gemini-2.5-pro",
    };
}
```

- [ ] **Step 2: `GetLlmSettings` Request + Response definieren**

`src/Backend/Features/Settings/GetLlmSettings/GetLlmSettingsRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.GetLlmSettings;

public sealed record GetLlmSettingsRequest : IRequest<GetLlmSettingsResponse>;

public sealed record GetLlmSettingsResponse(
    string Provider,
    string OllamaModel,
    string GeminiModel,
    bool HasGeminiApiKey);
```

- [ ] **Step 3: Failing-Tests für `GetLlmSettingsHandler` schreiben**

`src/Backend.Tests/Features/Settings/GetLlmSettingsHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.GetLlmSettings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class GetLlmSettingsHandlerTests
{
    [Fact]
    public async Task Handle_DefaultsFromMigration()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        var handler = new GetLlmSettingsHandler(repo);

        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.Provider.Should().Be("ollama");
        response.OllamaModel.Should().Be("gemma4:26b");
        response.GeminiModel.Should().Be("gemini-2.5-flash");
        response.HasGeminiApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_HasGeminiApiKeyTrue_AfterKeyIsSet()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", "AIza-x"),
            CancellationToken.None);

        var handler = new GetLlmSettingsHandler(repo);
        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.HasGeminiApiKey.Should().BeTrue();
    }
}
```

- [ ] **Step 4: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~GetLlmSettingsHandlerTests"`
Expected: Build-Error — `GetLlmSettingsHandler` existiert nicht.

- [ ] **Step 5: `GetLlmSettingsHandler` implementieren**

`src/Backend/Features/Settings/GetLlmSettings/GetLlmSettingsHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.GetLlmSettings;

public sealed class GetLlmSettingsHandler : IRequestHandler<GetLlmSettingsRequest, GetLlmSettingsResponse>
{
    private readonly IAppSettingsRepository _settings;

    public GetLlmSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<GetLlmSettingsResponse> Handle(GetLlmSettingsRequest request, CancellationToken ct)
    {
        var s = await _settings.GetLlmAsync(ct);
        return new GetLlmSettingsResponse(
            Provider: s.Provider,
            OllamaModel: s.OllamaModel,
            GeminiModel: s.GeminiModel,
            HasGeminiApiKey: !string.IsNullOrEmpty(s.GeminiApiKey));
    }
}
```

- [ ] **Step 6: GetLlmSettings-Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~GetLlmSettingsHandlerTests"`
Expected: PASS (2).

- [ ] **Step 7: `UpdateLlmSettings` Request + Result definieren**

`src/Backend/Features/Settings/UpdateLlmSettings/UpdateLlmSettingsRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateLlmSettings;

/// <summary>
/// GeminiApiKey-Konvention:
///  - null  → Bestand bleibt
///  - ""    → Key löschen
///  - sonst → überschreiben
/// </summary>
public sealed record UpdateLlmSettingsRequest(
    string Provider,
    string OllamaModel,
    string GeminiModel,
    string? GeminiApiKey) : IRequest<UpdateLlmSettingsResult>;

public sealed record UpdateLlmSettingsResult(bool Ok, string? Error);
```

- [ ] **Step 8: Failing-Tests für `UpdateLlmSettingsHandler` schreiben**

`src/Backend.Tests/Features/Settings/UpdateLlmSettingsHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateLlmSettings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateLlmSettingsHandlerTests
{
    [Fact]
    public async Task Handle_UnknownProvider_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("anthropic", "gemma4:26b", "gemini-2.5-flash", null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("provider");
    }

    [Fact]
    public async Task Handle_InvalidOllamaModel_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "gpt-4", "gemini-2.5-flash", null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("ollamaModel");
    }

    [Fact]
    public async Task Handle_InvalidGeminiModel_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "gemma4:26b", "gpt-4", null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("geminiModel");
    }

    [Fact]
    public async Task Handle_SwitchToGeminiWithoutKey_ReturnsError()
    {
        var repo = new InMemorySettingsRepo(); // Default: kein Key
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("gemini", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("API-Key");
    }

    [Fact]
    public async Task Handle_SwitchToGeminiWithKey_Succeeds()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("gemini", "gemma4:26b", "gemini-2.5-flash", "AIza-xyz"),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        repo.Current.Provider.Should().Be("gemini");
        repo.Current.GeminiApiKey.Should().Be("AIza-xyz");
    }

    [Fact]
    public async Task Handle_NullKey_DoesNotOverwriteExistingKey()
    {
        var repo = new InMemorySettingsRepo();
        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", "AIza-original"),
            CancellationToken.None);
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("gemini", "gemma4:26b", "gemini-2.5-pro", GeminiApiKey: null),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.GeminiApiKey.Should().Be("AIza-original");
        repo.Current.GeminiModel.Should().Be("gemini-2.5-pro");
    }

    [Fact]
    public async Task Handle_EmptyStringKey_DeletesKey()
    {
        var repo = new InMemorySettingsRepo();
        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", "AIza-original"),
            CancellationToken.None);
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: ""),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.GeminiApiKey.Should().BeNull();
    }

    private sealed class InMemorySettingsRepo : IAppSettingsRepository
    {
        public LlmSettings Current { get; private set; } =
            new("ollama", "gemma4:26b", "gemini-2.5-flash", null);

        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) => Task.FromResult(Current);

        public Task SetLlmAsync(LlmSettings settings, CancellationToken ct)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 9: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateLlmSettingsHandlerTests"`
Expected: Build-Error.

- [ ] **Step 10: `UpdateLlmSettingsHandler` implementieren**

`src/Backend/Features/Settings/UpdateLlmSettings/UpdateLlmSettingsHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateLlmSettings;

public sealed class UpdateLlmSettingsHandler
    : IRequestHandler<UpdateLlmSettingsRequest, UpdateLlmSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public UpdateLlmSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<UpdateLlmSettingsResult> Handle(
        UpdateLlmSettingsRequest request,
        CancellationToken ct)
    {
        if (request.Provider != LlmProviders.Ollama && request.Provider != LlmProviders.Gemini)
        {
            return new UpdateLlmSettingsResult(false, $"Ungültiger provider: '{request.Provider}'.");
        }

        if (!SupportedModels.Ollama.Contains(request.OllamaModel))
        {
            return new UpdateLlmSettingsResult(false, $"Ungültiges ollamaModel: '{request.OllamaModel}'.");
        }

        if (!SupportedModels.Gemini.Contains(request.GeminiModel))
        {
            return new UpdateLlmSettingsResult(false, $"Ungültiges geminiModel: '{request.GeminiModel}'.");
        }

        var existing = await _settings.GetLlmAsync(ct);

        string? newKey = request.GeminiApiKey switch
        {
            null => existing.GeminiApiKey, // Bestand bleibt
            ""   => null,                  // löschen
            _    => request.GeminiApiKey,  // setzen
        };

        if (request.Provider == LlmProviders.Gemini && string.IsNullOrEmpty(newKey))
        {
            return new UpdateLlmSettingsResult(false,
                "Gemini benötigt einen API-Key — bitte eintragen, bevor du wechselst.");
        }

        await _settings.SetLlmAsync(
            new LlmSettings(request.Provider, request.OllamaModel, request.GeminiModel, newKey),
            ct);

        return new UpdateLlmSettingsResult(true, null);
    }
}
```

- [ ] **Step 11: Update-Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateLlmSettingsHandlerTests"`
Expected: PASS (7).

- [ ] **Step 12: Commit**

```bash
git add src/Backend/Features/Settings/ src/Backend.Tests/Features/Settings/
git commit -m "Plan H Task 6: GetLlmSettings + UpdateLlmSettings Mediator-Handler"
```

---

## Task 7: `Program.cs` finales DI-Wiring + `appsettings.json`

**Files:**
- Modify: `src/Backend/Program.cs`
- Modify: `src/Backend/appsettings.json`

- [ ] **Step 1: `appsettings.json` ergänzen**

`src/Backend/appsettings.json` — neue Sektion `Gemini` nach der `Ollama`-Sektion einfügen:

```json
"Gemini": {
  "BaseAddress": "https://generativelanguage.googleapis.com/v1beta/openai/",
  "InitialTimeoutSeconds": 60,
  "TokenTimeoutSeconds": 30,
  "Temperature": 0.3
}
```

- [ ] **Step 2: `Program.cs` LLM-Wiring umstellen**

`src/Backend/Program.cs` — die provisorische Sektion aus Task 4 (Zeilen rund um `AddHttpClient<ILlmClient, ...>`) komplett ersetzen durch:

```csharp
// LLM
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddHttpClient("Ollama");
builder.Services.AddHttpClient("Gemini");
builder.Services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
builder.Services.AddScoped<ILlmClientFactory, LlmClientFactory>();
builder.Services.AddScoped<ILlmClient>(sp =>
{
    var factory = sp.GetRequiredService<ILlmClientFactory>();
    return factory.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();
});
```

Auch die alte Zeile `builder.Services.Configure<OllamaOptions>(...)` aus dem oberen Configure-Block entfernen — sie wird jetzt im LLM-Block weiter unten neu registriert.

Neue Usings oben in `Program.cs` ergänzen:

```csharp
using NauAssist.Backend.Features.Infrastructure.Llm.Gemini;
using NauAssist.Backend.Features.Settings;
```

`using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;` bleibt (für `OllamaOptions`).

- [ ] **Step 3: Build + alle Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: PASS — bestehende ChatEndpoint-/AgentRunner-Tests injizieren weiterhin `FakeLlmClient` als Singleton, was den scoped Factory-Provider überschreibt. Sollte ein Test fehlschlagen, prüfen ob Scoped-Override über `AddSingleton<ILlmClient>(_fakeLlm)` greift; bei Bedarf in `ChatEndpointTests.Build()` auf `AddScoped` umstellen.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Program.cs src/Backend/appsettings.json
git commit -m "Plan H Task 7: ILlmClient via Factory pro Request (Hot-Reload)"
```

---

## Task 8: `SettingsEndpoints` HTTP-Routen + Endpoint-Tests + Audit-Log

**Files:**
- Create: `src/Backend/Endpoints/SettingsEndpoints.cs`
- Modify: `src/Backend/Program.cs`
- Create: `src/Backend.Tests/Endpoints/SettingsEndpointsTests.cs`

- [ ] **Step 1: Failing-Tests für Endpoints schreiben**

`src/Backend.Tests/Endpoints/SettingsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class SettingsEndpointsTests : IDisposable
{
    private readonly TestAppFactory _factory = new();

    [Fact]
    public async Task Get_ReturnsDefaults_NoApiKeyExposed()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/settings/llm");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LlmSettingsDto>();

        body!.Provider.Should().Be("ollama");
        body.OllamaModel.Should().Be("gemma4:26b");
        body.GeminiModel.Should().Be("gemini-2.5-flash");
        body.HasGeminiApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Put_Valid_ReturnsOk_AndGetReflectsChange()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "gemini",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = "AIza-test",
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        using var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.Provider.Should().Be("gemini");
        body.HasGeminiApiKey.Should().BeTrue();
    }

    [Fact]
    public async Task Put_InvalidProvider_Returns400()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "anthropic",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = (string?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_SwitchToGeminiWithoutKey_Returns400()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "gemini",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = (string?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_EmptyKey_DeletesExistingKey()
    {
        using var client = _factory.CreateClient();
        await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "ollama",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = "AIza-keepme",
        });

        var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "ollama",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = "",
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.HasGeminiApiKey.Should().BeFalse();
    }

    public void Dispose() => _factory.Dispose();

    private sealed record LlmSettingsDto(
        string Provider,
        string OllamaModel,
        string GeminiModel,
        bool HasGeminiApiKey);
}
```

- [ ] **Step 2: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~SettingsEndpointsTests"`
Expected: FAIL — 404, Route existiert nicht.

- [ ] **Step 3: `SettingsEndpoints` implementieren (inkl. Audit-Logging)**

`src/Backend/Endpoints/SettingsEndpoints.cs`:

```csharp
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Settings.GetLlmSettings;
using NauAssist.Backend.Features.Settings.UpdateLlmSettings;

namespace NauAssist.Backend.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/llm", async (
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new GetLlmSettingsRequest(), ct);
            return Results.Ok(new LlmSettingsDto(
                response.Provider,
                response.OllamaModel,
                response.GeminiModel,
                response.HasGeminiApiKey));
        });

        app.MapPut("/api/settings/llm", async (
            UpdateLlmSettingsPayload payload,
            IMediator mediator,
            AuditLogRepository audit,
            Func<DateTimeOffset> clock,
            ILogger<UpdateLlmSettingsResult> logger,
            CancellationToken ct) =>
        {
            var request = new UpdateLlmSettingsRequest(
                Provider: payload.Provider ?? "",
                OllamaModel: payload.OllamaModel ?? "",
                GeminiModel: payload.GeminiModel ?? "",
                GeminiApiKey: payload.GeminiApiKey);

            var result = await mediator.Send(request, ct);

            if (!result.Ok)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            var auditArgs = JsonSerializer.Serialize(new
            {
                provider = payload.Provider,
                ollamaModel = payload.OllamaModel,
                geminiModel = payload.GeminiModel,
                geminiKeyAction = payload.GeminiApiKey switch
                {
                    null => "unchanged",
                    "" => "cleared",
                    _ => "set",
                },
            });

            await audit.AppendAsync(
                new AuditEntry(
                    Id: 0,
                    TriggeringMessageId: null,
                    ToolName: "settings.llm.update",
                    ToolArgsJson: auditArgs,
                    ResultJson: "{\"ok\":true}",
                    ProviderEventId: null,
                    CreatedAt: clock()),
                ct);

            logger.LogInformation("LLM-Settings aktualisiert: {Args}", auditArgs);

            return Results.Ok(new { ok = true });
        });

        return app;
    }

    public sealed record UpdateLlmSettingsPayload(
        string? Provider,
        string? OllamaModel,
        string? GeminiModel,
        string? GeminiApiKey);

    private sealed record LlmSettingsDto(
        string Provider,
        string OllamaModel,
        string GeminiModel,
        bool HasGeminiApiKey);
}
```

- [ ] **Step 4: `Program.cs` Route registrieren**

`src/Backend/Program.cs` — bei den anderen `Map…Endpoints`-Aufrufen ergänzen:

```csharp
app.MapHealthEndpoints();
app.MapRulesEndpoints();
app.MapChatEndpoints();
app.MapSettingsEndpoints();   // ← neu
```

Plus `using NauAssist.Backend.Endpoints;` ist bereits da; falls Endpoints in einem anderen Namespace landen, anpassen.

- [ ] **Step 5: Endpoint-Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~SettingsEndpointsTests"`
Expected: PASS (5).

- [ ] **Step 6: Vollen Test-Run prüfen — keine Regressionen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: PASS (alle).

- [ ] **Step 7: Commit**

```bash
git add src/Backend/Endpoints/SettingsEndpoints.cs \
        src/Backend/Program.cs \
        src/Backend.Tests/Endpoints/SettingsEndpointsTests.cs
git commit -m "Plan H Task 8: GET/PUT /api/settings/llm + Audit-Log"
```

---

## Task 9: Frontend — Settings-API-Modul + Typen

**Files:**
- Create: `frontend/src/api/settings.ts`

- [ ] **Step 1: API-Modul erstellen**

`frontend/src/api/settings.ts`:

```typescript
export interface LlmSettings {
  provider: "ollama" | "gemini";
  ollamaModel: string;
  geminiModel: string;
  hasGeminiApiKey: boolean;
}

export interface UpdateLlmSettingsPayload {
  provider: "ollama" | "gemini";
  ollamaModel: string;
  geminiModel: string;
  geminiApiKey: string | null; // null = unchanged, "" = clear, else = set
}

export const OLLAMA_MODELS = [
  "gemma4:26b",
  "qwen2.5:7b-instruct",
  "llama3.2:3b",
] as const;

export const GEMINI_MODELS = [
  "gemini-2.5-flash",
  "gemini-2.5-pro",
] as const;

export async function getLlmSettings(): Promise<LlmSettings> {
  const res = await fetch("/api/settings/llm");
  if (!res.ok) throw new Error(`GET /api/settings/llm failed: ${res.status}`);
  return res.json();
}

export async function updateLlmSettings(
  payload: UpdateLlmSettingsPayload,
): Promise<void> {
  const res = await fetch("/api/settings/llm", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `PUT /api/settings/llm failed: ${res.status}`);
  }
}
```

- [ ] **Step 2: Build prüfen**

Run: `cd frontend && npx tsc --noEmit`
Expected: SUCCESS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/settings.ts
git commit -m "Plan H Task 9: Frontend-API-Modul für /api/settings/llm"
```

---

## Task 10: Frontend — `SettingsPage` echte LLM-Sektion + Footer-Cleanup

**Files:**
- Modify: `frontend/src/components/pages/SettingsPage.tsx`

- [ ] **Step 1: Imports + State-Hook in `SettingsPage` ergänzen**

Oben in `frontend/src/components/pages/SettingsPage.tsx` Importe und State einfügen.

Im Import-Block (Zeile 1–2) ergänzen:

```tsx
import { useEffect, useState } from "react";
import {
  getLlmSettings,
  updateLlmSettings,
  OLLAMA_MODELS,
  GEMINI_MODELS,
  type LlmSettings,
} from "@/api/settings";
```

In der `SettingsPage`-Component (nach `const navItems = ...`, vor `const shortcuts = ...`):

```tsx
const [llm, setLlm] = useState<LlmSettings | null>(null);
const [llmError, setLlmError] = useState<string | null>(null);
const [draftKey, setDraftKey] = useState<string>("");
const [editingKey, setEditingKey] = useState(false);
const [saving, setSaving] = useState(false);
const [savedFlash, setSavedFlash] = useState(false);

useEffect(() => {
  getLlmSettings().then(setLlm).catch((e) => setLlmError(String(e.message ?? e)));
}, []);

const saveLlm = async (patch: Partial<LlmSettings> & { geminiApiKey?: string | null }) => {
  if (!llm) return;
  setSaving(true);
  setLlmError(null);
  try {
    await updateLlmSettings({
      provider: patch.provider ?? llm.provider,
      ollamaModel: patch.ollamaModel ?? llm.ollamaModel,
      geminiModel: patch.geminiModel ?? llm.geminiModel,
      geminiApiKey: patch.geminiApiKey ?? null,
    });
    const fresh = await getLlmSettings();
    setLlm(fresh);
    setSavedFlash(true);
    setTimeout(() => setSavedFlash(false), 2500);
  } catch (e) {
    setLlmError(String((e as Error).message ?? e));
  } finally {
    setSaving(false);
    setEditingKey(false);
    setDraftKey("");
  }
};
```

- [ ] **Step 2: LLM-Sektion vor "Tonalität" einfügen**

Direkt nach dem öffnenden `<div>` des Sektion-03-Wrappers — also direkt nach der `<SectionHead n={3} … />`-Zeile (etwa Zeile 443 in der aktuellen Datei), folgenden Block einfügen, **vor** dem ersten `<Row label="Tonalität" …>`:

```tsx
{llmError && !llm && (
  <div className="border border-nau-line bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
    // SETTINGS NICHT LADBAR — BACKEND OFFLINE?
  </div>
)}

{llm && (
  <>
    <Row label="AI-Provider" hint="Welche AI Nau für seine Antworten nutzt.">
      <div className="inline-flex border border-nau-line">
        {(["ollama", "gemini"] as const).map((p, i) => {
          const active = llm.provider === p;
          return (
            <button
              key={p}
              type="button"
              onClick={() => saveLlm({ provider: p })}
              disabled={saving}
              className="cursor-pointer bg-transparent px-4 py-2.5 font-mono text-[11px] uppercase tracking-mono"
              style={{
                background: active ? "#facc15" : "transparent",
                color: active ? "#0a0a0a" : "#f5f5f4",
                borderLeft: i > 0 ? "1px solid rgba(255,255,255,0.10)" : "none",
              }}
            >
              {p === "ollama" ? "Ollama (lokal)" : "Gemini (Cloud)"}
            </button>
          );
        })}
      </div>
    </Row>

    <Row label="Modell" hint="Welches Modell verwendet wird.">
      <select
        value={llm.provider === "ollama" ? llm.ollamaModel : llm.geminiModel}
        disabled={saving}
        onChange={(e) =>
          saveLlm(
            llm.provider === "ollama"
              ? { ollamaModel: e.target.value }
              : { geminiModel: e.target.value },
          )
        }
        className="max-w-[480px] border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
      >
        {(llm.provider === "ollama" ? OLLAMA_MODELS : GEMINI_MODELS).map((m) => (
          <option key={m} value={m} className="bg-nau-bg text-nau-fg">
            {m}
          </option>
        ))}
      </select>
    </Row>

    {llm.provider === "gemini" && (
      <Row
        label="Gemini API-Key"
        hint={"Wird sicher lokal gespeichert. Hol dir einen Key bei aistudio.google.com."}
      >
        {llm.hasGeminiApiKey && !editingKey ? (
          <div className="flex items-center gap-3">
            <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
              •••••••••• GESPEICHERT
            </span>
            <button
              type="button"
              onClick={() => setEditingKey(true)}
              className="cursor-pointer border border-nau-line bg-transparent px-3.5 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg"
            >
              ÄNDERN
            </button>
            <button
              type="button"
              onClick={() => saveLlm({ geminiApiKey: "" })}
              disabled={saving}
              className="cursor-pointer border border-nau-line bg-transparent px-3.5 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim"
            >
              ENTFERNEN
            </button>
          </div>
        ) : (
          <div className="flex items-center gap-3">
            <input
              type="password"
              value={draftKey}
              onChange={(e) => setDraftKey(e.target.value)}
              placeholder="AIza..."
              className="max-w-[360px] flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
            />
            <button
              type="button"
              onClick={() => saveLlm({ geminiApiKey: draftKey })}
              disabled={saving || draftKey.length === 0}
              className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg"
            >
              ÜBERNEHMEN ↵
            </button>
            {llm.hasGeminiApiKey && (
              <button
                type="button"
                onClick={() => { setEditingKey(false); setDraftKey(""); }}
                className="cursor-pointer bg-transparent px-2 py-2 font-mono text-[10px] tracking-mono text-nau-fg-dim"
              >
                ABBRECHEN
              </button>
            )}
          </div>
        )}
      </Row>
    )}

    {savedFlash && (
      <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-accent">
        // PROVIDER AKTUALISIERT — WIRD AB DEINER NÄCHSTEN NACHRICHT GENUTZT
      </div>
    )}
    {llmError && llm && (
      <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-danger">
        // FEHLER: {llmError}
      </div>
    )}

    <div className="border-b border-nau-line py-4 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
      // ↓ ABSCHNITT UNTEN IST MOCKUP — NOCH NICHT VERDRAHTET
    </div>
  </>
)}
```

- [ ] **Step 3: Footer-Text "WIRD AUTOMATISCH GESPEICHERT" entfernen**

In der gleichen Datei, suche `// ALLE ÄNDERUNGEN WERDEN AUTOMATISCH GESPEICHERT` und ersetze den umschließenden Footer-Container so, dass nur die Buttons-Zeile bleibt. Konkret:

Bestehender Block (am Ende der `<main>`-Section):

```tsx
<div className="mt-14 flex items-center justify-between border-t border-nau-line pt-6">
  <div className="font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
    // ALLE ÄNDERUNGEN WERDEN AUTOMATISCH GESPEICHERT
  </div>
  <div className="flex gap-3">
    ...
  </div>
</div>
```

Ersetzen durch:

```tsx
<div className="mt-14 flex items-center justify-end border-t border-nau-line pt-6">
  <div className="flex gap-3">
    <button
      type="button"
      onClick={() => onNavigate("chat")}
      className="cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg"
    >
      ZURÜCK ZUM CHAT
    </button>
    <button
      type="button"
      onClick={() => onNavigate("chat")}
      className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg"
    >
      FERTIG ↵
    </button>
  </div>
</div>
```

- [ ] **Step 4: TypeScript-Build prüfen**

Run: `cd frontend && npx tsc --noEmit`
Expected: SUCCESS.

- [ ] **Step 5: Frontend dev-server starten + Sektion manuell visuell prüfen**

Run: `cd frontend && npm run dev` (in einem separaten Terminal). Browser auf `http://localhost:5173`, "Einstellungen" öffnen, Sektion 03 prüfen.

Erwartete Anzeige:
- Sektion 03 zeigt zuerst: AI-Provider-Toggle (Ollama aktiv), Modell-Dropdown mit `gemma4:26b`, KEINE Key-Row (weil Ollama).
- Toggle auf Gemini klicken → Modell-Dropdown wechselt auf Gemini-Modelle, Key-Row erscheint.
- Klick auf Gemini ohne Key → rote `// FEHLER:`-Zeile mit "Gemini benötigt einen API-Key …".

(Wenn das Backend nicht läuft, sieht man `// SETTINGS NICHT LADBAR — BACKEND OFFLINE?`.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/SettingsPage.tsx
git commit -m "Plan H Task 10: SettingsPage AI-Provider-Sektion (echt) + Footer-Cleanup"
```

---

## Task 11: Manuelle End-to-End-Verifikation

**Files:** keine — reines manuelles Smoke-Testing.

- [ ] **Step 1: Backend starten**

Run: `cd src/Backend && dotnet run`

Erwartet: kein Crash, Ollama als Default-Provider aktiv (sofern Ollama-Daemon läuft).

- [ ] **Step 2: Frontend starten**

Run: `cd frontend && npm run dev`

Erwartet: Vite-Dev-Server unter `http://localhost:5173`.

- [ ] **Step 3: Regression — Ollama funktioniert wie bisher**

Im Chat: "Wann hab ich Freitag nachmittag Zeit?" (oder analog zu deinem bisherigen Smoke-Workflow). Erwartet: bekanntes Verhalten (`lookup_free_slots` → `present_proposals` → Slots im UI).

- [ ] **Step 4: Switch auf Gemini ohne Key**

Settings → AI-Provider auf "Gemini" klicken. Erwartet: rote `// FEHLER:`-Zeile, Toggle bleibt visuell auf Ollama (weil Backend ablehnt) — oder schaltet erst nach erfolgreichem Save um. Genaues Verhalten beobachten.

- [ ] **Step 5: API-Key besorgen und eintragen**

In `aistudio.google.com` → "Get API Key" → Key kopieren. In NauAssist-Settings einfügen, "ÜBERNEHMEN" klicken. Erwartet: grüne `// PROVIDER AKTUALISIERT`-Zeile.

- [ ] **Step 6: Chat mit Gemini**

Chat-Nachricht senden: dieselbe Anfrage wie in Schritt 3. Erwartet: Antwort kommt schneller, Tool-Calls (`lookup_free_slots`, `present_proposals`, `create_event`) laufen sauber.

- [ ] **Step 7: Switch zurück auf Ollama**

Settings → AI-Provider auf "Ollama". Erwartet: nächste Nachricht wird wieder lokal beantwortet.

- [ ] **Step 8: Key löschen prüfen**

Settings → "ENTFERNEN" beim Key. Erwartet: Anzeige wechselt zu Eingabe-Feld, weiterer Versuch zu Gemini-Switch scheitert mit Key-Fehler.

- [ ] **Step 9: DB-Permissions prüfen**

Run: `ls -la src/Backend/data/nauassist.db`
Expected: `-rw-------` (0600).

- [ ] **Step 10: Audit-Log inspizieren**

Run: `sqlite3 src/Backend/data/nauassist.db "SELECT tool_name, tool_args_json, created_at FROM audit_log WHERE tool_name = 'settings.llm.update' ORDER BY id;"`
Expected: Zeilen pro durchgeführtem Wechsel, kein Key im JSON sichtbar (nur `geminiKeyAction: "set"|"cleared"|"unchanged"`).

- [ ] **Step 11: Falls alles ok — Final-Commit (Verification-Notiz, optional)**

Falls bei der Verifikation kleinere Anpassungen nötig waren, diese committen. Sonst Plan H abgeschlossen.

---

## Self-Review

**Spec coverage:**
- Migration `0005_app_settings.sql` → Task 1 ✓
- `LlmSettings`-Record + `IAppSettingsRepository` + `AppSettingsRepository` → Task 2 ✓
- DB-Permissions-Härtung 0600 → Task 1 (Step 5–8) ✓
- `OpenAICompatibleLlmClient` (Refactor von `OllamaLlmClient`, parametrisiert) → Task 4 ✓
- `OpenAICompatibleLlmOptions` (`Model`, Timeouts, `SystemPrompt`, `Temperature`, `NumCtx`) → Task 3 ✓
- `LlmClientFactory` + Hot-Reload-Wiring (`ILlmClient` Scoped) → Task 5 + Task 7 ✓
- `GeminiOptions` + neue `appsettings.json`-Sektion → Task 3 + Task 7 ✓
- `GetLlmSettings`-Mediator-Handler → Task 6 ✓
- `UpdateLlmSettings`-Mediator-Handler mit Validierung (Provider/Modell-Whitelist/Gemini-ohne-Key) → Task 6 ✓
- `GET /api/settings/llm` + `PUT /api/settings/llm` HTTP-Endpoint → Task 8 ✓
- Audit-Logging im `AuditLogRepository` (ToolName `settings.llm.update`, kein Key-Inhalt) → Task 8 ✓
- Frontend-API-Modul → Task 9 ✓
- Frontend-SettingsPage-LLM-Sektion mit Provider/Modell/API-Key-Rows + Hot-Reload-Feedback → Task 10 ✓
- Mockup-Footer-Cleanup → Task 10 ✓
- Manuelle Verifikation (Regression Ollama, Gemini ohne Key, mit Key, Tool-Call-Roundtrip, Switch-back, Key löschen, DB-Permissions, Audit-Log) → Task 11 ✓

Keine bekannten Lücken.

**Placeholder scan:** Keine TBDs, keine "implement later", keine "Add appropriate error handling"; jeder Schritt zeigt konkreten Code oder konkretes Kommando.

**Type-Konsistenz:**
- `LlmSettings`-Record-Felder (`Provider`, `OllamaModel`, `GeminiModel`, `GeminiApiKey`) sind in allen Tasks identisch verwendet.
- `OpenAICompatibleLlmOptions`-Konstruktor (6 Parameter: `Model`, `InitialTimeoutSeconds`, `TokenTimeoutSeconds`, `SystemPrompt`, `Temperature`, `NumCtx`) ist in Task 3, 4, 5 konsistent.
- `LlmClientFactory.CreateAsync(CancellationToken)` vs. `internal CreateInternalForTestAsync()` — beide explizit definiert in Task 5.
- Settings-Endpoint-DTO (`Provider`, `OllamaModel`, `GeminiModel`, `HasGeminiApiKey`) ist in Task 6 (Response-Record), Task 8 (Endpoint-DTO), Task 9 (Frontend-Typ) und Task 10 (Verwendung) konsistent.
- Frontend `OLLAMA_MODELS`/`GEMINI_MODELS` Konstanten matchen exakt Backend `SupportedModels.Ollama`/`SupportedModels.Gemini`.

Plan ist konsistent zur Spec.
