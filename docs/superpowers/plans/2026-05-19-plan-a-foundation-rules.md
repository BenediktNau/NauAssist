# Plan A — Foundation & Rules — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lauffähige Backend-Skelett-App auf .NET 10 mit Minimal API, Mediator-Pattern (martinothamar), SQLite via Dapper, Migrationssystem, und kompletter Rules-CRUD-Funktionalität inkl. RuleApplicator für deterministische Slot-Annotation.

**Architecture:** Ein einziges `Backend`-Projekt mit Vertical-Slice-Ordnerstruktur (`Features/<Bereich>/<UseCase>/`). Jede fachliche Aktion ist ein Mediator-Request mit zugehörigem Handler. Endpoints sind dünne Minimal-API-Mappings, die `IMediator.Send(...)` aufrufen. Persistenz über SQLite mit handgeschriebenen, eingebetteten SQL-Migrationen.

**Tech Stack:** .NET 10 · ASP.NET Core Minimal API · `Mediator.SourceGenerator` + `Mediator.Abstractions` (martinothamar) · `Microsoft.Data.Sqlite` + `Dapper` · xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `FluentAssertions`

**Bezug zur Spec:** `docs/superpowers/specs/2026-05-19-kalender-agent-mvp-design.md`, Abschnitte 4–6.6 (Foundation, Rules), 6.7 (Audit kommt erst Plan D), 9 (Tests).

**Was am Ende dieses Plans steht:**
- `dotnet build src/NauAssist.slnx` läuft sauber durch
- `dotnet test src/NauAssist.slnx` läuft grün
- `dotnet run --project src/Backend` startet die App, `GET /health` antwortet `ok`
- `GET /api/rules`, `POST /api/rules`, `DELETE /api/rules/{id}` funktionieren
- SQLite-Datei wird beim Startup angelegt, Migrationen laufen idempotent
- `RuleApplicator` annotiert Slot-Kandidaten deterministisch gegen aktive Regeln

---

## Datei-Übersicht (für diesen Plan)

**Neu anzulegen:**

| Pfad | Verantwortung |
|---|---|
| `.gitignore` | .NET-/OS-Standard-Ignorierungen |
| `src/NauAssist.slnx` | Solution-Definition |
| `src/Backend/Backend.csproj` | Backend-Projekt-File |
| `src/Backend/Program.cs` | Host-Konfiguration: DI, Mediator, Endpoints, Migrations |
| `src/Backend/appsettings.json` | Default-Konfiguration (DB-Pfad, Logging) |
| `src/Backend/appsettings.Development.json` | Dev-Overrides |
| `src/Backend/Endpoints/HealthEndpoints.cs` | `/health` |
| `src/Backend/Endpoints/RulesEndpoints.cs` | `/api/rules` (GET, POST, DELETE) |
| `src/Backend/Features/Rules/Rule.cs` | Domain-Modell, inkl. `RuleHardness`-Enum |
| `src/Backend/Features/Rules/RuleRepository.cs` | SQL-Zugriff (CRUD) |
| `src/Backend/Features/Rules/AddRule/AddRuleRequest.cs` | Mediator-Request für AddRule |
| `src/Backend/Features/Rules/AddRule/AddRuleHandler.cs` | Handler dazu |
| `src/Backend/Features/Rules/ListRules/ListRulesRequest.cs` | Mediator-Request |
| `src/Backend/Features/Rules/ListRules/ListRulesHandler.cs` | Handler |
| `src/Backend/Features/Rules/DeleteRule/DeleteRuleRequest.cs` | Mediator-Request |
| `src/Backend/Features/Rules/DeleteRule/DeleteRuleHandler.cs` | Handler |
| `src/Backend/Features/Rules/RuleApplicator.cs` | Deterministisches Filtern/Annotieren von Slot-Kandidaten |
| `src/Backend/Features/Rules/SlotAnnotation.cs` | Result-Modell des RuleApplicators |
| `src/Backend/Features/Infrastructure/Persistence/AppDb.cs` | Verbindungs-Factory |
| `src/Backend/Features/Infrastructure/Persistence/DbInitializer.cs` | Migrations-Runner |
| `src/Backend/Features/Infrastructure/Persistence/Migrations/0001_Init.sql` | Erstes Schema (schema_version, rules) |
| `src/Backend.Tests/Backend.Tests.csproj` | Test-Projekt-File |
| `src/Backend.Tests/Helpers/TestAppFactory.cs` | `WebApplicationFactory`-Subclass für Integration-Tests |
| `src/Backend.Tests/Helpers/TempSqliteDb.cs` | Temp-DB-Helper für Unit-Tests |
| `src/Backend.Tests/Endpoints/HealthEndpointTests.cs` | Smoke-Test gegen `/health` |
| `src/Backend.Tests/Endpoints/RulesEndpointsTests.cs` | End-to-End-Tests REST |
| `src/Backend.Tests/Features/Rules/RuleRepositoryTests.cs` | DB-Zugriff |
| `src/Backend.Tests/Features/Rules/AddRuleHandlerTests.cs` | Handler-Logik |
| `src/Backend.Tests/Features/Rules/ListRulesHandlerTests.cs` | Handler-Logik |
| `src/Backend.Tests/Features/Rules/DeleteRuleHandlerTests.cs` | Handler-Logik |
| `src/Backend.Tests/Features/Rules/RuleApplicatorTests.cs` | Reichhaltige Unit-Tests (deterministische Logik) |
| `src/Backend.Tests/Infrastructure/DbInitializerTests.cs` | Migrations-Korrektheit |

---

## Task 1: Repo-Skelett & Solution

**Files:**
- Create: `.gitignore`
- Create: `src/Backend/Backend.csproj`
- Create: `src/Backend/Program.cs`
- Create: `src/Backend.Tests/Backend.Tests.csproj`
- Create: `src/NauAssist.slnx`

- [ ] **Step 1: `.gitignore` im Repo-Root anlegen**

Datei `.gitignore` mit folgendem Inhalt:

```
# .NET
bin/
obj/
*.user
.vs/
TestResults/
*.pdb

# OS
.DS_Store
Thumbs.db

# Editor
.idea/
.vscode/

# App-Daten
data/
*.db
*.db-journal
*.db-wal
*.db-shm
logs/
*.log

# Frontend (kommt in Plan E)
node_modules/
dist/
.env.local
```

- [ ] **Step 2: Backend-Projekt anlegen**

Run:
```bash
mkdir -p src
dotnet new web -n Backend -o src/Backend --framework net10.0
```

Expected: Verzeichnis `src/Backend/` mit `Backend.csproj` und einer Default-`Program.cs`.

- [ ] **Step 3: Test-Projekt anlegen**

Run:
```bash
dotnet new xunit -n Backend.Tests -o src/Backend.Tests --framework net10.0
```

Expected: Verzeichnis `src/Backend.Tests/` mit Default-`UnitTest1.cs`.

- [ ] **Step 4: Default-Boilerplate in Test-Projekt löschen**

Run:
```bash
rm src/Backend.Tests/UnitTest1.cs
```

- [ ] **Step 5: Project-Reference Tests → Backend hinzufügen**

Run:
```bash
dotnet add src/Backend.Tests/Backend.Tests.csproj reference src/Backend/Backend.csproj
```

- [ ] **Step 6: Solution-Datei anlegen und Projekte einhängen**

Run:
```bash
dotnet new sln -n NauAssist -o src
dotnet sln src/NauAssist.sln add src/Backend/Backend.csproj src/Backend.Tests/Backend.Tests.csproj
```

- [ ] **Step 7: Solution auf SLNX migrieren**

