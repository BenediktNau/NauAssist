using Dapper;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources;

public sealed class SourceCursorRepository
{
    private readonly AppDb _db;
    private readonly IUserContext _user;

    public SourceCursorRepository(AppDb db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    public async Task<string?> GetAsync(string source, long? accountId, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        return await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT cursor FROM source_cursors
            WHERE user_id = @userId
              AND source = @source
              AND (account_id IS @accountId OR account_id = @accountId);
            """,
            new { userId = _user.UserId, source, accountId },
            cancellationToken: ct));
    }

    public async Task SetAsync(string source, long? accountId, string cursor, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO source_cursors(user_id, source, account_id, cursor, updated_at)
            VALUES(@userId, @source, @accountId, @cursor, @now)
            ON CONFLICT(user_id, source, account_id) DO UPDATE SET
                cursor = excluded.cursor,
                updated_at = excluded.updated_at;
            """,
            new { userId = _user.UserId, source, accountId, cursor, now = now.ToString("O") },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(string source, long? accountId, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM source_cursors
            WHERE user_id = @userId
              AND source = @source
              AND (account_id IS @accountId OR account_id = @accountId);
            """,
            new { userId = _user.UserId, source, accountId },
            cancellationToken: ct));
    }
}
