# Settings-Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mockup-Sektionen der Settings-Page entfernen, Ollama- und Google-Calendar-Konfiguration (inkl. OAuth-Client-Secret) aus `appsettings.json` in `app_settings` (SQLite) heben und über die UI editierbar machen, OAuth-Flow aus der UI startbar.

**Architecture:** Bestehende key-value-Tabelle `app_settings` wird um Ollama- und Calendar-Keys erweitert. `IAppSettingsRepository` bekommt neue Get/Set-Methoden je Domäne. `LlmClientFactory`, `GoogleAuthService`, `FreeSlotCalculator`, `CalendarContextBuilder` und `GoogleCalendarProvider` lesen ab jetzt aus dem Repository statt aus `IOptions`. OAuth-Flow wird im Backend per Memory-Cache + `AuthorizationCodeFlow` direkt umgebaut; UI bekommt Start- und Complete-Endpoints. `SettingsPage.tsx` wird radikal verschlankt auf zwei Sektionen.

**Tech Stack:** .NET 10 (ASP.NET Core Minimal API), Dapper + SQLite, Mediator, Google.Apis.Auth.OAuth2, React 19 + Vite + TypeScript, xUnit + FluentAssertions.

---

## File Structure

**Created:**
- `src/Backend/Features/Infrastructure/Persistence/Migrations/0006_settings_expansion.sql`
- `src/Backend/Features/Settings/OllamaUserSettings.cs`
- `src/Backend/Features/Settings/CalendarUserSettings.cs`
- `src/Backend/Features/Settings/GoogleCredentials.cs`
- `src/Backend/Features/Settings/GetOllamaSettings/GetOllamaSettingsRequest.cs`
- `src/Backend/Features/Settings/GetOllamaSettings/GetOllamaSettingsHandler.cs`
- `src/Backend/Features/Settings/UpdateOllamaSettings/UpdateOllamaSettingsRequest.cs`
- `src/Backend/Features/Settings/UpdateOllamaSettings/UpdateOllamaSettingsHandler.cs`
- `src/Backend/Features/Settings/GetCalendarSettings/GetCalendarSettingsRequest.cs`
- `src/Backend/Features/Settings/GetCalendarSettings/GetCalendarSettingsHandler.cs`
- `src/Backend/Features/Settings/UpdateCalendarSettings/UpdateCalendarSettingsRequest.cs`
- `src/Backend/Features/Settings/UpdateCalendarSettings/UpdateCalendarSettingsHandler.cs`
- `src/Backend/Features/Calendar/Google/AuthSessionStore.cs`
- `src/Backend/Features/Calendar/Google/NotAuthenticatedException.cs`
- `src/Backend/Endpoints/CalendarAuthEndpoints.cs`
- `src/Backend.Tests/Features/Settings/AppSettingsRepository.OllamaTests.cs`
- `src/Backend.Tests/Features/Settings/AppSettingsRepository.CalendarTests.cs`
- `src/Backend.Tests/Features/Settings/AppSettingsRepository.GoogleCredentialsTests.cs`
- `src/Backend.Tests/Features/Settings/UpdateOllamaSettingsHandlerTests.cs`
- `src/Backend.Tests/Features/Settings/UpdateCalendarSettingsHandlerTests.cs`
- `src/Backend.Tests/Features/Calendar/Google/AuthSessionStoreTests.cs`
- `frontend/src/api/calendar-settings.ts`

**Modified:**
- `src/Backend/Features/Settings/IAppSettingsRepository.cs`
- `src/Backend/Features/Settings/AppSettingsRepository.cs`
- `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs`
- `src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs`
- `src/Backend/Features/Calendar/Google/GoogleAuthService.cs`
- `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`
- `src/Backend/Features/Calendar/FreeSlotCalculator.cs` (Konstruktor)
- `src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs`
- `src/Backend/Endpoints/SettingsEndpoints.cs`
- `src/Backend/Program.cs`
- `src/Backend/appsettings.json`
- `src/Backend/appsettings.Development.json`
- `src/Backend/Backend.csproj` (Resource-Embedding für neue Migration läuft automatisch über Wildcard, ggf. nichts zu tun — prüfen)
- `Dockerfile`
- `src/Backend.Tests/Features/Settings/UpdateLlmSettingsHandlerTests.cs` (InMemorySettingsRepo erweitern)
- `src/Backend.Tests/Features/Calendar/CalendarContextBuilderTests.cs`
- `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs`
- `src/Backend.Tests/Features/Agent/AgentRunnerCalendarContextTests.cs`
- `src/Backend.Tests/Features/Agent/AgentRunnerTests.cs`
- `src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs`
- `src/Backend.Tests/Features/Chat/SendMessageHandlerTests.cs`
- `frontend/src/api/settings.ts`
- `frontend/src/components/pages/SettingsPage.tsx`

**Deleted:**
- `src/Backend/Features/Calendar/CalendarOptions.cs`

---

## Phase 1 — Datenmodell + Repository

### Task 1: Migration 0006 anlegen

**Files:**
- Create: `src/Backend/Features/Infrastructure/Persistence/Migrations/0006_settings_expansion.sql`

- [ ] **Step 1: SQL-Migration anlegen**

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
    ('calendar.search_horizon_days',  '14')
ON CONFLICT(key) DO NOTHING;
```

- [ ] **Step 2: Verifizieren dass Migration als embedded resource gefunden wird**

Run:
```bash
grep -n "EmbeddedResource\|<None.*\.sql" src/Backend/Backend.csproj
```

Wenn `.sql` per Wildcard (`<EmbeddedResource Include="**/*.sql">` o.ä.) eingebunden ist, ist nichts weiter zu tun. Falls die existierenden Migrationen einzeln gelistet sind, neuen Eintrag analog zu `0005_app_settings.sql` ergänzen.

- [ ] **Step 3: Build prüfen**

Run:
```bash
dotnet build src/Backend/Backend.csproj
```
Expected: 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Features/Infrastructure/Persistence/Migrations/0006_settings_expansion.sql
git commit -m "Migration 0006: Ollama- und Calendar-Settings in app_settings"
```

---

### Task 2: Settings-Records anlegen

**Files:**
- Create: `src/Backend/Features/Settings/OllamaUserSettings.cs`
- Create: `src/Backend/Features/Settings/CalendarUserSettings.cs`
- Create: `src/Backend/Features/Settings/GoogleCredentials.cs`

- [ ] **Step 1: OllamaUserSettings anlegen**

`src/Backend/Features/Settings/OllamaUserSettings.cs`:
```csharp
namespace NauAssist.Backend.Features.Settings;

public sealed record OllamaUserSettings(
    string Host,
    string? ApiKey,
    int NumCtx,
    double Temperature);
```

- [ ] **Step 2: CalendarUserSettings anlegen**

`src/Backend/Features/Settings/CalendarUserSettings.cs`:
```csharp
namespace NauAssist.Backend.Features.Settings;

public sealed record CalendarUserSettings(
    string CalendarId,
    TimeOnly WorkingHoursStart,
    TimeOnly WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays);
```

- [ ] **Step 3: GoogleCredentials anlegen**

`src/Backend/Features/Settings/GoogleCredentials.cs`:
```csharp
namespace NauAssist.Backend.Features.Settings;

public sealed record GoogleCredentials(string ClientId, string ClientSecret);
```

- [ ] **Step 4: Build prüfen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: 0 Errors.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Settings/OllamaUserSettings.cs \
        src/Backend/Features/Settings/CalendarUserSettings.cs \
        src/Backend/Features/Settings/GoogleCredentials.cs
git commit -m "Settings: Records OllamaUserSettings/CalendarUserSettings/GoogleCredentials"
```

---

### Task 3: IAppSettingsRepository erweitern

**Files:**
- Modify: `src/Backend/Features/Settings/IAppSettingsRepository.cs`
- Modify: `src/Backend.Tests/Features/Settings/UpdateLlmSettingsHandlerTests.cs` (InMemorySettingsRepo erweitern)

- [ ] **Step 1: Interface erweitern**

`src/Backend/Features/Settings/IAppSettingsRepository.cs`:
```csharp
namespace NauAssist.Backend.Features.Settings;

public interface IAppSettingsRepository
{
    Task<LlmSettings> GetLlmAsync(CancellationToken ct);
    Task SetLlmAsync(LlmSettings settings, CancellationToken ct);

    Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct);
    Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct);

    Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct);
    Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct);

    /// <summary>Null, wenn ClientId leer ist; sonst gefülltes Record.</summary>
    Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct);

    /// <summary>Speichert Credentials und löscht in derselben Transaktion alle google_oauth-Einträge.</summary>
    Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct);
}
```

- [ ] **Step 2: Build prüfen — muss FEHLSCHLAGEN**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Errors in `AppSettingsRepository.cs` und `InMemorySettingsRepo` (in `UpdateLlmSettingsHandlerTests.cs`) — Interface nicht implementiert.

- [ ] **Step 3: InMemorySettingsRepo in Tests erweitern**

Im `UpdateLlmSettingsHandlerTests.cs` die `InMemorySettingsRepo`-Klasse durch diese ersetzen (am Ende der Datei):
```csharp
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

    public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
        Task.FromResult(new OllamaUserSettings("http://localhost:11434", null, 16384, 0.3));

    public Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
        Task.FromResult(new CalendarUserSettings(
            "primary", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 14));

    public Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
        Task.FromResult<GoogleCredentials?>(null);

    public Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct) =>
        Task.CompletedTask;
}
```

- [ ] **Step 4: AppSettingsRepository-Stub für neue Methoden (NotImplemented) ergänzen**

In `src/Backend/Features/Settings/AppSettingsRepository.cs` — unten in der Klasse anhängen:
```csharp
public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
    throw new NotImplementedException();
public Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct) =>
    throw new NotImplementedException();
public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
    throw new NotImplementedException();
public Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct) =>
    throw new NotImplementedException();
public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
    throw new NotImplementedException();
public Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct) =>
    throw new NotImplementedException();
```

Diese Stubs werden in Tasks 4–6 implementiert.

- [ ] **Step 5: Build + Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateLlmSettingsHandlerTests"`
Expected: alle UpdateLlm-Tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Backend/Features/Settings/IAppSettingsRepository.cs \
        src/Backend/Features/Settings/AppSettingsRepository.cs \
        src/Backend.Tests/Features/Settings/UpdateLlmSettingsHandlerTests.cs
git commit -m "IAppSettingsRepository: Methoden für Ollama/Calendar/Google-Credentials"
```

---

### Task 4: AppSettingsRepository.GetOllamaAsync / SetOllamaAsync (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Settings/AppSettingsRepository.OllamaTests.cs`
- Modify: `src/Backend/Features/Settings/AppSettingsRepository.cs`

- [ ] **Step 1: Failing-Tests schreiben**

`src/Backend.Tests/Features/Settings/AppSettingsRepository.OllamaTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryOllamaTests
{
    [Fact]
    public async Task GetOllama_ReturnsSeededDefaults()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var s = await repo.GetOllamaAsync(CancellationToken.None);

        s.Host.Should().Be("http://localhost:11434");
        s.ApiKey.Should().BeNull();
        s.NumCtx.Should().Be(16384);
        s.Temperature.Should().Be(0.3);
    }

    [Fact]
    public async Task SetOllama_RoundtripsAllFields()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetOllamaAsync(
            new OllamaUserSettings("https://ollama.lan:11434", "secret-key", 8192, 0.7),
            CancellationToken.None);

        var loaded = await repo.GetOllamaAsync(CancellationToken.None);

        loaded.Host.Should().Be("https://ollama.lan:11434");
        loaded.ApiKey.Should().Be("secret-key");
        loaded.NumCtx.Should().Be(8192);
        loaded.Temperature.Should().Be(0.7);
    }

    [Fact]
    public async Task SetOllama_EmptyApiKey_ReadsBackAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", ApiKey: "", 16384, 0.3),
            CancellationToken.None);

        var loaded = await repo.GetOllamaAsync(CancellationToken.None);
        loaded.ApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetOllama_NullApiKey_PersistsAsEmpty()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", ApiKey: null, 16384, 0.3),
            CancellationToken.None);

        using var conn = db.AppDb.OpenConnection();
        var raw = await conn.ExecuteScalarAsync<string>(
            "SELECT value FROM app_settings WHERE key = 'ollama.api_key';");
        raw.Should().Be("");
    }
}
```

- [ ] **Step 2: Tests laufen — müssen FEHLSCHLAGEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryOllamaTests"`
Expected: FAIL mit `NotImplementedException`.

- [ ] **Step 3: Implementierung in `AppSettingsRepository`**

In `src/Backend/Features/Settings/AppSettingsRepository.cs`:

Konstanten oben in der Klasse erweitern (über den bestehenden):
```csharp
private const string KeyOllamaHost = "ollama.host";
private const string KeyOllamaApiKey = "ollama.api_key";
private const string KeyOllamaNumCtx = "ollama.num_ctx";
private const string KeyOllamaTemperature = "ollama.temperature";
```

