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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // OS-Aufräumung übernimmt im Notfall
        }
    }
}
