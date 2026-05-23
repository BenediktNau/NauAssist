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
}