Die `NotImplementedException`-Stubs für `GetOllamaAsync`/`SetOllamaAsync` ersetzen durch:
```csharp
public async Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
        "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2, @k3, @k4);",
        new
        {
            k1 = KeyOllamaHost,
            k2 = KeyOllamaApiKey,
            k3 = KeyOllamaNumCtx,
            k4 = KeyOllamaTemperature,
        },
        cancellationToken: ct));

    var map = rows.ToDictionary(r => r.Key, r => r.Value);
    var apiKeyRaw = map.GetValueOrDefault(KeyOllamaApiKey, "");

    return new OllamaUserSettings(
        Host: map.GetValueOrDefault(KeyOllamaHost, "http://localhost:11434"),
        ApiKey: string.IsNullOrEmpty(apiKeyRaw) ? null : apiKeyRaw,
        NumCtx: int.Parse(map.GetValueOrDefault(KeyOllamaNumCtx, "16384")),
        Temperature: double.Parse(
            map.GetValueOrDefault(KeyOllamaTemperature, "0.3"),
            System.Globalization.CultureInfo.InvariantCulture));
}

public async Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    using var tx = conn.BeginTransaction();
    try
    {
        await UpsertAsync(conn, tx, KeyOllamaHost, settings.Host, ct);
        await UpsertAsync(conn, tx, KeyOllamaApiKey, settings.ApiKey ?? "", ct);
        await UpsertAsync(conn, tx, KeyOllamaNumCtx, settings.NumCtx.ToString(), ct);
        await UpsertAsync(conn, tx, KeyOllamaTemperature,
            settings.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        tx.Commit();
    }
    catch
    {
        tx.Rollback();
        throw;
    }
}
```

- [ ] **Step 4: Tests laufen — müssen PASSEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryOllamaTests"`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Settings/AppSettingsRepository.cs \
        src/Backend.Tests/Features/Settings/AppSettingsRepository.OllamaTests.cs
git commit -m "AppSettingsRepository: GetOllama/SetOllama mit Persistenz"
```

---

### Task 5: AppSettingsRepository.GetCalendarAsync / SetCalendarAsync (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Settings/AppSettingsRepository.CalendarTests.cs`
- Modify: `src/Backend/Features/Settings/AppSettingsRepository.cs`

- [ ] **Step 1: Failing-Tests schreiben**

`src/Backend.Tests/Features/Settings/AppSettingsRepository.CalendarTests.cs`:
```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryCalendarTests
{
    [Fact]
    public async Task GetCalendar_ReturnsSeededDefaults()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var s = await repo.GetCalendarAsync(CancellationToken.None);

        s.CalendarId.Should().Be("primary");
        s.WorkingHoursStart.Should().Be(new TimeOnly(9, 0));
        s.WorkingHoursEnd.Should().Be(new TimeOnly(18, 0));
        s.DefaultDurationMinutes.Should().Be(60);
        s.SearchHorizonDays.Should().Be(14);
    }

    [Fact]
    public async Task SetCalendar_RoundtripsAllFields()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetCalendarAsync(
            new CalendarUserSettings(
                CalendarId: "work@nau.studio",
                WorkingHoursStart: new TimeOnly(7, 30),
                WorkingHoursEnd: new TimeOnly(19, 45),
                DefaultDurationMinutes: 30,
                SearchHorizonDays: 21),
            CancellationToken.None);

        var loaded = await repo.GetCalendarAsync(CancellationToken.None);

        loaded.CalendarId.Should().Be("work@nau.studio");
        loaded.WorkingHoursStart.Should().Be(new TimeOnly(7, 30));
        loaded.WorkingHoursEnd.Should().Be(new TimeOnly(19, 45));
        loaded.DefaultDurationMinutes.Should().Be(30);
        loaded.SearchHorizonDays.Should().Be(21);
    }
}
```

- [ ] **Step 2: Tests laufen — müssen FEHLSCHLAGEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryCalendarTests"`
Expected: FAIL mit `NotImplementedException`.

- [ ] **Step 3: Implementierung in `AppSettingsRepository`**

Konstanten ergänzen:
```csharp
private const string KeyCalendarId      = "calendar.google.calendar_id";
private const string KeyWorkingStart    = "calendar.working_hours_start";
private const string KeyWorkingEnd      = "calendar.working_hours_end";
private const string KeyDefaultDuration = "calendar.default_duration_min";
private const string KeySearchHorizon   = "calendar.search_horizon_days";
```

`GetCalendarAsync`/`SetCalendarAsync` ersetzen:
```csharp
public async Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
        "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2, @k3, @k4, @k5);",
        new
        {
            k1 = KeyCalendarId,
            k2 = KeyWorkingStart,
            k3 = KeyWorkingEnd,
            k4 = KeyDefaultDuration,
            k5 = KeySearchHorizon,
        },
        cancellationToken: ct));

    var map = rows.ToDictionary(r => r.Key, r => r.Value);

    return new CalendarUserSettings(
        CalendarId: map.GetValueOrDefault(KeyCalendarId, "primary"),
        WorkingHoursStart: TimeOnly.Parse(map.GetValueOrDefault(KeyWorkingStart, "09:00")),
        WorkingHoursEnd: TimeOnly.Parse(map.GetValueOrDefault(KeyWorkingEnd, "18:00")),
        DefaultDurationMinutes: int.Parse(map.GetValueOrDefault(KeyDefaultDuration, "60")),
        SearchHorizonDays: int.Parse(map.GetValueOrDefault(KeySearchHorizon, "14")));
}

public async Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    using var tx = conn.BeginTransaction();
    try
    {
        await UpsertAsync(conn, tx, KeyCalendarId, settings.CalendarId, ct);
        await UpsertAsync(conn, tx, KeyWorkingStart, settings.WorkingHoursStart.ToString("HH:mm"), ct);
        await UpsertAsync(conn, tx, KeyWorkingEnd, settings.WorkingHoursEnd.ToString("HH:mm"), ct);
        await UpsertAsync(conn, tx, KeyDefaultDuration, settings.DefaultDurationMinutes.ToString(), ct);
        await UpsertAsync(conn, tx, KeySearchHorizon, settings.SearchHorizonDays.ToString(), ct);
        tx.Commit();
    }
    catch
    {
        tx.Rollback();
        throw;
    }
}
```

- [ ] **Step 4: Tests laufen — müssen PASSEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryCalendarTests"`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Settings/AppSettingsRepository.cs \
        src/Backend.Tests/Features/Settings/AppSettingsRepository.CalendarTests.cs
git commit -m "AppSettingsRepository: GetCalendar/SetCalendar mit Persistenz"
```

---

### Task 6: AppSettingsRepository.GoogleCredentials + Token-Flush (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Settings/AppSettingsRepository.GoogleCredentialsTests.cs`
- Modify: `src/Backend/Features/Settings/AppSettingsRepository.cs`

- [ ] **Step 1: Failing-Tests schreiben**

`src/Backend.Tests/Features/Settings/AppSettingsRepository.GoogleCredentialsTests.cs`:
```csharp
using Dapper;
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryGoogleCredentialsTests
{
    [Fact]
    public async Task GetGoogleCredentials_FreshDb_ReturnsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var creds = await repo.GetGoogleCredentialsAsync(CancellationToken.None);

        creds.Should().BeNull();
    }

    [Fact]
    public async Task SetThenGet_Roundtrips()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetGoogleCredentialsAsync(
            new GoogleCredentials("123.apps.googleusercontent.com", "GOCSPX-secret"),
            CancellationToken.None);

        var loaded = await repo.GetGoogleCredentialsAsync(CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.ClientId.Should().Be("123.apps.googleusercontent.com");
        loaded.ClientSecret.Should().Be("GOCSPX-secret");
    }

    [Fact]
    public async Task SetGoogleCredentials_ClearsExistingOauthTokens()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        using (var conn = db.AppDb.OpenConnection())
        {
            await conn.ExecuteAsync(
                @"INSERT INTO google_oauth(key, value, updated_at)
                  VALUES('test-key', X'00', @ts);",
                new { ts = DateTimeOffset.UtcNow.ToString("O") });
        }

        await repo.SetGoogleCredentialsAsync(
            new GoogleCredentials("new-id", "new-secret"),
            CancellationToken.None);

        using (var conn = db.AppDb.OpenConnection())
        {
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM google_oauth;");
            count.Should().Be(0);
        }
    }
}
```

- [ ] **Step 2: Tests laufen — müssen FEHLSCHLAGEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryGoogleCredentialsTests"`
Expected: FAIL mit `NotImplementedException`.

- [ ] **Step 3: Implementierung in `AppSettingsRepository`**

Konstanten ergänzen:
```csharp
private const string KeyGoogleClientId     = "calendar.google.client_id";
private const string KeyGoogleClientSecret = "calendar.google.client_secret";
```

`GetGoogleCredentialsAsync`/`SetGoogleCredentialsAsync` ersetzen:
```csharp
public async Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
        "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2);",
        new { k1 = KeyGoogleClientId, k2 = KeyGoogleClientSecret },
        cancellationToken: ct));

    var map = rows.ToDictionary(r => r.Key, r => r.Value);
    var clientId = map.GetValueOrDefault(KeyGoogleClientId, "");
    var clientSecret = map.GetValueOrDefault(KeyGoogleClientSecret, "");

    if (string.IsNullOrEmpty(clientId))
    {
        return null;
    }

    return new GoogleCredentials(clientId, clientSecret);
}

public async Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    using var tx = conn.BeginTransaction();
    try
    {
        await UpsertAsync(conn, tx, KeyGoogleClientId, credentials.ClientId, ct);
        await UpsertAsync(conn, tx, KeyGoogleClientSecret, credentials.ClientSecret, ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM google_oauth;",
            transaction: tx,
            cancellationToken: ct));
        tx.Commit();
    }
    catch
    {
        tx.Rollback();
        throw;
    }
}
```

- [ ] **Step 4: Tests laufen — müssen PASSEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AppSettingsRepositoryGoogleCredentialsTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Settings/AppSettingsRepository.cs \
        src/Backend.Tests/Features/Settings/AppSettingsRepository.GoogleCredentialsTests.cs
git commit -m "AppSettingsRepository: GoogleCredentials + automatischer Token-Flush"
```

---

## Phase 2 — Ollama-Settings durchverdrahten

### Task 7: OllamaOptions verschlanken + LlmClientFactory aus DB

**Files:**
- Modify: `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs`
- Modify: `src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs`

- [ ] **Step 1: OllamaOptions auf reine Bootstrap-Defaults reduzieren**

`src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs`:
```csharp
namespace NauAssist.Backend.Features.Infrastructure.Llm.Ollama;

public sealed class OllamaOptions
{
    public string Model { get; set; } = "qwen2.5:7b-instruct";
    public int InitialTimeoutSeconds { get; set; } = 60;
    public int TokenTimeoutSeconds { get; set; } = 30;
    public string? SystemPrompt { get; set; }
}
```

Entfernt: `Host`, `ApiKey`, `NumCtx`, `Temperature` — die kommen jetzt aus der DB. `Model` bleibt formell, wird aber von `LlmSettings.OllamaModel` (in `app_settings`) überschrieben — analog zum Status quo.

- [ ] **Step 2: LlmClientFactory.BuildOllama umbauen**

In `src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs`:

Die Methode `BuildOllama` ersetzen durch:
```csharp
private async Task<(ILlmClient, HttpClient)> BuildOllamaAsync(LlmSettings s, CancellationToken ct)
{
    var ollamaUser = await _settings.GetOllamaAsync(ct);

    var http = _httpFactory.CreateClient("Ollama");
    http.BaseAddress = new Uri(ollamaUser.Host.TrimEnd('/') + "/v1/");

    if (!string.IsNullOrWhiteSpace(ollamaUser.ApiKey))
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ollamaUser.ApiKey);
    }

    var options = new OpenAICompatibleLlmOptions(
        Model: s.OllamaModel,
        InitialTimeoutSeconds: _ollamaDefaults.InitialTimeoutSeconds,
        TokenTimeoutSeconds: _ollamaDefaults.TokenTimeoutSeconds,
        SystemPrompt: _ollamaDefaults.SystemPrompt,
        Temperature: ollamaUser.Temperature,
        NumCtx: ollamaUser.NumCtx);

    var logger = _loggerFactory.CreateLogger<OpenAICompatibleLlmClient>();
    return (new OpenAICompatibleLlmClient(http, options, logger), http);
}
```

Im `CreateInternalAsync`-Switch den Aufruf anpassen:
```csharp
return s.Provider switch
{
    LlmProviders.Ollama => await BuildOllamaAsync(s, ct),
    LlmProviders.Gemini => BuildGemini(s),
    _ => throw new InvalidOperationException($"Unbekannter LLM-Provider: '{s.Provider}'."),
};
```

- [ ] **Step 3: Build prüfen + bestehende Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: 0 Build-Errors, alle bestehenden Tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs \
        src/Backend/Features/Infrastructure/Llm/LlmClientFactory.cs
git commit -m "Ollama: Host/ApiKey/NumCtx/Temperature aus app_settings statt IOptions"
```

---

### Task 8: UpdateOllamaSettings-Handler (TDD)

**Files:**
- Create: `src/Backend/Features/Settings/UpdateOllamaSettings/UpdateOllamaSettingsRequest.cs`
- Create: `src/Backend/Features/Settings/UpdateOllamaSettings/UpdateOllamaSettingsHandler.cs`
- Create: `src/Backend.Tests/Features/Settings/UpdateOllamaSettingsHandlerTests.cs`

