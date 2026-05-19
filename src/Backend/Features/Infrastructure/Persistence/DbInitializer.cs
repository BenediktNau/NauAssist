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

        // schema_version-Tabelle ist Teil von Migration 0001 — Sonderbehandlung beim ersten Lauf:
        // existiert die Tabelle nicht, ist es ein Fresh-DB-Lauf, alle Migrationen sind ungelaufen.
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