Run:
```bash
dotnet sln src/NauAssist.sln migrate
rm src/NauAssist.sln
```

Expected: `src/NauAssist.slnx` existiert; alte `.sln`-Datei ist weg.

- [ ] **Step 8: Backend.csproj um `InternalsVisibleTo` ergänzen, damit Tests an interne Typen rankommen**

In `src/Backend/Backend.csproj` innerhalb des `<PropertyGroup>` ergänzen:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <RootNamespace>NauAssist.Backend</RootNamespace>
  <AssemblyName>NauAssist.Backend</AssemblyName>
</PropertyGroup>

<ItemGroup>
  <InternalsVisibleTo Include="NauAssist.Backend.Tests" />
</ItemGroup>
```

(Die `<TargetFramework>`-Zeile war schon da; oben sind die ergänzten Zeilen markiert.)

- [ ] **Step 9: Backend.Tests.csproj-Namen alignieren**

In `src/Backend.Tests/Backend.Tests.csproj`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <RootNamespace>NauAssist.Backend.Tests</RootNamespace>
  <AssemblyName>NauAssist.Backend.Tests</AssemblyName>
  <IsPackable>false</IsPackable>
</PropertyGroup>
```

- [ ] **Step 10: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` für beide Projekte.

- [ ] **Step 11: Commit**

```bash
git add .gitignore src/
git commit -m "Plan A Task 1: Solution-Skelett mit Backend + Backend.Tests"
```

---

## Task 2: Health-Endpoint & Integration-Test-Infrastruktur

**Files:**
- Create: `src/Backend/Endpoints/HealthEndpoints.cs`
- Modify: `src/Backend/Program.cs`
- Create: `src/Backend.Tests/Helpers/TestAppFactory.cs`
- Create: `src/Backend.Tests/Endpoints/HealthEndpointTests.cs`
- Modify: `src/Backend.Tests/Backend.Tests.csproj` (NuGets)

- [ ] **Step 1: Test-NuGets hinzufügen**

Run:
```bash
dotnet add src/Backend.Tests/Backend.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add src/Backend.Tests/Backend.Tests.csproj package FluentAssertions
```

- [ ] **Step 2: Health-Endpoint-Test schreiben (FAIL)**

Datei `src/Backend.Tests/Endpoints/HealthEndpointTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class HealthEndpointTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public HealthEndpointTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("ok");
    }
}
```

- [ ] **Step 3: TestAppFactory schreiben**

Datei `src/Backend.Tests/Helpers/TestAppFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NauAssist.Backend.Tests.Helpers;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
```

- [ ] **Step 4: Test laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~HealthEndpointTests"
```

Expected: Build-Fehler (Endpoint nicht existent) oder Test schlägt mit 404 fehl.

- [ ] **Step 5: HealthEndpoints.cs anlegen**

Datei `src/Backend/Endpoints/HealthEndpoints.cs`:

```csharp
namespace NauAssist.Backend.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Text("ok"));
        return app;
    }
}
```

- [ ] **Step 6: Program.cs aktualisieren**

Datei `src/Backend/Program.cs` komplett überschreiben:

```csharp
using NauAssist.Backend.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

// Für WebApplicationFactory<Program>
public partial class Program;
```

- [ ] **Step 7: Test laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~HealthEndpointTests"
```

Expected: `Passed!` mit 1 Test.

- [ ] **Step 8: Manuell starten zur Verifikation**

Run:
```bash
dotnet run --project src/Backend &
sleep 2
curl -s http://localhost:5000/health
kill %1
```

Expected: `ok` als Antwort.

(Der Port kann je nach launchSettings.json variieren; ggf. aus der Konsolen-Ausgabe ablesen.)

- [ ] **Step 9: Commit**

```bash
git add src/
git commit -m "Plan A Task 2: Health-Endpoint + Integration-Test-Infrastruktur"
```

---

## Task 3: Mediator-Setup

**Files:**
- Modify: `src/Backend/Backend.csproj` (NuGets)
- Modify: `src/Backend/Program.cs`
- Create: `src/Backend.Tests/Infrastructure/MediatorRegistrationTests.cs`

- [ ] **Step 1: Mediator-NuGets installieren**

Run:
```bash
dotnet add src/Backend/Backend.csproj package Mediator.Abstractions
dotnet add src/Backend/Backend.csproj package Mediator.SourceGenerator
```

Expected: Beide Pakete erscheinen in `Backend.csproj`. `Mediator.SourceGenerator` wird automatisch mit `OutputItemType="Analyzer"` referenziert (vom NuGet-Paket selbst gesetzt).

- [ ] **Step 2: Failing-Test schreiben**

Datei `src/Backend.Tests/Infrastructure/MediatorRegistrationTests.cs`:

```csharp
using FluentAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Infrastructure;

public sealed class MediatorRegistrationTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public MediatorRegistrationTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Mediator_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();

        var mediator = scope.ServiceProvider.GetService<IMediator>();

        mediator.Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Test laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~MediatorRegistrationTests"
```

Expected: `IMediator` nicht in DI → Test schlägt fehl.

- [ ] **Step 4: Mediator in Program.cs registrieren**

`src/Backend/Program.cs` aktualisieren:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 5: Test laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle bisherigen Tests grün (Health + Mediator).

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Plan A Task 3: Mediator-Setup (martinothamar/Mediator)"
```

---

## Task 4: SQLite & Migrations-Infrastruktur

**Files:**
- Modify: `src/Backend/Backend.csproj` (NuGets, EmbeddedResource)
- Create: `src/Backend/appsettings.json`
- Create: `src/Backend/appsettings.Development.json`
- Create: `src/Backend/Features/Infrastructure/Persistence/AppDb.cs`
- Create: `src/Backend/Features/Infrastructure/Persistence/DbInitializer.cs`
- Create: `src/Backend/Features/Infrastructure/Persistence/Migrations/0001_Init.sql`
- Modify: `src/Backend/Program.cs` (DI + Startup-Hook)
- Create: `src/Backend.Tests/Helpers/TempSqliteDb.cs`
- Create: `src/Backend.Tests/Infrastructure/DbInitializerTests.cs`

- [ ] **Step 1: NuGets installieren**

Run:
```bash
dotnet add src/Backend/Backend.csproj package Microsoft.Data.Sqlite
dotnet add src/Backend/Backend.csproj package Dapper
```

- [ ] **Step 2: appsettings.json anlegen**

Datei `src/Backend/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Persistence": {
    "DatabasePath": "./data/nauassist.db"
  }
}
```

- [ ] **Step 3: appsettings.Development.json anlegen**

Datei `src/Backend/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

- [ ] **Step 4: csproj um EmbeddedResource ergänzen**

In `src/Backend/Backend.csproj` ergänzen:

```xml
<ItemGroup>
  <EmbeddedResource Include="Features/Infrastructure/Persistence/Migrations/*.sql" />
</ItemGroup>
```

- [ ] **Step 5: AppDb (Verbindungs-Factory) schreiben**

Datei `src/Backend/Features/Infrastructure/Persistence/AppDb.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public string DatabasePath { get; set; } = "./data/nauassist.db";
}

public sealed class AppDb
{
    private readonly string _connectionString;

