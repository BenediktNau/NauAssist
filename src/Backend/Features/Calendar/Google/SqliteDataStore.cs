using System.Text;
using System.Text.Json;
using Dapper;
using Google.Apis.Util.Store;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Calendar.Google;

/// <summary>
/// IDataStore-Implementierung für Google.Apis, persistiert in SQLite (Tabelle google_oauth).
/// Serialisiert Werte als UTF-8-JSON, Schlüssel = {typeName}::{key}.
/// </summary>
public sealed class SqliteDataStore : IDataStore
{
    private readonly AppDb _db;

    public SqliteDataStore(AppDb db)
    {
        _db = db;
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        var combinedKey = MakeKey<T>(key);
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO google_oauth(key, value, updated_at)
              VALUES(@k, @v, @ts)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;",
            new { k = combinedKey, v = bytes, ts = DateTimeOffset.UtcNow.ToString("O") });
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var combinedKey = MakeKey<T>(key);

        using var conn = _db.OpenConnection();
        var bytes = await conn.QueryFirstOrDefaultAsync<byte[]?>(
            "SELECT value FROM google_oauth WHERE key = @k;",
            new { k = combinedKey });

        if (bytes is null)
        {
            return default!;
        }

        var json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    public async Task DeleteAsync<T>(string key)
    {
        var combinedKey = MakeKey<T>(key);

        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync("DELETE FROM google_oauth WHERE key = @k;", new { k = combinedKey });
    }

    public async Task ClearAsync()
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync("DELETE FROM google_oauth;");
    }

    private static string MakeKey<T>(string key) => $"{typeof(T).FullName}::{key}";
}
