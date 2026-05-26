using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources;

public sealed class SourceCursorRepository
{
    private readonly AppDb _db;

    public SourceCursorRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(string source, long? accountId, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        return await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT cursor FROM source_cursors
            WHERE source = @source
              AND (account_id IS @accountId OR account_id = @accountId);
            """,
            new { source, accountId },
            cancellationToken: ct));
    }

    public async Task SetAsync(string source, long? accountId, string cursor, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO source_cursors(source, account_id, cursor, updated_at)
            VALUES(@source, @accountId, @cursor, @now)
            ON CONFLICT(source, account_id) DO UPDATE SET
                cursor = excluded.cursor,
                updated_at = excluded.updated_at;
            """,
            new { source, accountId, cursor, now = now.ToString("O") },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(string source, long? accountId, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM source_cursors
            WHERE source = @source
              AND (account_id IS @accountId OR account_id = @accountId);
            """,
            new { source, accountId },
            cancellationToken: ct));
    }
}