    public AppDb(IOptions<PersistenceOptions> options)
    {
        var path = options.Value.DatabasePath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

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

- [ ] **Step 6: Erste Migration anlegen**

Datei `src/Backend/Features/Infrastructure/Persistence/Migrations/0001_Init.sql`:

```sql
CREATE TABLE schema_version (
    version     TEXT PRIMARY KEY,
    applied_at  TEXT NOT NULL
);

CREATE TABLE rules (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    text            TEXT NOT NULL,
    days_of_week    INTEGER NOT NULL,
    time_range_start TEXT NULL,
    time_range_end   TEXT NULL,
    hardness        TEXT NOT NULL,
    created_at      TEXT NOT NULL
);

CREATE INDEX idx_rules_created_at ON rules(created_at);
```

Erläuterungen für den Implementierer:
- `days_of_week` ist eine Bitmaske: Mo=1, Di=2, Mi=4, Do=8, Fr=16, Sa=32, So=64 (alle Tage = 127)
- `time_range_start`/`time_range_end` sind `HH:MM`-Strings, `NULL` bedeutet "ganzer Tag"
- `hardness` ist `"hard"` oder `"soft"`
- Alle Zeitstempel als ISO-8601-Strings

- [ ] **Step 7: DbInitializer schreiben**

Datei `src/Backend/Features/Infrastructure/Persistence/DbInitializer.cs`:

```csharp
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Infrastructure.Persistence;

public sealed class DbInitializer
{
    private readonly AppDb _db;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDb db, ILogger<DbInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Initialize()
    {
        using var conn = _db.OpenConnection();

        // schema_version-Tabelle ist Teil von Migration 0001 — daher Sonderbehandlung beim allerersten Lauf:
        // Wir prüfen, ob die Tabelle existiert; falls nicht, ist es ein Fresh-DB-Lauf.
        var hasSchemaTable = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version';") == 1;

        var appliedVersions = hasSchemaTable
            ? conn.Query<string>("SELECT version FROM schema_version;").ToHashSet()
            : new HashSet<string>();

        var migrations = LoadMigrations();

        foreach (var (version, sql) in migrations.OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(version))
            {
                _logger.LogDebug("Migration {Version} bereits angewendet, überspringe.", version);
                continue;
            }

            _logger.LogInformation("Wende Migration {Version} an.", version);

            using var tx = conn.BeginTransaction();
            try
            {
                conn.Execute(sql, transaction: tx);

                // schema_version-Eintrag schreiben (auch beim allerersten Mal, weil die Tabelle in 0001 erst entsteht)
                conn.Execute(
                    "INSERT INTO schema_version(version, applied_at) VALUES(@v, @a);",
                    new { v = version, a = DateTimeOffset.UtcNow.ToString("O") },
                    transaction: tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    private static IReadOnlyList<(string Version, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "NauAssist.Backend.Features.Infrastructure.Persistence.Migrations.";

        var result = new List<(string, string)>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix) || !name.EndsWith(".sql"))
            {
                continue;
            }

            var relative = name[prefix.Length..^4]; // "0001_Init"
            var version = relative.Split('_', 2)[0]; // "0001"

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Migration-Resource {name} nicht lesbar.");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            result.Add((version, sql));
        }
        return result;
    }
}
```

- [ ] **Step 8: Program.cs an Persistenz binden**

`src/Backend/Program.cs` aktualisieren:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Endpoints;
using NauAssist.Backend.Features.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<DbInitializer>();

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

var app = builder.Build();

// Migrationen beim Startup ausführen
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    initializer.Initialize();
}

app.MapHealthEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 9: TempSqliteDb-Helper schreiben**

Datei `src/Backend.Tests/Helpers/TempSqliteDb.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// Erzeugt eine frische SQLite-Datei im temporären Verzeichnis,
/// wendet alle Migrationen an, und räumt am Ende auf.
/// </summary>
public sealed class TempSqliteDb : IDisposable
{
    public string Path { get; }
    public AppDb AppDb { get; }

    public TempSqliteDb()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"nauassist-test-{Guid.NewGuid():N}.db");

        var options = Options.Create(new PersistenceOptions { DatabasePath = Path });
        AppDb = new AppDb(options);

        var initializer = new DbInitializer(AppDb, NullLogger<DbInitializer>.Instance);
        initializer.Initialize();
    }

    public void Dispose()
    {
        try
        {
            // SQLite-Pools schließen, damit die Datei freigegeben wird
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // Bei Aufräumfehler im Test einfach durchlassen — Temp wird vom OS irgendwann frei
        }
    }
}
```

- [ ] **Step 10: DbInitializer-Test schreiben**

Datei `src/Backend.Tests/Infrastructure/DbInitializerTests.cs`:

```csharp
using Dapper;
using FluentAssertions;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Infrastructure;

public sealed class DbInitializerTests
{
    [Fact]
    public void Initialize_OnFreshDb_CreatesSchemaVersionAndRulesTables()
    {
        using var db = new TempSqliteDb();
        using var conn = db.AppDb.OpenConnection();

        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;")
            .ToList();

        tables.Should().Contain("schema_version");
        tables.Should().Contain("rules");
    }

    [Fact]
    public void Initialize_RecordsAppliedMigrationsInSchemaVersion()
    {
        using var db = new TempSqliteDb();
        using var conn = db.AppDb.OpenConnection();

        var versions = conn.Query<string>("SELECT version FROM schema_version;").ToList();

        versions.Should().Contain("0001");
    }

    [Fact]
    public void Initialize_IsIdempotent_DoesNotReapplyMigrations()
    {
        using var db = new TempSqliteDb();

        // Zweiter Init-Aufruf darf nicht crashen und keine Duplikate erzeugen
        var initializer = new DbInitializer(db.AppDb, Microsoft.Extensions.Logging.Abstractions.NullLogger<DbInitializer>.Instance);
        initializer.Initialize();

        using var conn = db.AppDb.OpenConnection();
        var count = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM schema_version WHERE version='0001';");
        count.Should().Be(1);
    }
}
```

- [ ] **Step 11: Tests laufen lassen, GREEN bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle Tests grün.

- [ ] **Step 12: Manuell verifizieren**

Run:
```bash
rm -rf src/Backend/data
dotnet run --project src/Backend &
sleep 3
ls -la src/Backend/data/
kill %1
```

Expected: `data/nauassist.db` ist angelegt.

Cleanup:
```bash
rm -rf src/Backend/data
```

- [ ] **Step 13: Commit**

```bash
git add src/
git commit -m "Plan A Task 4: SQLite + Migrations-Infrastruktur, erste Migration (schema_version + rules)"
```

---

## Task 5: Rule-Domain-Modell

**Files:**
- Create: `src/Backend/Features/Rules/Rule.cs`
- Create: `src/Backend.Tests/Features/Rules/RuleTests.cs`

- [ ] **Step 1: Test für Rule-Modell und DayOfWeekFlags-Bitmaske schreiben**

Datei `src/Backend.Tests/Features/Rules/RuleTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class RuleTests
{
    [Fact]
    public void DayOfWeekFlags_WeekdaysOnly_HasMondayThruFriday()
    {
        var weekdays = DayOfWeekFlags.WeekdaysOnly;

        weekdays.HasFlag(DayOfWeekFlags.Monday).Should().BeTrue();
        weekdays.HasFlag(DayOfWeekFlags.Friday).Should().BeTrue();
        weekdays.HasFlag(DayOfWeekFlags.Saturday).Should().BeFalse();
        weekdays.HasFlag(DayOfWeekFlags.Sunday).Should().BeFalse();
    }

    [Fact]
    public void DayOfWeekFlags_AllDays_HasAllSevenDays()
    {
        var all = DayOfWeekFlags.AllDays;
        ((int)all).Should().Be(127); // 2^7 - 1
    }

    [Fact]
    public void Rule_CanBeConstructed_WithValidProperties()
    {
        var rule = new Rule(
            Id: 1,
            Text: "Mo–Fr nach 18 Uhr lieber frei",
            DaysOfWeek: DayOfWeekFlags.WeekdaysOnly,
            TimeRangeStart: new TimeOnly(18, 0),
            TimeRangeEnd: new TimeOnly(23, 59),
            Hardness: RuleHardness.Soft,
            CreatedAt: DateTimeOffset.UtcNow);

        rule.Text.Should().Be("Mo–Fr nach 18 Uhr lieber frei");
        rule.Hardness.Should().Be(RuleHardness.Soft);
    }
}
```

- [ ] **Step 2: Test laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RuleTests"
```

Expected: Compile-Fehler (Typen existieren noch nicht).

- [ ] **Step 3: Rule.cs implementieren**

Datei `src/Backend/Features/Rules/Rule.cs`:

```csharp
namespace NauAssist.Backend.Features.Rules;

[Flags]
public enum DayOfWeekFlags
{
    None      = 0,
    Monday    = 1 << 0,
    Tuesday   = 1 << 1,
    Wednesday = 1 << 2,
    Thursday  = 1 << 3,
    Friday    = 1 << 4,
    Saturday  = 1 << 5,
    Sunday    = 1 << 6,

