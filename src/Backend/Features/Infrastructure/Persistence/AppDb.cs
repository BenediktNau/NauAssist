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