- [ ] **Step 1: Request/Result-Records anlegen**

`src/Backend/Features/Settings/UpdateOllamaSettings/UpdateOllamaSettingsRequest.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

public sealed record UpdateOllamaSettingsRequest(
    string Host,
    string? ApiKey,
    int NumCtx,
    double Temperature) : IRequest<UpdateOllamaSettingsResult>;

public sealed record UpdateOllamaSettingsResult(bool Ok, string? Error);
```

- [ ] **Step 2: Failing-Tests schreiben**

`src/Backend.Tests/Features/Settings/UpdateOllamaSettingsHandlerTests.cs`:
```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateOllamaSettingsHandlerTests
{
    [Fact]
    public async Task Handle_InvalidHostUri_ReturnsError()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("not a url", null, 16384, 0.3),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("Host");
    }

    [Fact]
    public async Task Handle_NegativeNumCtx_ReturnsError()
    {
        var handler = new UpdateOllamaSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://localhost:11434", null, -1, 0.3),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("NumCtx");
    }

    [Fact]
    public async Task Handle_TemperatureOutOfRange_ReturnsError()
    {
        var handler = new UpdateOllamaSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://h", null, 8192, 5.0),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("Temperature");
    }

    [Fact]
    public async Task Handle_NullApiKey_DoesNotOverwriteExistingKey()
    {
        var repo = new InMemoryRepo();
        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", "kept", 8192, 0.3),
            CancellationToken.None);
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://h2", ApiKey: null, NumCtx: 4096, Temperature: 0.5),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Current.ApiKey.Should().Be("kept");
        repo.Current.Host.Should().Be("http://h2");
    }

    [Fact]
    public async Task Handle_EmptyApiKey_ClearsKey()
    {
        var repo = new InMemoryRepo();
        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", "to-delete", 8192, 0.3),
            CancellationToken.None);
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://h", ApiKey: "", NumCtx: 8192, Temperature: 0.3),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Current.ApiKey.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidUpdate_Persists()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("https://ollama.lan", "key123", 8192, 0.7),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Current.Host.Should().Be("https://ollama.lan");
        repo.Current.ApiKey.Should().Be("key123");
        repo.Current.NumCtx.Should().Be(8192);
        repo.Current.Temperature.Should().Be(0.7);
    }

    private sealed class InMemoryRepo : IAppSettingsRepository
    {
        public OllamaUserSettings Current { get; private set; } =
            new("http://localhost:11434", null, 16384, 0.3);

        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            Task.FromResult(Current);
        public Task SetOllamaAsync(OllamaUserSettings s, CancellationToken ct)
        {
            Current = s;
            return Task.CompletedTask;
        }

        // Unused stubs:
        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) =>
            Task.FromResult(new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null));
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct) => Task.CompletedTask;
        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            Task.FromResult(new CalendarUserSettings("primary", new(9, 0), new(18, 0), 60, 14));
        public Task SetCalendarAsync(CalendarUserSettings s, CancellationToken ct) =>
            Task.CompletedTask;
        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            Task.FromResult<GoogleCredentials?>(null);
        public Task SetGoogleCredentialsAsync(GoogleCredentials c, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Tests laufen — müssen FEHLSCHLAGEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateOllamaSettingsHandlerTests"`
Expected: Compile-Error — `UpdateOllamaSettingsHandler` existiert nicht.

- [ ] **Step 4: Handler implementieren**

`src/Backend/Features/Settings/UpdateOllamaSettings/UpdateOllamaSettingsHandler.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

public sealed class UpdateOllamaSettingsHandler
    : IRequestHandler<UpdateOllamaSettingsRequest, UpdateOllamaSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public UpdateOllamaSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<UpdateOllamaSettingsResult> Handle(
        UpdateOllamaSettingsRequest request,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Host, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return new UpdateOllamaSettingsResult(false,
                "Host muss eine absolute http(s)-URL sein.");
        }

        if (request.NumCtx <= 0 || request.NumCtx > 1_000_000)
        {
            return new UpdateOllamaSettingsResult(false,
                "NumCtx muss zwischen 1 und 1.000.000 liegen.");
        }

        if (request.Temperature < 0.0 || request.Temperature > 2.0)
        {
            return new UpdateOllamaSettingsResult(false,
                "Temperature muss zwischen 0.0 und 2.0 liegen.");
        }

        var existing = await _settings.GetOllamaAsync(ct);

        string? newKey = request.ApiKey switch
        {
            null => existing.ApiKey,
            ""   => null,
            _    => request.ApiKey,
        };

        await _settings.SetOllamaAsync(
            new OllamaUserSettings(request.Host, newKey, request.NumCtx, request.Temperature),
            ct);

        return new UpdateOllamaSettingsResult(true, null);
    }
}
```

- [ ] **Step 5: Tests laufen — müssen PASSEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateOllamaSettingsHandlerTests"`
Expected: 6 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Backend/Features/Settings/UpdateOllamaSettings/ \
        src/Backend.Tests/Features/Settings/UpdateOllamaSettingsHandlerTests.cs
git commit -m "UpdateOllamaSettings-Handler mit Validierung + Tri-State-ApiKey"
```

---

### Task 9: GetOllamaSettings-Handler + Endpoints

**Files:**
- Create: `src/Backend/Features/Settings/GetOllamaSettings/GetOllamaSettingsRequest.cs`
- Create: `src/Backend/Features/Settings/GetOllamaSettings/GetOllamaSettingsHandler.cs`
- Modify: `src/Backend/Endpoints/SettingsEndpoints.cs`

- [ ] **Step 1: Get-Request/Result anlegen**

`src/Backend/Features/Settings/GetOllamaSettings/GetOllamaSettingsRequest.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.GetOllamaSettings;

public sealed record GetOllamaSettingsRequest : IRequest<GetOllamaSettingsResult>;

public sealed record GetOllamaSettingsResult(
    string Host,
    bool HasApiKey,
    int NumCtx,
    double Temperature);
```

- [ ] **Step 2: Get-Handler anlegen**

`src/Backend/Features/Settings/GetOllamaSettings/GetOllamaSettingsHandler.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.GetOllamaSettings;

public sealed class GetOllamaSettingsHandler
    : IRequestHandler<GetOllamaSettingsRequest, GetOllamaSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public GetOllamaSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<GetOllamaSettingsResult> Handle(
        GetOllamaSettingsRequest request, CancellationToken ct)
    {
        var s = await _settings.GetOllamaAsync(ct);
        return new GetOllamaSettingsResult(
            Host: s.Host,
            HasApiKey: !string.IsNullOrEmpty(s.ApiKey),
            NumCtx: s.NumCtx,
            Temperature: s.Temperature);
    }
}
```

- [ ] **Step 3: Endpoints registrieren**

In `src/Backend/Endpoints/SettingsEndpoints.cs`, im `MapSettingsEndpoints`-Body **vor** dem `return app;` ergänzen:

```csharp
app.MapGet("/api/settings/ollama", async (IMediator mediator, CancellationToken ct) =>
{
    var r = await mediator.Send(new GetOllamaSettingsRequest(), ct);
    return Results.Ok(new OllamaSettingsDto(r.Host, r.HasApiKey, r.NumCtx, r.Temperature));
});

app.MapPut("/api/settings/ollama", async (
    UpdateOllamaSettingsPayload payload,
    IMediator mediator,
    AuditLogRepository audit,
    Func<DateTimeOffset> clock,
    CancellationToken ct) =>
{
    var request = new UpdateOllamaSettingsRequest(
        Host: payload.Host ?? "",
        ApiKey: payload.ApiKey,
        NumCtx: payload.NumCtx,
        Temperature: payload.Temperature);

    var result = await mediator.Send(request, ct);
    if (!result.Ok) return Results.BadRequest(new { error = result.Error });

    var args = JsonSerializer.Serialize(new
    {
        host = payload.Host,
        numCtx = payload.NumCtx,
        temperature = payload.Temperature,
        apiKeyAction = payload.ApiKey switch { null => "unchanged", "" => "cleared", _ => "set" },
    });
    await audit.AppendAsync(
        new AuditEntry(0, null, "settings.ollama.update", args, "{\"ok\":true}", null, clock()),
        ct);

    return Results.Ok(new { ok = true });
});
```

Am Ende der Klasse zwei DTOs ergänzen:
```csharp
public sealed record UpdateOllamaSettingsPayload(
    string? Host,
    string? ApiKey,
    int NumCtx,
    double Temperature);

private sealed record OllamaSettingsDto(
    string Host,
    bool HasApiKey,
    int NumCtx,
    double Temperature);
```

Imports oben ergänzen:
```csharp
using NauAssist.Backend.Features.Settings.GetOllamaSettings;
using NauAssist.Backend.Features.Settings.UpdateOllamaSettings;
```

- [ ] **Step 4: Build + Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: 0 Errors, alle Tests PASS.

- [ ] **Step 5: Smoke-Test manuell**

Run:
```bash
dotnet run --project src/Backend &
sleep 5
curl -sS http://localhost:5000/api/settings/ollama
kill %1
```
Expected: JSON `{"host":"http://localhost:11434","hasApiKey":false,"numCtx":16384,"temperature":0.3}` (Port ggf. anpassen — siehe `Properties/launchSettings.json`).

- [ ] **Step 6: Commit**

```bash
git add src/Backend/Features/Settings/GetOllamaSettings/ \
        src/Backend/Endpoints/SettingsEndpoints.cs
git commit -m "Endpoints: GET/PUT /api/settings/ollama"
```

---

### Task 10: Ollama-Verbindung-testen-Endpoint

**Files:**
- Modify: `src/Backend/Endpoints/SettingsEndpoints.cs`

- [ ] **Step 1: Endpoint hinzufügen**

In `MapSettingsEndpoints` ergänzen:
```csharp
app.MapPost("/api/settings/ollama/test", async (
    TestOllamaPayload payload,
    IHttpClientFactory httpFactory,
    CancellationToken ct) =>
{
    if (!Uri.TryCreate(payload.Host, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "http" && uri.Scheme != "https"))
    {
        return Results.Ok(new TestOllamaResult(false, null, "Host muss absolute http(s)-URL sein."));
    }

    var http = httpFactory.CreateClient("Ollama");
    http.Timeout = TimeSpan.FromSeconds(5);
    var req = new HttpRequestMessage(HttpMethod.Get,
        new Uri(new Uri(payload.Host.TrimEnd('/') + "/"), "api/tags"));
    if (!string.IsNullOrWhiteSpace(payload.ApiKey))
    {
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", payload.ApiKey);
    }

    try
    {
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            return Results.Ok(new TestOllamaResult(false, null, $"HTTP {(int)res.StatusCode}"));
        }

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var models = doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
                 .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                 .Where(n => n is not null).Cast<string>().ToArray()
            : Array.Empty<string>();

        return Results.Ok(new TestOllamaResult(true, models, null));
    }
    catch (Exception ex)
    {
        return Results.Ok(new TestOllamaResult(false, null, ex.Message));
    }
});
```

Am Ende der Klasse:
```csharp
public sealed record TestOllamaPayload(string Host, string? ApiKey);
private sealed record TestOllamaResult(bool Ok, string[]? Models, string? Error);
```

Import oben sicherstellen:
```csharp
using System.Text.Json;
```

- [ ] **Step 2: Build prüfen**

Run: `dotnet build`
Expected: 0 Errors.

- [ ] **Step 3: Smoke-Test (mit lokalem Ollama oder Fake-URL)**

Run:
```bash
dotnet run --project src/Backend &
sleep 5
curl -sS -X POST http://localhost:5000/api/settings/ollama/test \
  -H 'Content-Type: application/json' \
  -d '{"host":"http://localhost:11434","apiKey":null}'
kill %1
```
Expected: Wenn Ollama läuft → `{"ok":true,"models":[…],"error":null}`. Wenn nicht → `{"ok":false,"models":null,"error":"…"}`. **Beide Fälle sind Erfolg** — der Endpoint soll nicht 5xx werfen.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Endpoints/SettingsEndpoints.cs
git commit -m "Endpoint: POST /api/settings/ollama/test (Verbindungstest)"
```

---

## Phase 3 — Calendar-Settings durchverdrahten

### Task 11: FreeSlotCalculator auf Scoped umstellen

**Files:**
- Modify: `src/Backend/Features/Calendar/FreeSlotCalculator.cs`
- Modify: `src/Backend/Program.cs`
- Modify: `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs` (falls Konstruktor-Tests brechen)

Hinweis: Aktuell ist `FreeSlotCalculator` mit konkreten `TimeOnly`-Werten als Singleton registriert. Ab jetzt wird er pro Scope frisch aus `IAppSettingsRepository` instanziiert.

- [ ] **Step 1: Bestehenden FreeSlotCalculator-Konstruktor inspizieren**

Run: `grep -n "public FreeSlotCalculator" src/Backend/Features/Calendar/FreeSlotCalculator.cs`

Den Konstruktor unverändert lassen — er erwartet `(TimeZoneInfo, TimeOnly start, TimeOnly end, DayOfWeekFlags flags)`. Wir bauen ihn jetzt **außen** in einer Factory.

