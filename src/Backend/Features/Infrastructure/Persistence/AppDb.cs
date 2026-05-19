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
