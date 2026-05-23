using Dapper;
using Microsoft.Data.Sqlite;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Settings;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private const string KeyProvider = "llm.provider";
    private const string KeyOllamaModel = "llm.ollama.model";
    private const string KeyGeminiModel = "llm.gemini.model";
    private const string KeyGeminiApiKey = "llm.gemini.api_key";
    private const string KeyOllamaHost = "ollama.host";
    private const string KeyOllamaApiKey = "ollama.api_key";
    private const string KeyOllamaNumCtx = "ollama.num_ctx";
    private const string KeyOllamaTemperature = "ollama.temperature";

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
        SqliteConnection conn,
        SqliteTransaction tx,
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
    public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
        throw new NotImplementedException();
    public Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
        throw new NotImplementedException();
    public Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct) =>
        throw new NotImplementedException();
}