- [ ] **Step 2: Program.cs umstellen**

In `src/Backend/Program.cs` den existierenden Block:
```csharp
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CalendarOptions>>().Value;
    return new FreeSlotCalculator(
        sp.GetRequiredService<TimeZoneInfo>(),
        TimeOnly.Parse(opts.WorkingHoursStart),
        TimeOnly.Parse(opts.WorkingHoursEnd),
        DayOfWeekFlags.WeekdaysOnly);
});
```
ersetzen durch:
```csharp
builder.Services.AddScoped(sp =>
{
    var settings = sp.GetRequiredService<IAppSettingsRepository>();
    var cal = settings.GetCalendarAsync(CancellationToken.None).GetAwaiter().GetResult();
    return new FreeSlotCalculator(
        sp.GetRequiredService<TimeZoneInfo>(),
        cal.WorkingHoursStart,
        cal.WorkingHoursEnd,
        DayOfWeekFlags.WeekdaysOnly);
});
```

- [ ] **Step 3: Build prüfen + alle Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`

Erwartung: Build kann an dieser Stelle wegen anderen `CalendarOptions`-Konsumenten (z.B. `GoogleCalendarProvider`, `CalendarContextBuilder`) noch fehlschlagen — die hängen wir in den nächsten Tasks ab. Wenn nur `Program.cs` rot ist, ist das OK; weiterarbeiten. Falls `CalendarOptions` selbst noch existiert, sollte der Build aktuell durchgehen (wir entfernen die Klasse erst in Task 14).

Falls Test-Konstruktor-Aufrufe (`new FreeSlotCalculator(...)`) brechen: das passiert erst, wenn wir die Signatur ändern — hier ist sie identisch, also keine Auswirkung.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Program.cs
git commit -m "FreeSlotCalculator: Scoped + WorkingHours aus app_settings"
```

---

### Task 12: CalendarContextBuilder + GoogleCalendarProvider aus DB

**Files:**
- Modify: `src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs`
- Modify: `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`
- Modify: `src/Backend/Program.cs`
- Modify: alle Tests, die `Options.Create(new CalendarOptions {…})` an `CalendarContextBuilder` durchreichen (siehe File-Liste oben)

- [ ] **Step 1: CalendarContextBuilder umbauen**

In `src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs`:

Konstruktor und Field ersetzen:
```csharp
private readonly IAppSettingsRepository _settings;
// statt: private readonly CalendarOptions _options;

public CalendarContextBuilder(
    ICalendarProvider provider,
    IAppSettingsRepository settings,
    TimeZoneInfo tz)
{
    _provider = provider;
    _settings = settings;
    _tz = tz;
}
```

Imports anpassen: `using NauAssist.Backend.Features.Settings;`. Den `using Microsoft.Extensions.Options;` entfernen, falls nicht anderweitig genutzt.

Innerhalb der Build-Methode (oder wo `_options.SearchHorizonDays` benutzt wird): vorher
```csharp
var horizon = _options.SearchHorizonDays;
```
ersetzen durch:
```csharp
var cal = await _settings.GetCalendarAsync(ct);
var horizon = cal.SearchHorizonDays;
```
Falls die Methode bisher synchron war: `async` machen, `CancellationToken` durchreichen. Aufrufer entsprechend `await`-en (Mediator-Handler ist eh `async`).

- [ ] **Step 2: GoogleCalendarProvider umbauen**

In `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`:

Konstruktor und Field ersetzen:
```csharp
private readonly IAppSettingsRepository _settings;

public GoogleCalendarProvider(
    GoogleAuthService auth,
    IAppSettingsRepository settings,
    ILogger<GoogleCalendarProvider> logger)
{
    _auth = auth;
    _settings = settings;
    _logger = logger;
}
```

Imports: `using NauAssist.Backend.Features.Settings;` ergänzen, `Microsoft.Extensions.Options` ggf. entfernen.

Überall wo `_options.GoogleCalendarId` benutzt wird, durch DB-Call ersetzen — z.B.:
```csharp
var cal = await _settings.GetCalendarAsync(ct);
// dann cal.CalendarId statt _options.GoogleCalendarId
```

(Hinweis: `GoogleCalendarProvider` muss u.U. ebenfalls Scoped statt Singleton werden. In Program.cs ist er aktuell Singleton — ändern auf Scoped in Step 3.)

- [ ] **Step 3: DI-Registrierung anpassen**

In `src/Backend/Program.cs` die Zeile
```csharp
builder.Services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
```
ersetzen durch:
```csharp
builder.Services.AddScoped<ICalendarProvider, GoogleCalendarProvider>();
```

`GoogleAuthService` bleibt Singleton (kein DB-Lookup im Hot-Path beim Build → wird in Task 14 nochmal angefasst). Die `builder.Services.Configure<CalendarOptions>(...)` Zeile noch nicht entfernen — kommt in Task 14.

- [ ] **Step 4: Test-Aufrufe anpassen**

In jedem dieser Files den `CalendarContextBuilder`-Konstruktor-Aufruf umstellen:

`src/Backend.Tests/Features/Calendar/CalendarContextBuilderTests.cs`,
`src/Backend.Tests/Features/Agent/AgentRunnerCalendarContextTests.cs`,
`src/Backend.Tests/Features/Agent/AgentRunnerTests.cs`,
`src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs`,
`src/Backend.Tests/Features/Chat/SendMessageHandlerTests.cs`:

```csharp
// vorher:
new CalendarContextBuilder(provider, Options.Create(new CalendarOptions { SearchHorizonDays = 14 }), Berlin);
// nachher:
new CalendarContextBuilder(provider, new FakeSettingsRepo(searchHorizon: 14), Berlin);
```

Dafür einmalig einen Helper anlegen — `src/Backend.Tests/Helpers/FakeSettingsRepo.cs`:
```csharp
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Tests.Helpers;

public sealed class FakeSettingsRepo : IAppSettingsRepository
{
    private readonly CalendarUserSettings _calendar;

    public FakeSettingsRepo(int searchHorizon = 14)
    {
        _calendar = new CalendarUserSettings(
            "primary", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, searchHorizon);
    }

    public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
        Task.FromResult(_calendar);
    public Task SetCalendarAsync(CalendarUserSettings s, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<LlmSettings> GetLlmAsync(CancellationToken ct) =>
        Task.FromResult(new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null));
    public Task SetLlmAsync(LlmSettings s, CancellationToken ct) => Task.CompletedTask;
    public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
        Task.FromResult(new OllamaUserSettings("http://localhost:11434", null, 16384, 0.3));
    public Task SetOllamaAsync(OllamaUserSettings s, CancellationToken ct) => Task.CompletedTask;
    public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
        Task.FromResult<GoogleCredentials?>(null);
    public Task SetGoogleCredentialsAsync(GoogleCredentials c, CancellationToken ct) =>
        Task.CompletedTask;
}
```

Alle `Options.Create(new CalendarOptions {…})`-Aufrufe in Tests durch `new FakeSettingsRepo(searchHorizon: X)` ersetzen. `using Microsoft.Extensions.Options;` und `using NauAssist.Backend.Features.Calendar;` ggf. unbenutzte Imports entfernen.

- [ ] **Step 5: Build + Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: 0 Build-Errors, alle Tests PASS (bis auf eventuell brechende `CalendarModelTests.CalendarOptions_HasReasonableDefaults` — fixen wir in Task 14).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Calendar-Konsumenten lesen IAppSettingsRepository statt CalendarOptions"
```

---

### Task 13: GetCalendarSettings + UpdateCalendarSettings Handler (TDD)

**Files:**
- Create: `src/Backend/Features/Settings/GetCalendarSettings/GetCalendarSettingsRequest.cs`
- Create: `src/Backend/Features/Settings/GetCalendarSettings/GetCalendarSettingsHandler.cs`
- Create: `src/Backend/Features/Settings/UpdateCalendarSettings/UpdateCalendarSettingsRequest.cs`
- Create: `src/Backend/Features/Settings/UpdateCalendarSettings/UpdateCalendarSettingsHandler.cs`
- Create: `src/Backend.Tests/Features/Settings/UpdateCalendarSettingsHandlerTests.cs`
- Modify: `src/Backend/Endpoints/SettingsEndpoints.cs`

- [ ] **Step 1: Get-Request/Handler**

`src/Backend/Features/Settings/GetCalendarSettings/GetCalendarSettingsRequest.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.GetCalendarSettings;

public sealed record GetCalendarSettingsRequest : IRequest<GetCalendarSettingsResult>;

public sealed record GetCalendarSettingsResult(
    string CalendarId,
    string WorkingHoursStart,   // "HH:mm"
    string WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays,
    bool HasGoogleCredentials,
    bool IsConnected);
```

`src/Backend/Features/Settings/GetCalendarSettings/GetCalendarSettingsHandler.cs`:
```csharp
using Mediator;
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Features.Settings.GetCalendarSettings;

public sealed class GetCalendarSettingsHandler
    : IRequestHandler<GetCalendarSettingsRequest, GetCalendarSettingsResult>
{
    private readonly IAppSettingsRepository _settings;
    private readonly SqliteDataStore _dataStore;

    public GetCalendarSettingsHandler(
        IAppSettingsRepository settings,
        SqliteDataStore dataStore)
    {
        _settings = settings;
        _dataStore = dataStore;
    }

    public async ValueTask<GetCalendarSettingsResult> Handle(
        GetCalendarSettingsRequest request, CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var creds = await _settings.GetGoogleCredentialsAsync(ct);

        // Token-Datensatz vorhanden? Sniff via SqliteDataStore-Typ-Pfad.
        var token = await _dataStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>(
            "nauassist-default");
        var isConnected = token is not null;

        return new GetCalendarSettingsResult(
            CalendarId: cal.CalendarId,
            WorkingHoursStart: cal.WorkingHoursStart.ToString("HH:mm"),
            WorkingHoursEnd: cal.WorkingHoursEnd.ToString("HH:mm"),
            DefaultDurationMinutes: cal.DefaultDurationMinutes,
            SearchHorizonDays: cal.SearchHorizonDays,
            HasGoogleCredentials: creds is not null,
            IsConnected: isConnected);
    }
}
```

- [ ] **Step 2: Update-Request anlegen**

`src/Backend/Features/Settings/UpdateCalendarSettings/UpdateCalendarSettingsRequest.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateCalendarSettings;

public sealed record UpdateCalendarSettingsRequest(
    string CalendarId,
    string WorkingHoursStart,
    string WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays,
    string? GoogleClientId,       // null = unchanged
    string? GoogleClientSecret)   // null = unchanged, "" = cleared (clears ID+Secret+tokens)
    : IRequest<UpdateCalendarSettingsResult>;