    WeekdaysOnly = Monday | Tuesday | Wednesday | Thursday | Friday,
    WeekendOnly  = Saturday | Sunday,
    AllDays      = WeekdaysOnly | WeekendOnly,
}

public enum RuleHardness
{
    Hard,
    Soft,
}

public sealed record Rule(
    long Id,
    string Text,
    DayOfWeekFlags DaysOfWeek,
    TimeOnly? TimeRangeStart,
    TimeOnly? TimeRangeEnd,
    RuleHardness Hardness,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 4: Test laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RuleTests"
```

Expected: 3 Tests grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan A Task 5: Rule-Domain-Modell mit DayOfWeekFlags-Bitmaske und Hardness-Enum"
```

---

## Task 6: RuleRepository

**Files:**
- Create: `src/Backend/Features/Rules/RuleRepository.cs`
- Create: `src/Backend.Tests/Features/Rules/RuleRepositoryTests.cs`

- [ ] **Step 1: Failing-Test für Add/List/Delete schreiben**

Datei `src/Backend.Tests/Features/Rules/RuleRepositoryTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class RuleRepositoryTests
{
    [Fact]
    public async Task Add_PersistsRule_AndAssignsId()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var draft = new Rule(
            Id: 0,
            Text: "Mo–Fr nach 18 Uhr nicht",
            DaysOfWeek: DayOfWeekFlags.WeekdaysOnly,
            TimeRangeStart: new TimeOnly(18, 0),
            TimeRangeEnd: new TimeOnly(23, 59),
            Hardness: RuleHardness.Hard,
            CreatedAt: DateTimeOffset.UtcNow);

        var saved = await repo.AddAsync(draft, CancellationToken.None);

        saved.Id.Should().BeGreaterThan(0);
        saved.Text.Should().Be("Mo–Fr nach 18 Uhr nicht");
    }

    [Fact]
    public async Task ListAll_ReturnsAddedRules_OrderedByCreatedAt()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var first = await repo.AddAsync(MakeRule("Regel A", DateTimeOffset.UtcNow.AddMinutes(-10)), CancellationToken.None);
        var second = await repo.AddAsync(MakeRule("Regel B", DateTimeOffset.UtcNow), CancellationToken.None);

        var all = await repo.ListAllAsync(CancellationToken.None);

        all.Should().HaveCount(2);
        all[0].Id.Should().Be(first.Id);
        all[1].Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task Delete_RemovesRule_AndReturnsTrue()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var saved = await repo.AddAsync(MakeRule("zum Löschen", DateTimeOffset.UtcNow), CancellationToken.None);

        var deleted = await repo.DeleteAsync(saved.Id, CancellationToken.None);

        deleted.Should().BeTrue();
        var all = await repo.ListAllAsync(CancellationToken.None);
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NonexistentId_ReturnsFalse()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var deleted = await repo.DeleteAsync(99999, CancellationToken.None);

        deleted.Should().BeFalse();
    }

    private static Rule MakeRule(string text, DateTimeOffset createdAt) => new(
        Id: 0,
        Text: text,
        DaysOfWeek: DayOfWeekFlags.AllDays,
        TimeRangeStart: null,
        TimeRangeEnd: null,
        Hardness: RuleHardness.Soft,
        CreatedAt: createdAt);
}
```

- [ ] **Step 2: Test laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RuleRepositoryTests"
```

Expected: Compile-Fehler — `RuleRepository` existiert nicht.

- [ ] **Step 3: RuleRepository implementieren**

Datei `src/Backend/Features/Rules/RuleRepository.cs`:

```csharp
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Rules;

public sealed class RuleRepository
{
    private readonly AppDb _db;

    public RuleRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<Rule> AddAsync(Rule draft, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO rules(text, days_of_week, time_range_start, time_range_end, hardness, created_at)
              VALUES(@Text, @Days, @Start, @End, @Hardness, @CreatedAt);
              SELECT last_insert_rowid();",
            new
            {
                Text = draft.Text,
                Days = (int)draft.DaysOfWeek,
                Start = draft.TimeRangeStart?.ToString("HH:mm"),
                End = draft.TimeRangeEnd?.ToString("HH:mm"),
                Hardness = draft.Hardness.ToString().ToLowerInvariant(),
                CreatedAt = draft.CreatedAt.ToString("O"),
            },
            cancellationToken: ct));

        return draft with { Id = id };
    }

    public async Task<IReadOnlyList<Rule>> ListAllAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var rows = await conn.QueryAsync<RuleRow>(new CommandDefinition(
            "SELECT id, text, days_of_week, time_range_start, time_range_end, hardness, created_at FROM rules ORDER BY created_at ASC;",
            cancellationToken: ct));

        return rows.Select(MapToDomain).ToList();
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM rules WHERE id = @Id;",
            new { Id = id },
            cancellationToken: ct));

        return rows > 0;
    }

    private static Rule MapToDomain(RuleRow row) => new(
        Id: row.id,
        Text: row.text,
        DaysOfWeek: (DayOfWeekFlags)row.days_of_week,
        TimeRangeStart: ParseTime(row.time_range_start),
        TimeRangeEnd: ParseTime(row.time_range_end),
        Hardness: Enum.Parse<RuleHardness>(row.hardness, ignoreCase: true),
        CreatedAt: DateTimeOffset.Parse(row.created_at));

    private static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrEmpty(value) ? null : TimeOnly.Parse(value);

    private sealed record RuleRow(
        long id,
        string text,
        int days_of_week,
        string? time_range_start,
        string? time_range_end,
        string hardness,
        string created_at);
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RuleRepositoryTests"
```

Expected: 4 Tests grün.

- [ ] **Step 5: Repository in DI registrieren**

`src/Backend/Program.cs` ergänzen (vor `var app = builder.Build();`):

```csharp
builder.Services.AddScoped<NauAssist.Backend.Features.Rules.RuleRepository>();
```

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Plan A Task 6: RuleRepository mit Add/List/Delete und Tests"
```

---

## Task 7: AddRule-Handler

**Files:**
- Create: `src/Backend/Features/Rules/AddRule/AddRuleRequest.cs`
- Create: `src/Backend/Features/Rules/AddRule/AddRuleHandler.cs`
- Create: `src/Backend.Tests/Features/Rules/AddRuleHandlerTests.cs`

