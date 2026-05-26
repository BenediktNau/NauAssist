using System.Text.Json;
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources;

public sealed class SourceAccountRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    private readonly AppDb _db;

    public SourceAccountRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<SourceAccount> AddAsync(
        string kind,
        string displayName,
        string credentialsJson,
        IReadOnlyList<string> allowlist,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO source_accounts(kind, display_name, credentials_json, allowlist_json, enabled, created_at, updated_at)
            VALUES(@kind, @displayName, @credentialsJson, @allowlistJson, 1, @now, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                kind,
                displayName,
                credentialsJson,
                allowlistJson = JsonSerializer.Serialize(allowlist, JsonOpts),
                now = now.ToString("O"),
            },
            cancellationToken: ct));

        return new SourceAccount(
            Id: id,
            Kind: kind,
            DisplayName: displayName,
            CredentialsJson: credentialsJson,
            Allowlist: allowlist,
            Enabled: true,
            CreatedAt: now,
            UpdatedAt: now);
    }

    public async Task<SourceAccount?> GetAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            "SELECT * FROM source_accounts WHERE id = @id;",
            new { id },
            cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<SourceAccount>> ListAsync(string? kind, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = kind is null
            ? await conn.QueryAsync<AccountRow>(new CommandDefinition(
                "SELECT * FROM source_accounts ORDER BY id ASC;",
                cancellationToken: ct))
            : await conn.QueryAsync<AccountRow>(new CommandDefinition(
                "SELECT * FROM source_accounts WHERE kind = @kind ORDER BY id ASC;",
                new { kind },
                cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<SourceAccount>> ListEnabledAsync(string kind, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<AccountRow>(new CommandDefinition(
            "SELECT * FROM source_accounts WHERE kind = @kind AND enabled = 1 ORDER BY id ASC;",
            new { kind },
            cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<bool> UpdateAsync(
        long id,
        string? displayName,
        string? credentialsJson,
        IReadOnlyList<string>? allowlist,
        bool? enabled,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE source_accounts
            SET display_name      = COALESCE(@displayName, display_name),
                credentials_json  = COALESCE(@credentialsJson, credentials_json),
                allowlist_json    = COALESCE(@allowlistJson, allowlist_json),
                enabled           = COALESCE(@enabled, enabled),
                updated_at        = @now
            WHERE id = @id;
            """,
            new
            {
                id,
                displayName,
                credentialsJson,
                allowlistJson = allowlist is null ? null : JsonSerializer.Serialize(allowlist, JsonOpts),
                enabled = enabled.HasValue ? (enabled.Value ? 1 : 0) : (int?)null,
                now = now.ToString("O"),
            },
            cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM source_accounts WHERE id = @id;",
            new { id },
            cancellationToken: ct));
        return affected > 0;
    }

    private static SourceAccount MapToDomain(AccountRow r)
    {
        var allowlist = string.IsNullOrEmpty(r.allowlist_json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(r.allowlist_json, JsonOpts) ?? Array.Empty<string>();

        return new SourceAccount(
            Id: r.id,
            Kind: r.kind,
            DisplayName: r.display_name,
            CredentialsJson: r.credentials_json,
            Allowlist: allowlist,
            Enabled: r.enabled != 0,
            CreatedAt: DateTimeOffset.Parse(r.created_at),
            UpdatedAt: DateTimeOffset.Parse(r.updated_at));
    }

    private sealed record AccountRow(
        long id,
        string kind,
        string display_name,
        string credentials_json,
        string allowlist_json,
        long enabled,
        string created_at,
        string updated_at);
}