public sealed record UpdateCalendarSettingsResult(bool Ok, string? Error);
```

- [ ] **Step 3: Failing-Tests schreiben**

`src/Backend.Tests/Features/Settings/UpdateCalendarSettingsHandlerTests.cs`:
```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateCalendarSettings;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateCalendarSettingsHandlerTests
{
    [Fact]
    public async Task Handle_InvalidWorkingHoursStart_ReturnsError()
    {
        var handler = new UpdateCalendarSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "25:00", "18:00", 60, 14, null, null),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("WorkingHoursStart");
    }

    [Fact]
    public async Task Handle_EndBeforeStart_ReturnsError()
    {
        var handler = new UpdateCalendarSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "18:00", "09:00", 60, 14, null, null),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("Ende");
    }

    [Fact]
    public async Task Handle_NegativeSearchHorizon_ReturnsError()
    {
        var handler = new UpdateCalendarSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "09:00", "18:00", 60, 0, null, null),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("SearchHorizon");
    }

    [Fact]
    public async Task Handle_ValidUpdate_PersistsCalendar()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateCalendarSettingsHandler(repo);

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "work@nau.studio", "07:30", "19:00", 45, 21, null, null),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Calendar.CalendarId.Should().Be("work@nau.studio");
        repo.Calendar.WorkingHoursStart.Should().Be(new TimeOnly(7, 30));
        repo.Calendar.WorkingHoursEnd.Should().Be(new TimeOnly(19, 0));
        repo.Calendar.DefaultDurationMinutes.Should().Be(45);
        repo.Calendar.SearchHorizonDays.Should().Be(21);
    }

    [Fact]
    public async Task Handle_BothCredentialFieldsProvided_PersistsCredentials()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateCalendarSettingsHandler(repo);

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "09:00", "18:00", 60, 14,
            "id.apps.googleusercontent.com", "GOCSPX-abc"),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Credentials.Should().NotBeNull();
        repo.Credentials!.ClientId.Should().Be("id.apps.googleusercontent.com");
        repo.Credentials.ClientSecret.Should().Be("GOCSPX-abc");
    }

    [Fact]
    public async Task Handle_EmptyClientSecret_ClearsCredentials()
    {
        var repo = new InMemoryRepo();
        await repo.SetGoogleCredentialsAsync(
            new GoogleCredentials("old-id", "old-secret"), CancellationToken.None);
        var handler = new UpdateCalendarSettingsHandler(repo);

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "09:00", "18:00", 60, 14, null, ""),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Credentials.Should().BeNull();
    }

    private sealed class InMemoryRepo : IAppSettingsRepository
    {
        public CalendarUserSettings Calendar { get; private set; } =
            new("primary", new(9, 0), new(18, 0), 60, 14);
        public GoogleCredentials? Credentials { get; private set; }

        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            Task.FromResult(Calendar);
        public Task SetCalendarAsync(CalendarUserSettings s, CancellationToken ct)
        {
            Calendar = s; return Task.CompletedTask;
        }
        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            Task.FromResult(Credentials);
        public Task SetGoogleCredentialsAsync(GoogleCredentials c, CancellationToken ct)
        {
            Credentials = string.IsNullOrEmpty(c.ClientId) ? null : c;
            return Task.CompletedTask;
        }

        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) =>
            Task.FromResult(new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null));
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct) => Task.CompletedTask;
        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            Task.FromResult(new OllamaUserSettings("http://localhost:11434", null, 16384, 0.3));
        public Task SetOllamaAsync(OllamaUserSettings s, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Tests laufen — Compile-Fehler erwartet**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateCalendarSettingsHandlerTests"`
Expected: Compile-Error — Handler nicht da.

- [ ] **Step 5: Handler implementieren**

`src/Backend/Features/Settings/UpdateCalendarSettings/UpdateCalendarSettingsHandler.cs`:
```csharp
using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateCalendarSettings;

public sealed class UpdateCalendarSettingsHandler
    : IRequestHandler<UpdateCalendarSettingsRequest, UpdateCalendarSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public UpdateCalendarSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<UpdateCalendarSettingsResult> Handle(
        UpdateCalendarSettingsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CalendarId))
        {
            return new(false, "CalendarId darf nicht leer sein.");
        }

        if (!TimeOnly.TryParseExact(request.WorkingHoursStart, "HH:mm", out var start))
        {
            return new(false, "WorkingHoursStart muss im Format HH:mm sein.");
        }
        if (!TimeOnly.TryParseExact(request.WorkingHoursEnd, "HH:mm", out var end))
        {
            return new(false, "WorkingHoursEnd muss im Format HH:mm sein.");
        }
        if (end <= start)
        {
            return new(false, "Ende der Arbeitszeit muss nach dem Anfang liegen.");
        }

        if (request.DefaultDurationMinutes <= 0 || request.DefaultDurationMinutes > 24 * 60)
        {
            return new(false, "DefaultDurationMinutes muss zwischen 1 und 1440 liegen.");
        }
        if (request.SearchHorizonDays <= 0 || request.SearchHorizonDays > 365)
        {
            return new(false, "SearchHorizonDays muss zwischen 1 und 365 liegen.");
        }

        await _settings.SetCalendarAsync(
            new CalendarUserSettings(
                request.CalendarId,
                start, end,
                request.DefaultDurationMinutes,
                request.SearchHorizonDays),
            ct);

        // Credential-Update: nur wenn explizit was geliefert wurde
        var hasNewId = request.GoogleClientId is not null;
        var hasNewSecret = request.GoogleClientSecret is not null;

        if (hasNewId || hasNewSecret)
        {
            var existing = await _settings.GetGoogleCredentialsAsync(ct);
            var newId = request.GoogleClientId ?? existing?.ClientId ?? "";
            var newSecret = request.GoogleClientSecret switch
            {
                null => existing?.ClientSecret ?? "",
                ""   => "",
                var s => s,
            };

            // Leere Strings → clear (= ClientId leer)
            var clearAll = string.IsNullOrEmpty(newId) || string.IsNullOrEmpty(newSecret);
            await _settings.SetGoogleCredentialsAsync(
                new GoogleCredentials(
                    ClientId: clearAll ? "" : newId,
                    ClientSecret: clearAll ? "" : newSecret),
                ct);
        }

        return new(true, null);
    }
}
```

- [ ] **Step 6: Tests laufen — müssen PASSEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~UpdateCalendarSettingsHandlerTests"`
Expected: 6 PASS.

- [ ] **Step 7: Endpoints registrieren**

In `src/Backend/Endpoints/SettingsEndpoints.cs`:

```csharp
app.MapGet("/api/settings/calendar", async (IMediator mediator, CancellationToken ct) =>
{
    var r = await mediator.Send(new GetCalendarSettingsRequest(), ct);
    return Results.Ok(new CalendarSettingsDto(
        r.CalendarId, r.WorkingHoursStart, r.WorkingHoursEnd,
        r.DefaultDurationMinutes, r.SearchHorizonDays,
        r.HasGoogleCredentials, r.IsConnected));
});

app.MapPut("/api/settings/calendar", async (
    UpdateCalendarSettingsPayload payload,
    IMediator mediator,
    AuditLogRepository audit,
    Func<DateTimeOffset> clock,
    CancellationToken ct) =>
{
    var result = await mediator.Send(new UpdateCalendarSettingsRequest(
        CalendarId: payload.CalendarId ?? "",
        WorkingHoursStart: payload.WorkingHoursStart ?? "",
        WorkingHoursEnd: payload.WorkingHoursEnd ?? "",
        DefaultDurationMinutes: payload.DefaultDurationMinutes,
        SearchHorizonDays: payload.SearchHorizonDays,
        GoogleClientId: payload.GoogleClientId,
        GoogleClientSecret: payload.GoogleClientSecret), ct);

    if (!result.Ok) return Results.BadRequest(new { error = result.Error });

    var args = JsonSerializer.Serialize(new
    {
        calendarId = payload.CalendarId,
        workingHoursStart = payload.WorkingHoursStart,
        workingHoursEnd = payload.WorkingHoursEnd,
        clientIdAction = payload.GoogleClientId switch
        {
            null => "unchanged", "" => "cleared", _ => "set",
        },
        clientSecretAction = payload.GoogleClientSecret switch
        {
            null => "unchanged", "" => "cleared", _ => "set",
        },
    });
    await audit.AppendAsync(
        new AuditEntry(0, null, "settings.calendar.update", args, "{\"ok\":true}", null, clock()),
        ct);

    return Results.Ok(new { ok = true });
});
```

DTOs am Ende der Klasse ergänzen:
```csharp
public sealed record UpdateCalendarSettingsPayload(
    string? CalendarId,
    string? WorkingHoursStart,
    string? WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays,
    string? GoogleClientId,
    string? GoogleClientSecret);

private sealed record CalendarSettingsDto(
    string CalendarId,
    string WorkingHoursStart,
    string WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays,
    bool HasGoogleCredentials,
    bool IsConnected);
```

Imports oben:
```csharp
using NauAssist.Backend.Features.Settings.GetCalendarSettings;
using NauAssist.Backend.Features.Settings.UpdateCalendarSettings;
```

- [ ] **Step 8: Build + Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: 0 Errors, alle Tests PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Calendar-Settings: Get/Update-Handler + Endpoints"
```

---

## Phase 4 — Google-OAuth-Flow aus UI

### Task 14: GoogleAuthService entkoppeln + NotAuthenticatedException + CalendarOptions löschen

**Files:**
- Modify: `src/Backend/Features/Calendar/Google/GoogleAuthService.cs`
- Create: `src/Backend/Features/Calendar/Google/NotAuthenticatedException.cs`
- Delete: `src/Backend/Features/Calendar/CalendarOptions.cs`
- Modify: `src/Backend/Features/Calendar/Google/GoogleAuthCommand.cs` (Aufruf anpassen)
- Modify: `src/Backend/Program.cs` (CalendarOptions-Registrierung entfernen)
- Modify: `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs` (Options-Test entfernen)

- [ ] **Step 1: NotAuthenticatedException anlegen**

`src/Backend/Features/Calendar/Google/NotAuthenticatedException.cs`:
```csharp
namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class NotAuthenticatedException : Exception
{
    public NotAuthenticatedException(string message) : base(message) { }
}
```

- [ ] **Step 2: GoogleAuthService umbauen**

`src/Backend/Features/Calendar/Google/GoogleAuthService.cs` komplett ersetzen durch:
```csharp
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleAuthService
{
    public const string UserId = "nauassist-default";
    public const string RedirectUri = "http://localhost";

    private readonly IAppSettingsRepository _settings;
    private readonly SqliteDataStore _dataStore;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IAppSettingsRepository settings,
        SqliteDataStore dataStore,
        ILogger<GoogleAuthService> logger)
    {
        _settings = settings;
        _dataStore = dataStore;
        _logger = logger;
    }

    /// <summary>
    /// Liefert gültige Credentials aus dem persistierten Token-Store.
    /// Wirft NotAuthenticatedException, wenn keine Tokens gespeichert sind.
    /// </summary>
    public async Task<UserCredential> GetCredentialAsync(CancellationToken ct)
    {
        var clientSecrets = await LoadClientSecretsAsync(ct);
        var flow = BuildFlow(clientSecrets);

        var token = await _dataStore.GetAsync<TokenResponse>(UserId);
        if (token is null)
        {
            throw new NotAuthenticatedException(
                "Google-Kalender ist nicht verbunden. Bitte in den Settings autorisieren.");
        }

        var credential = new UserCredential(flow, UserId, token);
        if (credential.Token.IsStale)
        {
            _logger.LogInformation("Google-Token ist abgelaufen — refreshe.");
            await credential.RefreshTokenAsync(ct);
        }
        return credential;
    }

    /// <summary>Baut Auth-URL für UI-Flow.</summary>
    public async Task<(string AuthUrl, GoogleAuthorizationCodeFlow Flow)> StartAuthorizationAsync(
        CancellationToken ct)
    {
        var clientSecrets = await LoadClientSecretsAsync(ct);
        var flow = BuildFlow(clientSecrets);
        var url = flow.CreateAuthorizationCodeRequest(RedirectUri).Build().AbsoluteUri;
        return (url, flow);
    }

    /// <summary>Tauscht Code gegen Token, persistiert via SqliteDataStore.</summary>
    public async Task ExchangeCodeAsync(
        GoogleAuthorizationCodeFlow flow, string code, CancellationToken ct)
    {
        await flow.ExchangeCodeForTokenAsync(UserId, code, RedirectUri, ct);
    }

    public async Task<bool> IsConnectedAsync()
    {
        var token = await _dataStore.GetAsync<TokenResponse>(UserId);
        return token is not null;
    }

    public Task DisconnectAsync() => _dataStore.ClearAsync();

    private async Task<ClientSecrets> LoadClientSecretsAsync(CancellationToken ct)
    {
        var creds = await _settings.GetGoogleCredentialsAsync(ct);
        if (creds is null)
        {
            throw new NotAuthenticatedException(
                "Google-OAuth-Credentials nicht konfiguriert. Bitte Client-ID und -Secret in den Settings eintragen.");
        }
        return new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret };
    }

    private GoogleAuthorizationCodeFlow BuildFlow(ClientSecrets clientSecrets) =>
        new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = clientSecrets,
            Scopes = new[] { CalendarService.Scope.Calendar },
            DataStore = _dataStore,
        });
}
```

- [ ] **Step 3: GoogleAuthCommand anpassen**

In `src/Backend/Features/Calendar/Google/GoogleAuthCommand.cs` ersetzen durch (CLI-Pfad nutzt jetzt Console-Code-Receiver direkt):
```csharp
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Calendar.Google;

public static class GoogleAuthCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleAuthCommand");
        var auth = services.GetRequiredService<GoogleAuthService>();

        try
        {
            logger.LogInformation("Starte OAuth-Flow gegen Google (Console).");
            var (url, flow) = await auth.StartAuthorizationAsync(ct);

            var receiver = new ConsoleCodeReceiver();
            var reqUrl = new AuthorizationCodeRequestUrl(new Uri(url))
            {
                RedirectUri = GoogleAuthService.RedirectUri,
            };
            var codeResponse = await receiver.ReceiveCodeAsync(reqUrl, ct);

            await auth.ExchangeCodeAsync(flow, codeResponse.Code, ct);
            logger.LogInformation("OAuth-Flow erfolgreich. Tokens persistiert.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth-Flow fehlgeschlagen.");
            return 1;
        }
    }
}
```

- [ ] **Step 4: CalendarOptions löschen + Konsumenten finden**

```bash
rm src/Backend/Features/Calendar/CalendarOptions.cs
```

Run: `grep -rn "CalendarOptions" src/`

Übrige Treffer abarbeiten:
- `src/Backend/Program.cs`: Zeile `builder.Services.Configure<CalendarOptions>(builder.Configuration.GetSection("Calendar"));` löschen. Auch den `using NauAssist.Backend.Features.Calendar;` falls dadurch unbenutzt.
- `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs`: Test `CalendarOptions_HasReasonableDefaults` komplett löschen.

- [ ] **Step 5: Build + Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: 0 Errors, alle Tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "GoogleAuthService: ClientSecrets aus DB, NotAuthenticatedException, CalendarOptions raus"
```

---

### Task 15: AuthSessionStore + CalendarAuthEndpoints (TDD)

**Files:**
- Create: `src/Backend/Features/Calendar/Google/AuthSessionStore.cs`
- Create: `src/Backend/Endpoints/CalendarAuthEndpoints.cs`
- Create: `src/Backend.Tests/Features/Calendar/Google/AuthSessionStoreTests.cs`
- Modify: `src/Backend/Program.cs`

- [ ] **Step 1: Failing-Tests schreiben**

