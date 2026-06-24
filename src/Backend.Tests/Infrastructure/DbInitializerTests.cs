using Dapper;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Infrastructure.Persistence;
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
        var initializer = new DbInitializer(db.AppDb, NullLogger<DbInitializer>.Instance);
        initializer.Initialize();

        using var conn = db.AppDb.OpenConnection();
        var count = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM schema_version WHERE version='0001';");
        count.Should().Be(1);
    }

    [Fact]
    public void Initialize_AppliesMigration0003_CreatesMessagesAndAuditLog()
    {
        using var db = new TempSqliteDb();
        using var conn = db.AppDb.OpenConnection();

        var versions = conn.Query<string>("SELECT version FROM schema_version;").ToList();
        versions.Should().Contain("0003");

        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('messages','audit_log');")
            .ToList();
        tables.Should().Contain("messages").And.Contain("audit_log");
    }

    [Fact]
    public void Initialize_CreatesAppSettingsTable_WithSeedValues()
    {
        using var db = new TempSqliteDb();

        using var conn = db.AppDb.OpenConnection();
        var rows = conn.Query<(string Key, string Value)>(
            "SELECT key, value FROM app_settings ORDER BY key;").ToList();

        rows.Should().Contain(r => r.Key == "llm.ollama.model" && r.Value == "gemma4:26b");
        rows.Should().NotContain(r => r.Key == "llm.provider");
        rows.Should().NotContain(r => r.Key == "llm.gemini.model");
        rows.Should().NotContain(r => r.Key == "llm.gemini.api_key");
    }

    [Fact]
    public void Initialize_OnLinux_SetsDbPermissionsToOwnerOnly()
    {
        if (!OperatingSystem.IsLinux()) return; // Test ist Linux-spezifisch

        using var db = new TempSqliteDb();

        var mode = File.GetUnixFileMode(db.Path);
        var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        mode.Should().Be(ownerOnly);
    }
}