**Hinweis:** Dieser Handler nimmt bereits strukturierte Rule-Daten an. Das LLM-basierte Parsing aus Natursprache (`add_rule(natural_text)` aus der Spec Abschnitt 6.5) kommt in Plan D als zweiter Handler obendrauf, der dann diesen hier aufruft.

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Rules/AddRuleHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class AddRuleHandlerTests
{
    [Fact]
    public async Task Handle_PersistsRule_AndReturnsIt()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new AddRuleHandler(repo, () => DateTimeOffset.Parse("2026-05-19T12:00:00+02:00"));

        var request = new AddRuleRequest(
            Text: "Mi 19–20 Uhr Sport",
            DaysOfWeek: DayOfWeekFlags.Wednesday,
            TimeRangeStart: new TimeOnly(19, 0),
            TimeRangeEnd: new TimeOnly(20, 0),
            Hardness: RuleHardness.Hard);

        var response = await handler.Handle(request, CancellationToken.None);

        response.Rule.Id.Should().BeGreaterThan(0);
        response.Rule.Text.Should().Be("Mi 19–20 Uhr Sport");
        response.Rule.DaysOfWeek.Should().Be(DayOfWeekFlags.Wednesday);
        response.Rule.Hardness.Should().Be(RuleHardness.Hard);
        response.Rule.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-05-19T12:00:00+02:00"));
    }

    [Fact]
    public async Task Handle_RejectsEmptyText()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new AddRuleHandler(repo, () => DateTimeOffset.UtcNow);

        var request = new AddRuleRequest(
            Text: "",
            DaysOfWeek: DayOfWeekFlags.AllDays,
            TimeRangeStart: null,
            TimeRangeEnd: null,
            Hardness: RuleHardness.Soft);

        var act = async () => await handler.Handle(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Text*");
    }

    [Fact]
    public async Task Handle_RejectsEndBeforeStart()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new AddRuleHandler(repo, () => DateTimeOffset.UtcNow);

        var request = new AddRuleRequest(
            Text: "Quatsch-Range",
            DaysOfWeek: DayOfWeekFlags.AllDays,
            TimeRangeStart: new TimeOnly(20, 0),
            TimeRangeEnd: new TimeOnly(18, 0),
            Hardness: RuleHardness.Soft);

        var act = async () => await handler.Handle(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*TimeRange*");
    }
}
```

- [ ] **Step 2: Tests laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~AddRuleHandlerTests"
```

Expected: Compile-Fehler.

- [ ] **Step 3: AddRuleRequest schreiben**

Datei `src/Backend/Features/Rules/AddRule/AddRuleRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Rules.AddRule;

public sealed record AddRuleRequest(
    string Text,
    DayOfWeekFlags DaysOfWeek,
    TimeOnly? TimeRangeStart,
    TimeOnly? TimeRangeEnd,
    RuleHardness Hardness) : IRequest<AddRuleResponse>;

public sealed record AddRuleResponse(Rule Rule);
```

- [ ] **Step 4: AddRuleHandler schreiben**

Datei `src/Backend/Features/Rules/AddRule/AddRuleHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Rules.AddRule;

public sealed class AddRuleHandler : IRequestHandler<AddRuleRequest, AddRuleResponse>
{
    private readonly RuleRepository _repo;
    private readonly Func<DateTimeOffset> _clock;

    public AddRuleHandler(RuleRepository repo, Func<DateTimeOffset> clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public async ValueTask<AddRuleResponse> Handle(AddRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Text darf nicht leer sein.", nameof(request));
        }

        if (request.TimeRangeStart.HasValue && request.TimeRangeEnd.HasValue
            && request.TimeRangeEnd.Value <= request.TimeRangeStart.Value)
        {
            throw new ArgumentException("TimeRangeEnd muss nach TimeRangeStart liegen.", nameof(request));
        }

        if (request.DaysOfWeek == DayOfWeekFlags.None)
        {
            throw new ArgumentException("DaysOfWeek muss mindestens einen Tag enthalten.", nameof(request));
        }

        var draft = new Rule(
            Id: 0,
            Text: request.Text.Trim(),
            DaysOfWeek: request.DaysOfWeek,
            TimeRangeStart: request.TimeRangeStart,
            TimeRangeEnd: request.TimeRangeEnd,
            Hardness: request.Hardness,
            CreatedAt: _clock());

        var saved = await _repo.AddAsync(draft, cancellationToken);
        return new AddRuleResponse(saved);
    }
}
```

- [ ] **Step 5: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~AddRuleHandlerTests"
```

Expected: 3 Tests grün.

- [ ] **Step 6: Clock in DI registrieren**

`src/Backend/Program.cs` ergänzen (vor Mediator-Registrierung):

```csharp
builder.Services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
```

(Damit hat der Handler eine echte Uhr in Produktion; Tests injizieren ihre eigene Lambda.)

- [ ] **Step 7: Commit**

```bash
git add src/
git commit -m "Plan A Task 7: AddRule-Handler mit Validierung"
```

---

## Task 8: ListRules-Handler

**Files:**
- Create: `src/Backend/Features/Rules/ListRules/ListRulesRequest.cs`
- Create: `src/Backend/Features/Rules/ListRules/ListRulesHandler.cs`
- Create: `src/Backend.Tests/Features/Rules/ListRulesHandlerTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Rules/ListRulesHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.ListRules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class ListRulesHandlerTests
{
    [Fact]
    public async Task Handle_OnEmptyDb_ReturnsEmptyList()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new ListRulesHandler(repo);

        var response = await handler.Handle(new ListRulesRequest(), CancellationToken.None);

        response.Rules.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsAllSavedRules()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new ListRulesHandler(repo);

        await repo.AddAsync(new Rule(0, "A", DayOfWeekFlags.AllDays, null, null, RuleHardness.Soft, DateTimeOffset.UtcNow.AddMinutes(-5)), CancellationToken.None);
        await repo.AddAsync(new Rule(0, "B", DayOfWeekFlags.AllDays, null, null, RuleHardness.Hard, DateTimeOffset.UtcNow), CancellationToken.None);

        var response = await handler.Handle(new ListRulesRequest(), CancellationToken.None);

        response.Rules.Should().HaveCount(2);
        response.Rules.Select(r => r.Text).Should().ContainInOrder("A", "B");
    }
}
```

- [ ] **Step 2: Tests laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~ListRulesHandlerTests"
```

Expected: Compile-Fehler.

- [ ] **Step 3: ListRulesRequest schreiben**

Datei `src/Backend/Features/Rules/ListRules/ListRulesRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Rules.ListRules;

public sealed record ListRulesRequest() : IRequest<ListRulesResponse>;

public sealed record ListRulesResponse(IReadOnlyList<Rule> Rules);
```

- [ ] **Step 4: ListRulesHandler schreiben**

Datei `src/Backend/Features/Rules/ListRules/ListRulesHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Rules.ListRules;

public sealed class ListRulesHandler : IRequestHandler<ListRulesRequest, ListRulesResponse>
{
    private readonly RuleRepository _repo;

    public ListRulesHandler(RuleRepository repo)
    {
        _repo = repo;
    }

    public async ValueTask<ListRulesResponse> Handle(ListRulesRequest request, CancellationToken cancellationToken)
    {
        var rules = await _repo.ListAllAsync(cancellationToken);
        return new ListRulesResponse(rules);
    }
}
```

- [ ] **Step 5: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~ListRulesHandlerTests"
```

Expected: 2 Tests grün.

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Plan A Task 8: ListRules-Handler"
```

---

## Task 9: DeleteRule-Handler