`src/Backend.Tests/Features/Calendar/Google/AuthSessionStoreTests.cs`:
```csharp
using FluentAssertions;
using Google.Apis.Auth.OAuth2.Flows;
using Microsoft.Extensions.Caching.Memory;
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Tests.Features.Calendar.Google;

public sealed class AuthSessionStoreTests
{
    private static GoogleAuthorizationCodeFlow MakeFlow() =>
        new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new Google.Apis.Auth.OAuth2.ClientSecrets
            {
                ClientId = "x", ClientSecret = "y",
            },
            Scopes = new[] { "scope" },
        });

    [Fact]
    public void PutThenTake_ReturnsFlow_AndRemovesSession()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new AuthSessionStore(cache);
        var flow = MakeFlow();

        var id = store.Put(flow);
        var taken = store.Take(id);
        var second = store.Take(id);

        taken.Should().BeSameAs(flow);
        second.Should().BeNull();
    }

    [Fact]
    public void Take_UnknownId_ReturnsNull()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new AuthSessionStore(cache);

        store.Take("nope").Should().BeNull();
    }
}
```

- [ ] **Step 2: Tests laufen — Compile-Fehler erwartet**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AuthSessionStoreTests"`
Expected: Compile-Error — `AuthSessionStore` existiert nicht.

- [ ] **Step 3: AuthSessionStore implementieren**

`src/Backend/Features/Calendar/Google/AuthSessionStore.cs`:
```csharp
using Google.Apis.Auth.OAuth2.Flows;
using Microsoft.Extensions.Caching.Memory;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class AuthSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly IMemoryCache _cache;

    public AuthSessionStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string Put(GoogleAuthorizationCodeFlow flow)
    {
        var id = Guid.NewGuid().ToString("N");
        _cache.Set(Key(id), flow, Ttl);
        return id;
    }

    public GoogleAuthorizationCodeFlow? Take(string id)
    {
        if (_cache.TryGetValue<GoogleAuthorizationCodeFlow>(Key(id), out var flow))
        {
            _cache.Remove(Key(id));
            return flow;
        }
        return null;
    }

    private static string Key(string id) => $"oauth-session:{id}";
}
```

- [ ] **Step 4: Tests laufen — müssen PASSEN**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AuthSessionStoreTests"`
Expected: 2 PASS.

- [ ] **Step 5: CalendarAuthEndpoints anlegen**

`src/Backend/Endpoints/CalendarAuthEndpoints.cs`:
```csharp
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Endpoints;

public static class CalendarAuthEndpoints
{
    public static IEndpointRouteBuilder MapCalendarAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/calendar/auth/start", async (
            GoogleAuthService auth,
            AuthSessionStore sessions,
            CancellationToken ct) =>
        {
            try
            {
                var (url, flow) = await auth.StartAuthorizationAsync(ct);
                var id = sessions.Put(flow);
                return Results.Ok(new { authUrl = url, sessionId = id });
            }
            catch (NotAuthenticatedException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/calendar/auth/complete", async (
            CompleteAuthPayload payload,
            AuthSessionStore sessions,
            GoogleAuthService auth,
            CancellationToken ct) =>
        {
            var flow = sessions.Take(payload.SessionId);
            if (flow is null)
            {
                return Results.StatusCode(410); // Gone
            }
            try
            {
                await auth.ExchangeCodeAsync(flow, payload.Code, ct);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Code ungültig: {ex.Message}" });
            }
        });

        app.MapPost("/api/calendar/auth/disconnect", async (GoogleAuthService auth) =>
        {
            await auth.DisconnectAsync();
            return Results.Ok(new { ok = true });
        });

        return app;
    }

    public sealed record CompleteAuthPayload(string SessionId, string Code);
}
```

- [ ] **Step 6: Program.cs erweitern**

In `src/Backend/Program.cs`:

1. `using Microsoft.Extensions.Caching.Memory;` oben (wenn nicht vorhanden — `Microsoft.AspNetCore.App` enthält das transitive).
2. Nach `builder.Services.AddSingleton<SqliteDataStore>();` ergänzen:
   ```csharp
   builder.Services.AddMemoryCache();
   builder.Services.AddSingleton<AuthSessionStore>();
   ```
3. Bei den Endpoint-Mappings vor `app.MapFallbackToFile("index.html");` ergänzen:
   ```csharp
   app.MapCalendarAuthEndpoints();
   ```
4. `GoogleAuthService`-Registrierung anpassen: `builder.Services.AddScoped<GoogleAuthService>();` (statt Singleton — er hat jetzt eine Scoped-Abhängigkeit auf `IAppSettingsRepository`).

- [ ] **Step 7: Build + Tests laufen**

Run: `dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: 0 Errors, alle Tests PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "OAuth-Flow: AuthSessionStore + Calendar-Auth-Endpoints (start/complete/disconnect)"
```

---

## Phase 5 — Konfig-Cleanup

### Task 16: appsettings.json + Dockerfile aufräumen

**Files:**
- Modify: `src/Backend/appsettings.json`
- Modify: `src/Backend/appsettings.Development.json`
- Modify: `Dockerfile`

- [ ] **Step 1: appsettings.json bereinigen**

In `src/Backend/appsettings.json`:

Entfernen:
- die komplette `"Calendar": { … }`-Sektion
- aus `"Ollama"`: Keys `Host`, `ApiKey`, `NumCtx`, `Temperature`

Behalten in `Ollama`: `Model`, `InitialTimeoutSeconds`, `TokenTimeoutSeconds`, `SystemPrompt`.

Beispiel-Endzustand der `Ollama`-Sektion:
```json
"Ollama": {
  "Model": "gemma4:26b",
  "InitialTimeoutSeconds": 60,
  "TokenTimeoutSeconds": 30,
  "SystemPrompt": "Du bist NauAssist…"
}
```

- [ ] **Step 2: appsettings.Development.json analog**

Falls dort `Calendar`- oder `Ollama:Host/ApiKey/NumCtx/Temperature` gesetzt sind, ebenfalls entfernen. (`cat src/Backend/appsettings.Development.json` zur Prüfung.)

- [ ] **Step 3: Dockerfile bereinigen**

In `Dockerfile` die Zeile
```
    Calendar__GoogleCredentialsPath=/app/data/google-credentials.json \
```
aus dem `ENV`-Block entfernen.

- [ ] **Step 4: Build + Tests + Smoke-Test mit frischer DB**

```bash
rm -f src/Backend/data/nauassist.db src/Backend/data/google-credentials.json
dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj
dotnet run --project src/Backend &
sleep 5
curl -sS http://localhost:5000/api/settings/ollama
curl -sS http://localhost:5000/api/settings/calendar
kill %1
```
Expected: Beide Endpoints liefern JSON mit Default-Werten. `nauassist.db` wird neu angelegt, `google-credentials.json` wird **nicht** mehr erzeugt.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "appsettings.json + Dockerfile: Calendar/Ollama-User-Settings rausnehmen"
```

---

## Phase 6 — Frontend

### Task 17: API-Module erweitern

**Files:**
- Modify: `frontend/src/api/settings.ts`
- Create: `frontend/src/api/calendar-settings.ts`

- [ ] **Step 1: settings.ts um Ollama-Endpoints erweitern**

In `frontend/src/api/settings.ts` am Ende anhängen:
```typescript
export interface OllamaSettings {
  host: string;
  hasApiKey: boolean;
  numCtx: number;
  temperature: number;
}

export interface UpdateOllamaSettingsPayload {
  host: string;
  apiKey: string | null;       // null = unchanged, "" = clear, else = set
  numCtx: number;
  temperature: number;
}

export async function getOllamaSettings(): Promise<OllamaSettings> {
  const res = await fetch("/api/settings/ollama");
  if (!res.ok) throw new Error(`GET /api/settings/ollama failed: ${res.status}`);
  return res.json();
}

export async function updateOllamaSettings(
  payload: UpdateOllamaSettingsPayload,
): Promise<void> {
  const res = await fetch("/api/settings/ollama", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `PUT /api/settings/ollama failed: ${res.status}`);
  }
}

export interface OllamaTestResult {
  ok: boolean;
  models?: string[] | null;
  error?: string | null;
}

export async function testOllamaConnection(
  host: string,
  apiKey: string | null,
): Promise<OllamaTestResult> {
  const res = await fetch("/api/settings/ollama/test", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ host, apiKey }),
  });
  return res.json();
}
```

- [ ] **Step 2: calendar-settings.ts anlegen**

`frontend/src/api/calendar-settings.ts`:
```typescript
export interface CalendarSettings {
  calendarId: string;
  workingHoursStart: string;   // "HH:mm"
  workingHoursEnd: string;
  defaultDurationMinutes: number;
  searchHorizonDays: number;
  hasGoogleCredentials: boolean;
  isConnected: boolean;
}

export interface UpdateCalendarSettingsPayload {
  calendarId: string;
  workingHoursStart: string;
  workingHoursEnd: string;
  defaultDurationMinutes: number;
  searchHorizonDays: number;
  googleClientId: string | null;
  googleClientSecret: string | null;
}

export async function getCalendarSettings(): Promise<CalendarSettings> {
  const res = await fetch("/api/settings/calendar");
  if (!res.ok) throw new Error(`GET /api/settings/calendar failed: ${res.status}`);
  return res.json();
}

export async function updateCalendarSettings(
  payload: UpdateCalendarSettingsPayload,
): Promise<void> {
  const res = await fetch("/api/settings/calendar", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `PUT /api/settings/calendar failed: ${res.status}`);
  }
}

export interface StartAuthResponse {
  authUrl: string;
  sessionId: string;
}

export async function startGoogleAuth(): Promise<StartAuthResponse> {
  const res = await fetch("/api/calendar/auth/start", { method: "POST" });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Auth nicht startbar" }));
    throw new Error(body.error ?? `POST /api/calendar/auth/start failed: ${res.status}`);
  }
  return res.json();
}

export async function completeGoogleAuth(
  sessionId: string,
  code: string,
): Promise<void> {
  const res = await fetch("/api/calendar/auth/complete", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ sessionId, code }),
  });
  if (!res.ok) {
    if (res.status === 410) {
      throw new Error("Sitzung abgelaufen, bitte neu starten.");
    }
    const body = await res.json().catch(() => ({ error: "Auth fehlgeschlagen" }));
    throw new Error(body.error ?? `POST /api/calendar/auth/complete failed: ${res.status}`);
  }
}

export async function disconnectGoogle(): Promise<void> {
  const res = await fetch("/api/calendar/auth/disconnect", { method: "POST" });
  if (!res.ok) throw new Error(`POST /api/calendar/auth/disconnect failed: ${res.status}`);
}
```

- [ ] **Step 3: Typecheck**

Run: `cd frontend && npm run typecheck`
Expected: 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/settings.ts frontend/src/api/calendar-settings.ts
git commit -m "Frontend API: Ollama- + Calendar-Settings + OAuth-Flow"
```

---

### Task 18: SettingsPage komplett neu schreiben

**Files:**
- Modify: `frontend/src/components/pages/SettingsPage.tsx` (vollständiger Rewrite)

Diese Task ersetzt die gesamte Datei. Wir kopieren das LLM-bezogene Verhalten (auto-save für Provider/Modell-Wechsel, Tri-State für ApiKey) und fügen die neuen Sektionen hinzu. Mockup-Komponenten (`Toggle`, `SegRadio`, `Stepper`, `ColorSwatchRow`, `CalRow`, `TxtField`) sowie alle Mockup-Sektionen werden gelöscht.

- [ ] **Step 1: SettingsPage.tsx komplett ersetzen**

