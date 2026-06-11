using Dapper;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Chat;

public sealed class ChatClearMarkerRepository : IChatClearMarkerSource
{
    private readonly AppDb _db;
    private readonly IUserContext _user;

    public ChatClearMarkerRepository(AppDb db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    public async Task<DateTimeOffset?> GetLatestCreatedAtSinceAsync(string sessionId, DateTimeOffset since, CancellationToken ct)
    {
        var latest = await GetLatestSinceAsync(sessionId, since, ct);
        return latest?.CreatedAt;
    }

    public async Task<ChatClearMarker> AddAsync(string sessionId, DateTimeOffset createdAt, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO chat_clear_markers(user_id, session_id, created_at)
            VALUES(@userId, @sessionId, @createdAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                userId = _user.UserId,
                sessionId,
                createdAt = createdAt.ToString("O"),
            });
        return new ChatClearMarker(id, sessionId, createdAt);
    }

    public async Task<ChatClearMarker?> GetLatestSinceAsync(string sessionId, DateTimeOffset since, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<MarkerRow>(
            """
            SELECT id, session_id, created_at
            FROM chat_clear_markers
            WHERE user_id = @userId AND session_id = @sessionId AND created_at > @since
            ORDER BY created_at DESC
            LIMIT 1;
            """,
            new { userId = _user.UserId, sessionId, since = since.ToString("O") });
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<ChatClearMarker>> GetAllSinceAsync(string sessionId, DateTimeOffset since, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<MarkerRow>(
            """
            SELECT id, session_id, created_at
            FROM chat_clear_markers
            WHERE user_id = @userId AND session_id = @sessionId AND created_at > @since
            ORDER BY created_at ASC;
            """,
            new { userId = _user.UserId, sessionId, since = since.ToString("O") });
        return rows.Select(MapToDomain).ToList();
    }

    private static ChatClearMarker MapToDomain(MarkerRow r) => new(
        Id: r.id,
        SessionId: r.session_id,
        CreatedAt: DateTimeOffset.Parse(r.created_at));

    private sealed record MarkerRow(long id, string session_id, string created_at);
}