**Files:**
- Create: `src/Backend/Features/Rules/DeleteRule/DeleteRuleRequest.cs`
- Create: `src/Backend/Features/Rules/DeleteRule/DeleteRuleHandler.cs`
- Create: `src/Backend.Tests/Features/Rules/DeleteRuleHandlerTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Rules/DeleteRuleHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.DeleteRule;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class DeleteRuleHandlerTests
{
    [Fact]
    public async Task Handle_DeletesExistingRule_ReturnsTrue()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new DeleteRuleHandler(repo);

        var saved = await repo.AddAsync(new Rule(0, "weg", DayOfWeekFlags.AllDays, null, null, RuleHardness.Soft, DateTimeOffset.UtcNow), CancellationToken.None);

        var response = await handler.Handle(new DeleteRuleRequest(saved.Id), CancellationToken.None);

        response.Deleted.Should().BeTrue();
        (await repo.ListAllAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NonexistentId_ReturnsFalse()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new DeleteRuleHandler(repo);

        var response = await handler.Handle(new DeleteRuleRequest(99999), CancellationToken.None);

        response.Deleted.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Tests laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~DeleteRuleHandlerTests"
```

Expected: Compile-Fehler.

- [ ] **Step 3: DeleteRuleRequest schreiben**

Datei `src/Backend/Features/Rules/DeleteRule/DeleteRuleRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Rules.DeleteRule;

public sealed record DeleteRuleRequest(long Id) : IRequest<DeleteRuleResponse>;

public sealed record DeleteRuleResponse(bool Deleted);
```

- [ ] **Step 4: DeleteRuleHandler schreiben**

Datei `src/Backend/Features/Rules/DeleteRule/DeleteRuleHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Rules.DeleteRule;

public sealed class DeleteRuleHandler : IRequestHandler<DeleteRuleRequest, DeleteRuleResponse>
{
    private readonly RuleRepository _repo;

    public DeleteRuleHandler(RuleRepository repo)
    {
        _repo = repo;
    }

    public async ValueTask<DeleteRuleResponse> Handle(DeleteRuleRequest request, CancellationToken cancellationToken)
    {
        var deleted = await _repo.DeleteAsync(request.Id, cancellationToken);
        return new DeleteRuleResponse(deleted);
    }
}
```

- [ ] **Step 5: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~DeleteRuleHandlerTests"
```

Expected: 2 Tests grün.

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Plan A Task 9: DeleteRule-Handler"
```

---

## Task 10: RulesEndpoints (Minimal API)

**Files:**
- Create: `src/Backend/Endpoints/RulesEndpoints.cs`
- Modify: `src/Backend/Program.cs`
- Create: `src/Backend.Tests/Endpoints/RulesEndpointsTests.cs`

- [ ] **Step 1: End-to-End-Failing-Test schreiben**

Datei `src/Backend.Tests/Endpoints/RulesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class RulesEndpointsTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public RulesEndpointsTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullCycle_PostListDelete_WorksEndToEnd()
    {
        var client = _factory.CreateClient();

        // POST
        var postBody = new
        {
            text = "Mo–Fr nach 18 Uhr nicht",
            daysOfWeek = (int)DayOfWeekFlags.WeekdaysOnly,
            timeRangeStart = "18:00",
            timeRangeEnd = "23:59",
            hardness = "hard",
        };
        var postResp = await client.PostAsJsonAsync("/api/rules", postBody);
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var posted = await postResp.Content.ReadFromJsonAsync<RuleDto>();
        posted!.Id.Should().BeGreaterThan(0);

        // GET
        var getResp = await client.GetAsync("/api/rules");
        getResp.IsSuccessStatusCode.Should().BeTrue();
        var list = await getResp.Content.ReadFromJsonAsync<List<RuleDto>>();
        list!.Should().ContainSingle(r => r.Id == posted.Id);

        // DELETE
        var delResp = await client.DeleteAsync($"/api/rules/{posted.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // GET nach DELETE
        var getEmptyResp = await client.GetAsync("/api/rules");
        var empty = await getEmptyResp.Content.ReadFromJsonAsync<List<RuleDto>>();
        empty!.Should().NotContain(r => r.Id == posted.Id);
    }

    [Fact]
    public async Task Post_WithEmptyText_Returns400()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/rules", new
        {
            text = "",
            daysOfWeek = (int)DayOfWeekFlags.AllDays,
            timeRangeStart = (string?)null,
            timeRangeEnd = (string?)null,
            hardness = "soft",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_NonexistentId_Returns404()
    {
        var client = _factory.CreateClient();

        var resp = await client.DeleteAsync("/api/rules/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record RuleDto(
        long Id,
        string Text,
        int DaysOfWeek,
        string? TimeRangeStart,
        string? TimeRangeEnd,
        string Hardness,
        DateTimeOffset CreatedAt);
}
```

**Wichtig:** Damit dieser Test mit einer frischen DB läuft (nicht der Produktions-DB), muss `TestAppFactory` einen Temp-DB-Pfad konfigurieren. Das machen wir im nächsten Step.

- [ ] **Step 2: TestAppFactory erweitern, dass Tests gegen eine Temp-DB laufen**

Datei `src/Backend.Tests/Helpers/TestAppFactory.cs` komplett überschreiben:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NauAssist.Backend.Tests.Helpers;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public TestAppFactory()
    {
        _dbPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"nauassist-integration-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:DatabasePath"] = _dbPath,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch { /* OS-Aufräumung übernimmt im Notfall */ }
    }
}
```

- [ ] **Step 3: Test laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RulesEndpointsTests"
```

Expected: 404 für alle Endpoints (existieren nicht).

- [ ] **Step 4: RulesEndpoints implementieren**

Datei `src/Backend/Endpoints/RulesEndpoints.cs`:

```csharp
using Mediator;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;
using NauAssist.Backend.Features.Rules.DeleteRule;
using NauAssist.Backend.Features.Rules.ListRules;

namespace NauAssist.Backend.Endpoints;

public static class RulesEndpoints
{
    public static IEndpointRouteBuilder MapRulesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rules");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new ListRulesRequest(), ct);
            return Results.Ok(response.Rules.Select(ToDto));
        });

        group.MapPost("/", async (AddRuleDto body, IMediator mediator, CancellationToken ct) =>
        {
            try
            {
                var request = new AddRuleRequest(
                    Text: body.Text,
                    DaysOfWeek: (DayOfWeekFlags)body.DaysOfWeek,
                    TimeRangeStart: ParseTime(body.TimeRangeStart),
                    TimeRangeEnd: ParseTime(body.TimeRangeEnd),
                    Hardness: Enum.Parse<RuleHardness>(body.Hardness, ignoreCase: true));

                var response = await mediator.Send(request, ct);
                return Results.Created($"/api/rules/{response.Rule.Id}", ToDto(response.Rule));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{id:long}", async (long id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DeleteRuleRequest(id), ct);
            return response.Deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static TimeOnly? ParseTime(string? raw) =>
        string.IsNullOrEmpty(raw) ? null : TimeOnly.Parse(raw);

    private static RuleDto ToDto(Rule r) => new(
        r.Id,
        r.Text,
        (int)r.DaysOfWeek,
        r.TimeRangeStart?.ToString("HH:mm"),
        r.TimeRangeEnd?.ToString("HH:mm"),
        r.Hardness.ToString().ToLowerInvariant(),
        r.CreatedAt);

    private sealed record AddRuleDto(
        string Text,
        int DaysOfWeek,
        string? TimeRangeStart,
        string? TimeRangeEnd,
        string Hardness);

    private sealed record RuleDto(
        long Id,
        string Text,
        int DaysOfWeek,
        string? TimeRangeStart,
        string? TimeRangeEnd,
        string Hardness,
        DateTimeOffset CreatedAt);
}
```