`frontend/src/components/pages/SettingsPage.tsx`:
```tsx
import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { AppPage } from "@/App";
import {
  getLlmSettings,
  updateLlmSettings,
  getOllamaSettings,
  updateOllamaSettings,
  testOllamaConnection,
  OLLAMA_MODELS,
  GEMINI_MODELS,
  type LlmSettings,
  type OllamaSettings,
} from "@/api/settings";
import {
  getCalendarSettings,
  updateCalendarSettings,
  startGoogleAuth,
  completeGoogleAuth,
  disconnectGoogle,
  type CalendarSettings,
} from "@/api/calendar-settings";

interface SettingsPageProps {
  onNavigate: (page: AppPage) => void;
}

interface RowProps {
  label: string;
  hint?: string;
  children: ReactNode;
}

function Row({ label, hint, children }: RowProps) {
  return (
    <div
      className="grid items-start gap-8 border-b border-nau-line py-4"
      style={{ gridTemplateColumns: "260px 1fr" }}
    >
      <div>
        <div
          className="font-sans text-sm font-medium text-nau-fg"
          style={{ marginBottom: hint ? 6 : 0 }}
        >
          {label}
        </div>
        {hint && (
          <div className="max-w-[240px] font-sans text-[13px] leading-relaxed text-nau-fg-dim">
            {hint}
          </div>
        )}
      </div>
      <div className="pt-0.5">{children}</div>
    </div>
  );
}

interface SectionHeadProps {
  n: number;
  label: string;
  title: ReactNode;
  kicker?: string;
}

function SectionHead({ n, label, title, kicker }: SectionHeadProps) {
  return (
    <div className="mb-6 pt-2">
      <div className="mb-4 flex items-center gap-3.5">
        <span className="font-mono text-[13px] font-bold text-nau-accent">
          {String(n).padStart(2, "0")}
        </span>
        <span className="h-px w-8 bg-nau-line" />
        <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
          {label}
        </span>
      </div>
      <h2 className="m-0 mb-2 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg">
        {title}
      </h2>
      {kicker && (
        <p className="m-0 max-w-[600px] font-sans text-sm leading-relaxed text-nau-fg-dim">
          {kicker}
        </p>
      )}
    </div>
  );
}

function TextInput({
  value, onChange, placeholder, type = "text", disabled = false,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  type?: "text" | "password" | "number";
  disabled?: boolean;
}) {
  return (
    <input
      type={type}
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      onChange={(e) => onChange(e.target.value)}
      className="max-w-[480px] w-full border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg disabled:opacity-50"
    />
  );
}

function PrimaryButton({ children, onClick, disabled }: {
  children: ReactNode; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="cursor-pointer border-none bg-nau-accent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-bg disabled:opacity-40"
    >
      {children}
    </button>
  );
}

function SecondaryButton({ children, onClick, disabled }: {
  children: ReactNode; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="cursor-pointer border border-nau-line bg-transparent px-4 py-2.5 font-mono text-[11px] tracking-mono-wide text-nau-fg disabled:opacity-40"
    >
      {children}
    </button>
  );
}

// ─── Page ─────────────────────────────────────────────────────

export function SettingsPage({ onNavigate }: SettingsPageProps) {
  const [llm, setLlm] = useState<LlmSettings | null>(null);
  const [ollama, setOllama] = useState<OllamaSettings | null>(null);
  const [calendar, setCalendar] = useState<CalendarSettings | null>(null);
  const [topError, setTopError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([getLlmSettings(), getOllamaSettings(), getCalendarSettings()])
      .then(([l, o, c]) => {
        setLlm(l); setOllama(o); setCalendar(c);
      })
      .catch((e) => setTopError(String(e.message ?? e)));
  }, []);

  const navItems = [
    { n: "01", label: "KI-Provider", anchor: "section-llm" },
    { n: "02", label: "Kalender", anchor: "section-calendar" },
  ];

  return (
    <div
      className="grid min-h-screen bg-nau-bg text-nau-fg"
      style={{ gridTemplateColumns: "260px 1fr" }}
    >
      <aside className="relative border-r border-nau-line px-6 py-7">
        <button
          type="button"
          onClick={() => onNavigate("chat")}
          className="mb-10 flex cursor-pointer items-center gap-3 bg-transparent"
          aria-label="Zurück zum Chat"
        >
          <span className="inline-flex h-7 w-7 items-center justify-center bg-nau-accent font-mono text-[13px] font-bold text-nau-bg">
            N
          </span>
          <span className="font-sans text-[15px] font-semibold text-nau-fg">NauAssist</span>
        </button>

        <div className="mb-4 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
          // EINSTELLUNGEN
        </div>

        <nav className="flex flex-col gap-0.5">
          {navItems.map((it) => (
            <a
              key={it.n}
              href={`#${it.anchor}`}
              className="flex cursor-pointer items-center gap-3 px-3 py-2.5 no-underline"
            >
              <span className="font-mono text-[11px] font-bold tracking-mono text-nau-fg-dim">
                {it.n}
              </span>
              <span className="font-sans text-sm text-nau-fg-dim">{it.label}</span>
            </a>
          ))}
        </nav>
      </aside>

      <main className="max-w-[980px] px-16 pb-20 pt-10">
        <div className="mb-9">
          <div className="mb-3 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            — EINSTELLUNGEN —
          </div>
          <h1 className="m-0 font-sans text-4xl font-normal leading-[1.05] tracking-tight text-nau-fg">
            Provider &amp; Kalender.
          </h1>
        </div>

        {topError && (
          <div className="mb-6 border border-nau-danger bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
            // SETTINGS NICHT LADBAR — {topError}
          </div>
        )}

        {llm && ollama && (
          <LlmSection
            llm={llm} setLlm={setLlm}
            ollama={ollama} setOllama={setOllama}
          />
        )}
        {calendar && (
          <CalendarSection calendar={calendar} setCalendar={setCalendar} />
        )}

        <div className="mt-14 flex items-center justify-end border-t border-nau-line pt-6">
          <SecondaryButton onClick={() => onNavigate("chat")}>
            ZURÜCK ZUM CHAT
          </SecondaryButton>
        </div>
      </main>
    </div>
  );
}

// ─── Section: LLM ─────────────────────────────────────────────

