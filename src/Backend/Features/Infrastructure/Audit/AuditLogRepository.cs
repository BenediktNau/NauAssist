using Dapper;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Infrastructure.Audit;

public sealed class AuditLogRepository
{
    private readonly AppDb _db;
    private readonly IUserContext _user;

    public AuditLogRepository(AppDb db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    public async Task<AuditEntry> AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO audit_log(user_id, triggering_message_id, tool_name, tool_args_json, result_json, provider_event_id, created_at)
            VALUES(@UserId, @TriggeringMessageId, @ToolName, @ToolArgsJson, @ResultJson, @ProviderEventId, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                UserId = _user.UserId,
                entry.TriggeringMessageId,
                entry.ToolName,
                entry.ToolArgsJson,
                entry.ResultJson,
                entry.ProviderEventId,
                CreatedAt = entry.CreatedAt.ToString("O"),
            });
        return entry with { Id = id };
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByMessageIdAsync(long messageId, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<AuditRow>(
            """
            SELECT id, triggering_message_id, tool_name, tool_args_json, result_json, provider_event_id, created_at
            FROM audit_log
            WHERE user_id = @userId AND triggering_message_id = @messageId
            ORDER BY id;
            """,
            new { userId = _user.UserId, messageId });
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_log WHERE user_id = @userId;",
            new { userId = _user.UserId });
    }

    private static AuditEntry MapToDomain(AuditRow r) => new(
        Id: r.id,
        TriggeringMessageId: r.triggering_message_id,
        ToolName: r.tool_name,
        ToolArgsJson: r.tool_args_json,
        ResultJson: r.result_json,
        ProviderEventId: r.provider_event_id,
        CreatedAt: DateTimeOffset.Parse(r.created_at));

    private sealed record AuditRow(
        long id,
        long? triggering_message_id,
        string tool_name,
        string tool_args_json,
        string result_json,
        string? provider_event_id,
        string created_at);
}