- [ ] **Step 5: Program.cs an die neuen Endpoints binden**

`src/Backend/Program.cs` aktualisieren — die `app.MapHealthEndpoints();`-Zeile erweitern:

```csharp
app.MapHealthEndpoints();
app.MapRulesEndpoints();
```

- [ ] **Step 6: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle Tests grün, inkl. der drei neuen Endpoint-Tests.

- [ ] **Step 7: Manuell verifizieren via cURL**

Run:
```bash
rm -rf src/Backend/data
dotnet run --project src/Backend &
sleep 3

curl -sX POST http://localhost:5000/api/rules \
  -H "Content-Type: application/json" \
  -d '{"text":"Mo-Fr nach 18 nicht","daysOfWeek":31,"timeRangeStart":"18:00","timeRangeEnd":"23:59","hardness":"hard"}'

echo ""
curl -s http://localhost:5000/api/rules
echo ""

kill %1
rm -rf src/Backend/data
```

Expected: POST gibt JSON mit Id zurück; GET listet die Regel.

- [ ] **Step 8: Commit**

```bash
git add src/
git commit -m "Plan A Task 10: RulesEndpoints (Minimal API) + End-to-End-Tests"
```

---

## Task 11: RuleApplicator

**Files:**
- Create: `src/Backend/Features/Rules/SlotAnnotation.cs`
- Create: `src/Backend/Features/Rules/RuleApplicator.cs`
- Create: `src/Backend.Tests/Features/Rules/RuleApplicatorTests.cs`

**Hintergrund:** Der `RuleApplicator` ist reine, deterministische Logik (kein DI, kein LLM). Er bekommt eine Liste von Slot-Kandidaten und die aktiven Regeln, und gibt für jeden Slot eine `SlotAnnotation` zurück: `Passes`, `Violates(hard)` oder `Violates(soft)`. Er ist die zentrale Stelle, an der Regeln auf konkrete Termin-Slots wirken.

- [ ] **Step 1: Slot-Kandidaten-Typ in der Domain festlegen**

Datei `src/Backend/Features/Rules/SlotAnnotation.cs`:

```csharp
namespace NauAssist.Backend.Features.Rules;

/// <summary>
/// Ein Vorschlag im Zeitfenster, der gegen Regeln geprüft wird.
/// Plan A definiert diesen Typ hier; Plan B (Kalender) wird ihn nutzen.
/// </summary>
public sealed record SlotCandidate(DateTimeOffset Start, DateTimeOffset End);

public enum AnnotationStatus
{
    Passes,
    SoftViolation,
    HardViolation,
}

public sealed record SlotAnnotation(
    SlotCandidate Slot,
    AnnotationStatus Status,
    Rule? ViolatedBy);
```

- [ ] **Step 2: RuleApplicator-Tests schreiben (deterministische Logik = viele Cases)**

Datei `src/Backend.Tests/Features/Rules/RuleApplicatorTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class RuleApplicatorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void Annotate_NoRules_AllSlotsPass()
    {
        var slot = SlotAt(2026, 5, 27, 14, 0, 15, 0);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, Array.Empty<Rule>());

        result.Should().HaveCount(1);
        result[0].Status.Should().Be(AnnotationStatus.Passes);
        result[0].ViolatedBy.Should().BeNull();
    }

    [Fact]
    public void Annotate_SlotOutsideRuleHours_Passes()
    {
        var slot = SlotAt(2026, 5, 27, 10, 0, 11, 0); // Mi 10:00–11:00
        var rule = HardRuleAfter18Mondays();

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.Passes);
    }

    [Fact]
    public void Annotate_SlotMatchesHardRule_ReturnsHardViolation()
    {
        // Mo 18:30–19:30 — fällt in 18-23:59
        var slot = SlotAt(2026, 5, 25, 18, 30, 19, 30);
        var rule = HardRuleAfter18Mondays();

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
        result[0].ViolatedBy.Should().Be(rule);
    }

    [Fact]
    public void Annotate_SlotMatchesSoftRule_ReturnsSoftViolation()
    {
        var slot = SlotAt(2026, 5, 25, 18, 30, 19, 30);
        var rule = HardRuleAfter18Mondays() with { Hardness = RuleHardness.Soft };

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.SoftViolation);
        result[0].ViolatedBy.Should().Be(rule);
    }

    [Fact]
    public void Annotate_SlotDayMismatch_DoesNotApplyRule()
    {
        // Rule gilt Mo–Fr, Slot ist Samstag
        var slot = SlotAt(2026, 5, 30, 18, 30, 19, 30);
        var rule = new Rule(1, "Mo–Fr nach 18 nicht", DayOfWeekFlags.WeekdaysOnly,
            new TimeOnly(18, 0), new TimeOnly(23, 59), RuleHardness.Hard, DateTimeOffset.UtcNow);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.Passes);
    }

    [Fact]
    public void Annotate_HardRuleBeatsSoftWhenBothMatch()
    {
        var slot = SlotAt(2026, 5, 25, 18, 30, 19, 30);

        var softRule = new Rule(1, "Soft Abend", DayOfWeekFlags.AllDays,
            new TimeOnly(18, 0), new TimeOnly(23, 59), RuleHardness.Soft, DateTimeOffset.UtcNow);
        var hardRule = new Rule(2, "Hard Mo Abend", DayOfWeekFlags.Monday,
            new TimeOnly(18, 0), new TimeOnly(23, 59), RuleHardness.Hard, DateTimeOffset.UtcNow);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { softRule, hardRule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
        result[0].ViolatedBy.Should().Be(hardRule);
    }

    [Fact]
    public void Annotate_RuleWithoutTimeRange_AppliesToEntireDay()
    {
        // Rule "Sonntag nie" — ohne TimeRange
        var slot = SlotAt(2026, 5, 31, 10, 0, 11, 0); // Sonntag
        var rule = new Rule(1, "Sonntag nie", DayOfWeekFlags.Sunday,
            TimeRangeStart: null, TimeRangeEnd: null, RuleHardness.Hard, DateTimeOffset.UtcNow);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
    }

    [Fact]
    public void Annotate_SlotPartiallyOverlapsRule_IsAViolation()
    {
        // Slot 17:30–18:30, Rule ab 18:00 — Overlap mindestens ein Teil
        var slot = SlotAt(2026, 5, 25, 17, 30, 18, 30);
        var rule = HardRuleAfter18Mondays();

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
    }

    /// <summary>Slot konstruiert in Europe/Berlin-Lokalzeit.</summary>
    private static SlotCandidate SlotAt(int year, int month, int day, int startH, int startM, int endH, int endM)
    {
        var start = new DateTimeOffset(year, month, day, startH, startM, 0,
            Berlin.GetUtcOffset(new DateTime(year, month, day, startH, startM, 0)));
        var end = new DateTimeOffset(year, month, day, endH, endM, 0,
            Berlin.GetUtcOffset(new DateTime(year, month, day, endH, endM, 0)));
        return new SlotCandidate(start, end);
    }

    /// <summary>Hilfs-Factory: "Mo nach 18 nicht" als harte Regel.</summary>
    private static Rule HardRuleAfter18Mondays() => new(
        Id: 1,
        Text: "Mo nach 18 Uhr nicht",
        DaysOfWeek: DayOfWeekFlags.Monday,
        TimeRangeStart: new TimeOnly(18, 0),
        TimeRangeEnd: new TimeOnly(23, 59),
        Hardness: RuleHardness.Hard,
        CreatedAt: DateTimeOffset.UtcNow);
}
```

