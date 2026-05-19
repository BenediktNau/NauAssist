using Dapper;
using FluentAssertions;
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
}
