using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Chat;

public sealed class MessageRepository
{
    private readonly AppDb _db;

    public MessageRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<Message> AddAsync(Message msg, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO messages(session_id, role, content, proposals_json, incomplete, created_at)
            VALUES(@SessionId, @Role, @Content, @ProposalsJson, @Incomplete, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                msg.SessionId,
                Role = msg.Role.ToString().ToLowerInvariant(),
                msg.Content,
                msg.ProposalsJson,
                Incomplete = msg.Incomplete ? 1 : 0,
                CreatedAt = msg.CreatedAt.ToString("O"),
            });
        return msg with { Id = id };
    }

    public async Task<IReadOnlyList<Message>> GetRecentAsync(string sessionId, int take, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<MessageRow>(
            """
            SELECT id, session_id, role, content, proposals_json, incomplete, created_at
            FROM messages
            WHERE session_id = @sessionId
            ORDER BY id DESC
            LIMIT @take;
            """,
            new { sessionId, take });
        return rows.Select(MapToDomain).ToList();
    }

    public async Task MarkIncompleteAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(
            "UPDATE messages SET incomplete = 1 WHERE id = @id;",
            new { id });
    }

    private static Message MapToDomain(MessageRow r) => new(
        Id: r.id,
        SessionId: r.session_id,
        Role: Enum.Parse<MessageRole>(r.role, ignoreCase: true),
        Content: r.content,
        ProposalsJson: r.proposals_json,
        Incomplete: r.incomplete != 0,
        CreatedAt: DateTimeOffset.Parse(r.created_at));

    private sealed record MessageRow(
        long id,
        string session_id,
        string role,
        string content,
        string? proposals_json,
        long incomplete,
        string created_at);
}
