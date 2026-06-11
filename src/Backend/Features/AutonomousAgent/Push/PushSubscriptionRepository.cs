using Dapper;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.AutonomousAgent.Push;

public sealed class PushSubscriptionRepository
{
    private readonly AppDb _db;
    private readonly IUserContext _user;

    public PushSubscriptionRepository(AppDb db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PushSubscription> UpsertAsync(
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO web_push_subscriptions(user_id, endpoint, p256dh, auth, user_agent, created_at)
            VALUES(@userId, @endpoint, @p256dh, @auth, @userAgent, @now)
            ON CONFLICT(endpoint) DO UPDATE SET
                user_id = excluded.user_id,
                p256dh = excluded.p256dh,
                auth = excluded.auth,
                user_agent = excluded.user_agent;
            SELECT id FROM web_push_subscriptions WHERE endpoint = @endpoint;
            """,
            new { userId = _user.UserId, endpoint, p256dh, auth, userAgent, now = now.ToString("O") },
            cancellationToken: ct));

        return new PushSubscription(
            Id: id,
            Endpoint: endpoint,
            P256dh: p256dh,
            Auth: auth,
            UserAgent: userAgent,
            CreatedAt: now,
            LastUsed: null);
    }

    public async Task<IReadOnlyList<PushSubscription>> ListAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<SubRow>(new CommandDefinition(
            "SELECT id, endpoint, p256dh, auth, user_agent, created_at, last_used FROM web_push_subscriptions WHERE user_id = @userId ORDER BY id ASC;",
            new { userId = _user.UserId },
            cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM web_push_subscriptions WHERE id = @id AND user_id = @userId;",
            new { id, userId = _user.UserId },
            cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> DeleteByEndpointAsync(string endpoint, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM web_push_subscriptions WHERE endpoint = @endpoint AND user_id = @userId;",
            new { endpoint, userId = _user.UserId },
            cancellationToken: ct));
        return affected > 0;
    }

    public async Task TouchAsync(long id, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE web_push_subscriptions SET last_used = @now WHERE id = @id;",
            new { id, now = now.ToString("O") },
            cancellationToken: ct));
    }

    private static PushSubscription MapToDomain(SubRow r) => new(
        Id: r.id,
        Endpoint: r.endpoint,
        P256dh: r.p256dh,
        Auth: r.auth,
        UserAgent: r.user_agent,
        CreatedAt: DateTimeOffset.Parse(r.created_at),
        LastUsed: string.IsNullOrEmpty(r.last_used) ? null : DateTimeOffset.Parse(r.last_used));

    private sealed record SubRow(
        long id,
        string endpoint,
        string p256dh,
        string auth,
        string? user_agent,
        string created_at,
        string? last_used);
}