function LlmSection({
  llm, setLlm, ollama, setOllama,
}: {
  llm: LlmSettings;
  setLlm: (l: LlmSettings) => void;
  ollama: OllamaSettings;
  setOllama: (o: OllamaSettings) => void;
}) {
  const [draftKey, setDraftKey] = useState("");
  const [editingKey, setEditingKey] = useState(false);
  const [llmError, setLlmError] = useState<string | null>(null);
  const [savedFlash, setSavedFlash] = useState(false);

  const [showAdvanced, setShowAdvanced] = useState(false);
  const [hostDraft, setHostDraft] = useState(ollama.host);
  const [apiKeyDraft, setApiKeyDraft] = useState("");
  const [numCtxDraft, setNumCtxDraft] = useState(String(ollama.numCtx));
  const [tempDraft, setTempDraft] = useState(String(ollama.temperature));
  const [editingOllamaKey, setEditingOllamaKey] = useState(false);
  const [ollamaSaving, setOllamaSaving] = useState(false);
  const [ollamaError, setOllamaError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);

  const ollamaDirty =
    hostDraft !== ollama.host ||
    numCtxDraft !== String(ollama.numCtx) ||
    tempDraft !== String(ollama.temperature) ||
    editingOllamaKey;

  const saveLlm = async (
    patch: Partial<LlmSettings> & { geminiApiKey?: string | null },
  ) => {
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
      setEditingKey(false);
      setDraftKey("");
    }
  };

  const saveOllama = async () => {
    setOllamaSaving(true);
    setOllamaError(null);
    try {
      const apiKey = editingOllamaKey ? apiKeyDraft : null;
      await updateOllamaSettings({
        host: hostDraft,
        apiKey,
        numCtx: parseInt(numCtxDraft, 10),
        temperature: parseFloat(tempDraft),
      });
      const fresh = await getOllamaSettings();
      setOllama(fresh);
      setHostDraft(fresh.host);
      setNumCtxDraft(String(fresh.numCtx));
      setTempDraft(String(fresh.temperature));
      setApiKeyDraft("");
      setEditingOllamaKey(false);
    } catch (e) {
      setOllamaError(String((e as Error).message ?? e));
    } finally {
      setOllamaSaving(false);
    }
  };

  const runTest = async () => {
    setTestResult("// TESTE …");
    const r = await testOllamaConnection(
      hostDraft,
      editingOllamaKey ? apiKeyDraft : null,
    );
    if (r.ok) {
      setTestResult(`// ERREICHBAR · ${r.models?.length ?? 0} MODELLE`);
    } else {
      setTestResult(`// FEHLER: ${r.error ?? "unbekannt"}`);
    }
  };

  return (
    <div id="section-llm">
      <SectionHead n={1} label="KI-PROVIDER" title="Wie Nau denkt." />

      <Row label="AI-Provider" hint="Welche AI Nau für seine Antworten nutzt.">
        <div className="inline-flex border border-nau-line">
          {(["ollama", "gemini"] as const).map((p, i) => {
            const active = llm.provider === p;
            return (
              <button
                key={p}
                type="button"
                onClick={() => saveLlm({ provider: p })}
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

      <Row
        label="Gemini API-Key"
        hint="Wird sicher lokal gespeichert. Hol dir einen Key bei aistudio.google.com."
      >
        {llm.hasGeminiApiKey && !editingKey ? (
          <div className="flex items-center gap-3">
            <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
              •••••••••• GESPEICHERT
            </span>
            <SecondaryButton onClick={() => setEditingKey(true)}>ÄNDERN</SecondaryButton>
            <SecondaryButton
              onClick={() => saveLlm({ geminiApiKey: "" })}
              disabled={llm.provider === "gemini"}
            >
              ENTFERNEN
            </SecondaryButton>
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
            <PrimaryButton
              onClick={() => saveLlm({ geminiApiKey: draftKey })}
              disabled={draftKey.length === 0}
            >
              ÜBERNEHMEN ↵
            </PrimaryButton>
            {llm.hasGeminiApiKey && (
              <SecondaryButton onClick={() => {
                setEditingKey(false); setDraftKey("");
              }}>
                ABBRECHEN
              </SecondaryButton>
            )}
          </div>
        )}
      </Row>

      <Row label="Ollama erweitert" hint="Host, API-Key, Kontext-Größe, Temperatur.">
        <button
          type="button"
          onClick={() => setShowAdvanced(!showAdvanced)}
          className="cursor-pointer bg-transparent font-mono text-[11px] tracking-mono-wide text-nau-fg-dim"
        >
          {showAdvanced ? "▼ EINKLAPPEN" : "▶ AUSKLAPPEN"}
        </button>
      </Row>

      {showAdvanced && (
        <>
          <Row label="Ollama-Host" hint="Z.B. http://localhost:11434 oder hinter einem Reverse-Proxy.">
            <div className="flex flex-col gap-2 max-w-[480px]">
              <div className="flex items-center gap-3">
                <TextInput
                  value={hostDraft}
                  onChange={setHostDraft}
                  placeholder="http://localhost:11434"
                />
                <SecondaryButton onClick={runTest}>TESTEN</SecondaryButton>
              </div>
              {testResult && (
                <div className="font-mono text-[10px] tracking-mono-wide text-nau-fg-dim">
                  {testResult}
                </div>
              )}
            </div>
          </Row>

          <Row label="Ollama API-Key" hint="Optional. Bearer-Token für Reverse-Proxy-Endpoints.">
            {ollama.hasApiKey && !editingOllamaKey ? (
              <div className="flex items-center gap-3">
                <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
                  •••••• GESPEICHERT
                </span>
                <SecondaryButton onClick={() => setEditingOllamaKey(true)}>ÄNDERN</SecondaryButton>
              </div>
            ) : (
              <div className="flex items-center gap-3">
                <input
                  type="password"
                  value={apiKeyDraft}
                  onChange={(e) => {
                    setApiKeyDraft(e.target.value);
                    setEditingOllamaKey(true);
                  }}
                  placeholder="optional"
                  className="max-w-[360px] flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
                />
                {ollama.hasApiKey && (
                  <SecondaryButton onClick={() => {
                    setEditingOllamaKey(false); setApiKeyDraft("");
                  }}>
                    ABBRECHEN
                  </SecondaryButton>
                )}
              </div>
            )}
          </Row>

          <Row label="NumCtx" hint="Kontextfenster in Tokens (8192 / 16384 / …).">
            <TextInput type="number" value={numCtxDraft} onChange={setNumCtxDraft} />
          </Row>

          <Row label="Temperature" hint="0.0 = deterministisch, 1.0 = kreativ.">
            <TextInput type="number" value={tempDraft} onChange={setTempDraft} />
          </Row>

          <div className="flex items-center gap-3 border-b border-nau-line py-4">
            <PrimaryButton onClick={saveOllama} disabled={!ollamaDirty || ollamaSaving}>
              OLLAMA SPEICHERN ↵
            </PrimaryButton>
            {ollamaError && (
              <span className="font-mono text-[10px] tracking-mono-wide text-nau-danger">
                // FEHLER: {ollamaError}
              </span>
            )}
          </div>
        </>
      )}

      {savedFlash && (
        <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-accent">
          // PROVIDER AKTUALISIERT — WIRD AB DEINER NÄCHSTEN NACHRICHT GENUTZT
        </div>
      )}
      {llmError && (
        <div className="border-b border-nau-line py-3 font-mono text-[10px] tracking-mono-wide text-nau-danger">
          // FEHLER: {llmError}
        </div>
      )}
    </div>
  );
}

// ─── Section: Calendar ────────────────────────────────────────

function CalendarSection({
  calendar, setCalendar,
}: {
  calendar: CalendarSettings;
  setCalendar: (c: CalendarSettings) => void;
}) {
  const [calendarId, setCalendarId] = useState(calendar.calendarId);
  const [whStart, setWhStart] = useState(calendar.workingHoursStart);
  const [whEnd, setWhEnd] = useState(calendar.workingHoursEnd);
  const [defaultDur, setDefaultDur] = useState(String(calendar.defaultDurationMinutes));
  const [horizon, setHorizon] = useState(String(calendar.searchHorizonDays));

  const [clientIdDraft, setClientIdDraft] = useState("");
  const [clientSecretDraft, setClientSecretDraft] = useState("");
  const [editingCreds, setEditingCreds] = useState(false);

  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [authState, setAuthState] = useState<{ url: string; sessionId: string } | null>(null);
  const [authCode, setAuthCode] = useState("");
  const [authError, setAuthError] = useState<string | null>(null);

  const dirty =
    calendarId !== calendar.calendarId ||
    whStart !== calendar.workingHoursStart ||
    whEnd !== calendar.workingHoursEnd ||
    defaultDur !== String(calendar.defaultDurationMinutes) ||
    horizon !== String(calendar.searchHorizonDays) ||
    editingCreds;

  const save = async () => {
    setSaving(true);
    setSaveError(null);
    try {
      await updateCalendarSettings({
        calendarId,
        workingHoursStart: whStart,
        workingHoursEnd: whEnd,
        defaultDurationMinutes: parseInt(defaultDur, 10),
        searchHorizonDays: parseInt(horizon, 10),
        googleClientId: editingCreds ? clientIdDraft : null,
        googleClientSecret: editingCreds ? clientSecretDraft : null,
      });
      const fresh = await getCalendarSettings();
      setCalendar(fresh);
      setEditingCreds(false);
      setClientIdDraft("");
      setClientSecretDraft("");
    } catch (e) {
      setSaveError(String((e as Error).message ?? e));
    } finally {
      setSaving(false);
    }
  };

  const startAuth = async () => {
    setAuthError(null);
    try {
      const r = await startGoogleAuth();
      setAuthState(r);
    } catch (e) {
      setAuthError(String((e as Error).message ?? e));
    }
  };

  const completeAuth = async () => {
    if (!authState) return;
    setAuthError(null);
    try {
      await completeGoogleAuth(authState.sessionId, authCode.trim());
      const fresh = await getCalendarSettings();
      setCalendar(fresh);
      setAuthState(null);
      setAuthCode("");
    } catch (e) {
      setAuthError(String((e as Error).message ?? e));
    }
  };

  const disconnect = async () => {
    await disconnectGoogle();
    const fresh = await getCalendarSettings();
    setCalendar(fresh);
  };

  return (
    <div id="section-calendar" className="mt-14">
      <SectionHead
        n={2}
        label="KALENDER"
        title="Google-Kalender."
        kicker="OAuth-Credentials + Verhalten von Nau gegenüber deinem Kalender."
      />

      <Row label="Verbindungsstatus" hint="">
        <div className="flex items-center gap-3">
          <span
            className="px-2.5 py-1 font-mono text-[10px] tracking-mono-wide"
            style={{
              color: calendar.isConnected ? "#facc15" : "#888885",
              border: calendar.isConnected
                ? "1px solid #facc15"
                : "1px solid rgba(255,255,255,0.10)",
              background: calendar.isConnected ? "rgba(250,204,21,0.08)" : "transparent",
            }}
          >
            {calendar.isConnected ? "● VERBUNDEN" : "○ NICHT VERBUNDEN"}
          </span>
          {calendar.isConnected ? (
            <SecondaryButton onClick={disconnect}>TRENNEN</SecondaryButton>
          ) : (
            <PrimaryButton
              onClick={startAuth}
              disabled={!calendar.hasGoogleCredentials}
            >
              MIT GOOGLE VERBINDEN →
            </PrimaryButton>
          )}
        </div>
      </Row>

      {authState && (
        <div className="border border-nau-line bg-white/[0.015] px-4 py-4 my-4">
          <div className="mb-3 font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            // GOOGLE-AUTORISIERUNG
          </div>
          <ol className="m-0 mb-3 list-decimal pl-5 font-sans text-[13px] leading-relaxed text-nau-fg-dim">
            <li>Öffne die URL in einem Browser und klicke „Erlauben".</li>
            <li>Nach „Erlauben" landet der Browser auf einer nicht-erreichbaren Seite (Absicht).</li>
            <li>Kopiere aus der Adresszeile den Wert hinter <code>code=</code> bis zum nächsten <code>&amp;</code>.</li>
          </ol>
          <div className="mb-3 max-w-[600px] break-all border border-nau-line bg-nau-bg px-3 py-2 font-mono text-[11px] text-nau-fg">
            {authState.url}
          </div>
          <div className="flex items-center gap-3">
            <input
              type="text"
              value={authCode}
              onChange={(e) => setAuthCode(e.target.value)}
              placeholder="code=..."
              className="max-w-[360px] flex-1 border border-nau-line bg-white/[0.03] px-3.5 py-3 font-sans text-sm text-nau-fg"
            />
            <PrimaryButton onClick={completeAuth} disabled={authCode.trim().length === 0}>
              CODE ÜBERMITTELN ↵
            </PrimaryButton>
            <SecondaryButton onClick={() => { setAuthState(null); setAuthCode(""); }}>
              ABBRECHEN
            </SecondaryButton>
          </div>
          {authError && (
            <div className="mt-3 font-mono text-[10px] tracking-mono-wide text-nau-danger">
              // FEHLER: {authError}
            </div>
          )}
        </div>
      )}

      {!calendar.hasGoogleCredentials && !authState && (
        <div className="my-4 border border-nau-line bg-white/[0.015] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-fg-dim">
          // GOOGLE-CLIENT-ID / -SECRET FEHLEN — BITTE UNTEN EINTRAGEN UND SPEICHERN
        </div>
      )}

      <Row label="Google Client-ID" hint="Aus Google Cloud Console → OAuth-Client (Desktop App).">
        {calendar.hasGoogleCredentials && !editingCreds ? (
          <div className="flex items-center gap-3">
            <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
              GESPEICHERT
            </span>
            <SecondaryButton onClick={() => setEditingCreds(true)}>ÄNDERN</SecondaryButton>
          </div>
        ) : (
          <TextInput
            value={clientIdDraft}
            onChange={(v) => { setClientIdDraft(v); setEditingCreds(true); }}
            placeholder="123-abc.apps.googleusercontent.com"
          />
        )}
      </Row>

      <Row label="Google Client-Secret" hint="Aus derselben OAuth-Client-Definition.">
        {calendar.hasGoogleCredentials && !editingCreds ? (
          <span className="font-mono text-[12px] tracking-mono text-nau-fg-dim">
            •••••• GESPEICHERT
          </span>
        ) : (
          <TextInput
            type="password"
            value={clientSecretDraft}
            onChange={(v) => { setClientSecretDraft(v); setEditingCreds(true); }}
            placeholder="GOCSPX-..."
          />
        )}
      </Row>

      <Row label="Calendar-ID" hint="„primary" oder eine konkrete Kalender-Adresse.">
        <TextInput value={calendarId} onChange={setCalendarId} />
      </Row>

      <Row label="Arbeitszeiten" hint="Außerhalb dieser Zeiten schlägt Nau nichts vor.">
        <div className="flex items-center gap-3">
          <TextInput value={whStart} onChange={setWhStart} placeholder="09:00" />
          <span className="font-mono text-xs text-nau-fg-dim">→</span>
          <TextInput value={whEnd} onChange={setWhEnd} placeholder="18:00" />
        </div>
      </Row>

      <Row label="Standard-Dauer" hint="Wird genutzt, wenn du keine Dauer angibst (Minuten).">
        <TextInput type="number" value={defaultDur} onChange={setDefaultDur} />
      </Row>

      <Row label="Such-Horizont" hint="Wie viele Tage in die Zukunft Nau plant.">
        <TextInput type="number" value={horizon} onChange={setHorizon} />
      </Row>

      <div className="flex items-center gap-3 py-4">
        <PrimaryButton onClick={save} disabled={!dirty || saving}>
          KALENDER SPEICHERN ↵
        </PrimaryButton>
        {saveError && (
          <span className="font-mono text-[10px] tracking-mono-wide text-nau-danger">
            // FEHLER: {saveError}
          </span>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Typecheck + Lint**

Run:
```bash
cd frontend && npm run typecheck && npm run lint
```
Expected: 0 Errors. Lint-Warnings sollten dem bisherigen Stand entsprechen.

- [ ] **Step 3: Dev-Server starten**

Run: `cd frontend && npm run dev`

Im Browser zu `/settings` navigieren (siehe `App.tsx` für Routing). Visuell prüfen:
- Linke Nav zeigt nur "01 KI-Provider" und "02 Kalender".
- Section 01 zeigt Provider-Toggle, Modell-Dropdown, Gemini-Key-Input, "Ollama erweitert" ausklappbar mit Host/ApiKey/NumCtx/Temperature + Test-Button + Save-Button.
- Section 02 zeigt Status-Chip, Client-ID/Secret-Inputs, Calendar-ID, Arbeitszeiten, Standard-Dauer, Suchhorizont + Save-Button.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/SettingsPage.tsx
git commit -m "SettingsPage: Mockup raus, Ollama-/Calendar-Settings + OAuth-UI"
```

---

## Phase 7 — End-to-End-Verifikation

### Task 19: Manuelle E2E-Verifikation

**Files:**
- (keine Code-Änderungen)

Ziel: Sicherstellen, dass das System mit frischer DB funktioniert, Ollama-Settings live wirken und der Google-OAuth-Flow vollständig durchläuft.

- [ ] **Step 1: Frische DB + Backend hochfahren**

```bash
rm -f src/Backend/data/nauassist.db
dotnet build && dotnet test src/Backend.Tests/Backend.Tests.csproj
dotnet run --project src/Backend &
cd frontend && npm run dev &
```
Browser auf die Vite-URL öffnen (i.d.R. `http://localhost:5173`). Vite proxied `/api`-Requests ans Backend (siehe `frontend/vite.config.ts`).

- [ ] **Step 2: Ollama-Sektion durchklicken**

In den Settings:
1. Provider-Toggle auf "Gemini" und zurück auf "Ollama" → kein Fehler, Toggle bleibt sticky.
2. Modell-Dropdown wechseln → kein Fehler.
3. "Ollama erweitert" ausklappen, Host auf `http://nicht-existent:11434` ändern → "TESTEN" → erwarte "// FEHLER: …".
4. Host zurück auf `http://localhost:11434`, NumCtx auf `8192`, Temperature auf `0.5`, Speichern → "KALENDER" / "OLLAMA SPEICHERN" wechselt zu disabled.
5. Seite reloaden → Werte sind persistiert.

- [ ] **Step 3: Calendar-Settings + OAuth-Flow**

1. In Google Cloud Console einen OAuth-Client (Type: Desktop) anlegen (oder bestehenden nutzen), Client-ID und -Secret notieren.
2. In den Settings unter "Google Client-ID" und "-Secret" eintragen, "KALENDER SPEICHERN".
3. Status sollte auf "○ NICHT VERBUNDEN" bleiben, "MIT GOOGLE VERBINDEN" wird aktiv.
4. Auf "VERBINDEN" klicken → Auth-Card erscheint mit URL und Code-Input.
5. URL im Browser öffnen, durchklicken, `code=…`-Wert aus der Adresszeile kopieren, in das Input-Feld einfügen, "CODE ÜBERMITTELN".
6. Status flippt auf "● VERBUNDEN", Card schließt.
7. Chat öffnen, "Was steht morgen an?" → Antwort enthält Kalender-Daten (Voraussetzung: Ollama läuft).

- [ ] **Step 4: Token-Flush beim Credential-Wechsel verifizieren**

1. In den Settings auf "ÄNDERN" neben Client-ID klicken.
2. Andere (z.B. dummy) Client-ID/Secret eintragen, speichern.
3. Status sollte zurück auf "○ NICHT VERBUNDEN" springen.
4. SQLite prüfen:
   ```bash
   sqlite3 src/Backend/data/nauassist.db "SELECT COUNT(*) FROM google_oauth;"
   ```
   Expected: `0`.

- [ ] **Step 5: Cleanup-Check**

```bash
ls src/Backend/data/google-credentials.json 2>&1
```
Expected: `No such file` — die Datei darf nicht mehr existieren / erzeugt worden sein.

```bash
grep -i "GoogleCredentialsPath\|CalendarOptions" src/ -r
```
Expected: keine Treffer.

- [ ] **Step 6: Final commit (falls Stand sauber)**

Wenn keine ungetrackten Änderungen mehr da sind: nichts zu tun. Sonst:

```bash
git status
# Bei sauberem Stand: fertig.
```

---

## Self-Review Notes

**Spec-Coverage:** Alle Sektionen des Specs sind Tasks zugeordnet:
- Mockup raus → Task 18
- Ollama Host/ApiKey/NumCtx/Temperature in DB+UI → Tasks 1, 4, 7, 8, 9, 17, 18
- Google Client-ID/Secret in DB → Tasks 1, 6, 13, 14, 17, 18
- Auth-Flow aus UI → Tasks 14, 15, 17, 18
- Token-Flush bei Credential-Wechsel → Task 6
- Ollama-Test-Button → Tasks 10, 18
- FreeSlotCalculator Hot-Reload → Task 11
- CalendarOptions löschen + appsettings/Dockerfile-Cleanup → Tasks 14, 16
- Tests-Anpassung bestehender Suites → Tasks 3, 12, 14

**Risiken:**
1. `Properties/launchSettings.json` legt den lokalen Backend-Port fest — Smoke-Tests in den Tasks nehmen `http://localhost:5000` an, ggf. anpassen.
2. Der `using Microsoft.Extensions.Options;`-Import in `Program.cs` wird nach Cleanup nur dann unbenutzt, wenn keine anderen `IOptions<...>`-Konsumenten mehr da sind — die Datei prüfen.
3. Beim Hot-Reload des `FreeSlotCalculator` muss der Scope einer SSE-Streaming-Verbindung berücksichtigt werden — `AddScoped` reicht aber, weil pro HTTP-Request ein Scope offen ist und Chat-Anfragen Request-basiert sind.
