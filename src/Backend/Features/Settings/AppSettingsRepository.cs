using Dapper;
using Microsoft.Data.Sqlite;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Settings;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private const string KeyOllamaModel = "llm.ollama.model";
    private const string KeyOllamaSystemPrompt = "llm.system_prompt";
    private const string KeyOllamaHost = "ollama.host";
    private const string KeyOllamaApiKey = "ollama.api_key";
    private const string KeyOllamaNumCtx = "ollama.num_ctx";
    private const string KeyOllamaTemperature = "ollama.temperature";
    private const string KeyCalendarId      = "calendar.google.calendar_id";
    private const string KeyWorkingStart    = "calendar.working_hours_start";
    private const string KeyWorkingEnd      = "calendar.working_hours_end";
    private const string KeyDefaultDuration = "calendar.default_duration_min";
    private const string KeySearchHorizon   = "calendar.search_horizon_days";
    private const string KeyGoogleClientId     = "calendar.google.client_id";
    private const string KeyGoogleClientSecret = "calendar.google.client_secret";

    private readonly AppDb _db;

    public AppSettingsRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<LlmSettings> GetLlmAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2);",
            new { k1 = KeyOllamaModel, k2 = KeyOllamaSystemPrompt },
            cancellationToken: ct));

        var map = rows.ToDictionary(r => r.Key, r => r.Value);
        var model = map.GetValueOrDefault(KeyOllamaModel) ?? "gemma4:26b";
        var promptRaw = map.GetValueOrDefault(KeyOllamaSystemPrompt);
        var systemPrompt = string.IsNullOrEmpty(promptRaw) ? null : promptRaw;

        return new LlmSettings(model, systemPrompt);
    }

    public async Task SetLlmAsync(LlmSettings settings, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            await UpsertAsync(conn, tx, KeyOllamaModel, settings.OllamaModel, ct);
            await UpsertAsync(conn, tx, KeyOllamaSystemPrompt, settings.SystemPrompt ?? "", ct);
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
        SqliteTransaction? tx,
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
    public async Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2, @k3, @k4, @k5);",
            new
            {
                k1 = KeyCalendarId,
                k2 = KeyWorkingStart,
                k3 = KeyWorkingEnd,
                k4 = KeyDefaultDuration,
                k5 = KeySearchHorizon,
            },
            cancellationToken: ct));

        var map = rows.ToDictionary(r => r.Key, r => r.Value);

        return new CalendarUserSettings(
            CalendarId: map.GetValueOrDefault(KeyCalendarId, "primary"),
            WorkingHoursStart: TimeOnly.Parse(map.GetValueOrDefault(KeyWorkingStart, "09:00")),
            WorkingHoursEnd: TimeOnly.Parse(map.GetValueOrDefault(KeyWorkingEnd, "18:00")),
            DefaultDurationMinutes: int.Parse(map.GetValueOrDefault(KeyDefaultDuration, "60")),
            SearchHorizonDays: int.Parse(map.GetValueOrDefault(KeySearchHorizon, "14")));
    }

    public async Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            await UpsertAsync(conn, tx, KeyCalendarId, settings.CalendarId, ct);
            await UpsertAsync(conn, tx, KeyWorkingStart, settings.WorkingHoursStart.ToString("HH:mm"), ct);
            await UpsertAsync(conn, tx, KeyWorkingEnd, settings.WorkingHoursEnd.ToString("HH:mm"), ct);
            await UpsertAsync(conn, tx, KeyDefaultDuration, settings.DefaultDurationMinutes.ToString(), ct);
            await UpsertAsync(conn, tx, KeySearchHorizon, settings.SearchHorizonDays.ToString(), ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
    public async Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2);",
            new { k1 = KeyGoogleClientId, k2 = KeyGoogleClientSecret },
            cancellationToken: ct));

        var map = rows.ToDictionary(r => r.Key, r => r.Value);
        var clientId = map.GetValueOrDefault(KeyGoogleClientId, "");
        var clientSecret = map.GetValueOrDefault(KeyGoogleClientSecret, "");

        if (string.IsNullOrEmpty(clientId))
        {
            return null;
        }

        return new GoogleCredentials(clientId, clientSecret);
    }

    public async Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            await UpsertAsync(conn, tx, KeyGoogleClientId, credentials.ClientId, ct);
            await UpsertAsync(conn, tx, KeyGoogleClientSecret, credentials.ClientSecret, ct);
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM google_oauth;",
                transaction: tx,
                cancellationToken: ct));
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
