using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Infrastructure.Auth;

public sealed record UserRecord(
    string Id,
    string? Username,
    string? Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt);

public sealed class UserRepository
{
    private readonly AppDb _db;

    public UserRepository(AppDb db)
    {
        _db = db;
    }

    /// <summary>
    /// Provisioning: legt den User beim ersten authentifizierten Request an
    /// bzw. aktualisiert Username/E-Mail/last_seen (Keycloak ist Source-of-Truth).
    /// </summary>
    public async Task UpsertAsync(string id, string? username, string? email, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO users(id, username, email, created_at, last_seen_at)
            VALUES(@id, @username, @email, @now, @now)
            ON CONFLICT(id) DO UPDATE SET
                username = excluded.username,
                email = excluded.email,
                last_seen_at = excluded.last_seen_at;
            """,
            new { id, username, email, now = now.ToString("O") },
            cancellationToken: ct));
    }

    /// <summary>Alle bekannten User — der Scheduler iteriert darüber (ein Scope pro User).</summary>
    public async Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(
            "SELECT id, username, email, created_at, last_seen_at FROM users ORDER BY created_at;",
            cancellationToken: ct));
        return rows.Select(r => new UserRecord(
            Id: r.id,
            Username: r.username,
            Email: r.email,
            CreatedAt: DateTimeOffset.Parse(r.created_at),
            LastSeenAt: r.last_seen_at is null ? null : DateTimeOffset.Parse(r.last_seen_at))).ToList();
    }

    private sealed record UserRow(
        string id,
        string? username,
        string? email,
        string created_at,
        string? last_seen_at);
}