- [ ] **Step 3: Tests laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RuleApplicatorTests"
```

Expected: Compile-Fehler — `RuleApplicator` existiert nicht.

- [ ] **Step 4: RuleApplicator implementieren**

Datei `src/Backend/Features/Rules/RuleApplicator.cs`:

```csharp
namespace NauAssist.Backend.Features.Rules;

public sealed class RuleApplicator
{
    private readonly TimeZoneInfo _localZone;

    public RuleApplicator(TimeZoneInfo localZone)
    {
        _localZone = localZone;
    }

    public IReadOnlyList<SlotAnnotation> Annotate(
        IEnumerable<SlotCandidate> slots,
        IEnumerable<Rule> rules)
    {
        var ruleList = rules.ToList();
        var result = new List<SlotAnnotation>();

        foreach (var slot in slots)
        {
            var violations = ruleList
                .Where(r => Matches(slot, r))
                .ToList();

            if (violations.Count == 0)
            {
                result.Add(new SlotAnnotation(slot, AnnotationStatus.Passes, null));
                continue;
            }

            // Harte Verstöße haben Vorrang vor weichen
            var hard = violations.FirstOrDefault(r => r.Hardness == RuleHardness.Hard);
            if (hard is not null)
            {
                result.Add(new SlotAnnotation(slot, AnnotationStatus.HardViolation, hard));
            }
            else
            {
                result.Add(new SlotAnnotation(slot, AnnotationStatus.SoftViolation, violations[0]));
            }
        }

        return result;
    }

    private bool Matches(SlotCandidate slot, Rule rule)
    {
        var localStart = TimeZoneInfo.ConvertTime(slot.Start, _localZone);
        var localEnd = TimeZoneInfo.ConvertTime(slot.End, _localZone);

        // Wochentag prüfen (anhand des Slot-Starts in Lokalzeit)
        var dayFlag = DayFlagOf(localStart.DayOfWeek);
        if (!rule.DaysOfWeek.HasFlag(dayFlag))
        {
            return false;
        }

        // Wenn keine Zeit-Range definiert: gilt für den ganzen Tag
        if (rule.TimeRangeStart is null && rule.TimeRangeEnd is null)
        {
            return true;
        }

        // Range-Definition: Default-Start 00:00, Default-End 23:59
        var ruleStart = rule.TimeRangeStart ?? TimeOnly.MinValue;
        var ruleEnd = rule.TimeRangeEnd ?? new TimeOnly(23, 59, 59);

        var slotStartTime = TimeOnly.FromDateTime(localStart.DateTime);
        var slotEndTime = TimeOnly.FromDateTime(localEnd.DateTime);

        // Überschneidung: Slot[Start..End] vs. Rule[ruleStart..ruleEnd]
        return slotStartTime < ruleEnd && slotEndTime > ruleStart;
    }

    private static DayOfWeekFlags DayFlagOf(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday    => DayOfWeekFlags.Monday,
        DayOfWeek.Tuesday   => DayOfWeekFlags.Tuesday,
        DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday,
        DayOfWeek.Thursday  => DayOfWeekFlags.Thursday,
        DayOfWeek.Friday    => DayOfWeekFlags.Friday,
        DayOfWeek.Saturday  => DayOfWeekFlags.Saturday,
        DayOfWeek.Sunday    => DayOfWeekFlags.Sunday,
        _ => DayOfWeekFlags.None,
    };
}
```

- [ ] **Step 5: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~RuleApplicatorTests"
```

Expected: 8 Tests grün.

- [ ] **Step 6: RuleApplicator in DI registrieren**

`src/Backend/Program.cs` ergänzen (vor `var app = builder.Build();`):

```csharp
builder.Services.AddSingleton(_ =>
    new NauAssist.Backend.Features.Rules.RuleApplicator(
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
```

- [ ] **Step 7: Commit**

```bash
git add src/
git commit -m "Plan A Task 11: RuleApplicator (deterministische Slot-Annotation gegen aktive Regeln)"
```

---

## Task 12: Plan-A-Abschluss-Verifikation

**Files:** keine neuen — diese Task ist ein End-to-End-Check der gesamten Plan-A-Implementierung.

- [ ] **Step 1: Komplettes Test-Suite-Run**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle Tests grün. Erwartete Anzahl: 30 Tests (Health × 1, Mediator × 1, DbInitializer × 3, Rule × 3, RuleRepository × 4, AddRuleHandler × 3, ListRulesHandler × 2, DeleteRuleHandler × 2, RulesEndpoints × 3, RuleApplicator × 8 = 30). Abweichung von ±2 durch kleinere Anpassungen im Lauf der Implementierung ist normal.

- [ ] **Step 2: Manueller Smoke-Test des Backends**

Run:
```bash
rm -rf src/Backend/data

dotnet run --project src/Backend &
APP_PID=$!
sleep 3

echo "--- Health ---"
curl -s http://localhost:5000/health
echo ""

echo "--- POST Rule 1 ---"
curl -sX POST http://localhost:5000/api/rules \
  -H "Content-Type: application/json" \
  -d '{"text":"Mo-Fr nach 18 nicht","daysOfWeek":31,"timeRangeStart":"18:00","timeRangeEnd":"23:59","hardness":"hard"}'
echo ""

echo "--- POST Rule 2 ---"
curl -sX POST http://localhost:5000/api/rules \
  -H "Content-Type: application/json" \
  -d '{"text":"Mi 19-20 Sport","daysOfWeek":4,"timeRangeStart":"19:00","timeRangeEnd":"20:00","hardness":"hard"}'
echo ""

echo "--- GET ---"
curl -s http://localhost:5000/api/rules
echo ""

echo "--- DELETE Rule 1 ---"
curl -sX DELETE -w "%{http_code}\n" http://localhost:5000/api/rules/1
echo ""

echo "--- GET nach DELETE ---"
curl -s http://localhost:5000/api/rules
echo ""

kill $APP_PID
rm -rf src/Backend/data
```

Expected:
- `/health` → `ok`
- Beide POSTs liefern 201 mit JSON-Body
- GET listet beide Regeln
- DELETE liefert 204
- Zweiter GET zeigt nur noch eine Regel

- [ ] **Step 3: Abschluss-Commit (falls Code-Änderungen aus Smoke-Test nötig wurden, sonst skip)**

Falls keine Änderungen → kein Commit nötig. Falls doch:

```bash
git add src/
git commit -m "Plan A Task 12: Smoke-Test-Korrekturen"
```

---

## Plan-A-Abschluss

Nach Task 12 läuft:
- ✅ Solution baut mit `dotnet build src/NauAssist.slnx`
- ✅ Alle Unit- und Integration-Tests grün
- ✅ Backend serviert `/health`, `/api/rules` (POST/GET/DELETE)
- ✅ SQLite-DB wird automatisch angelegt, Migrationen laufen idempotent
- ✅ RuleApplicator annotiert Slot-Kandidaten deterministisch

**Was als Nächstes kommt (Plan B):**
- `ICalendarProvider`-Interface und Modelle (`CalendarEvent`, `NewEvent`, `Slot`)
- `FreeSlotCalculator` (pure Logik für freie Lücken im Tagesplan)
- `LookupFreeSlots`/`CreateEvent`/`GetCalendarRange`-Handler (gegen `FakeCalendarProvider`)
- `GoogleCalendarProvider` und OAuth-Flow

Der `RuleApplicator` aus Plan A wird in Plan B beim `LookupFreeSlotsHandler` eingesetzt, um Kandidaten zu filtern/annotieren.
