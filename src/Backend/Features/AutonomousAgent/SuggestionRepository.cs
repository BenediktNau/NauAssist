using System.Text.Json;
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.AutonomousAgent;

public sealed class SuggestionRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDb _db;

    public SuggestionRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<Suggestion> InsertAsync(
        string source,
        string sourceRef,
        string intent,
        string? topic,
        string? requester,
        string? quotedText,
        IReadOnlyList<SuggestionSlot> slots,
        string draftReply,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var slotsJson = JsonSerializer.Serialize(slots, JsonOpts);
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO suggestions(
                source, source_ref, intent, topic, requester, quoted_text,
                slots_json, draft_reply, status, created_at, updated_at)
            VALUES(
                @source, @sourceRef, @intent, @topic, @requester, @quotedText,
                @slotsJson, @draftReply, 'pending', @now, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                source,
                sourceRef,
                intent,
                topic,
                requester,
                quotedText,
                slotsJson,
                draftReply,
                now = now.ToString("O"),
            },
            cancellationToken: ct));

        return new Suggestion(
            Id: id,
            Source: source,
            SourceRef: sourceRef,
            Intent: intent,
            Topic: topic,
            Requester: requester,
            QuotedText: quotedText,
            Slots: slots,
            DraftReply: draftReply,
            Status: SuggestionStatus.Pending,
            PickedSlot: null,
            CreatedAt: now,
            UpdatedAt: now,
            RespondedAt: null);
    }

    public async Task<Suggestion?> GetAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<SuggestionRow>(new CommandDefinition(
            "SELECT * FROM suggestions WHERE id = @id;",
            new { id },
            cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<Suggestion>> ListAsync(
        SuggestionStatus? statusFilter,
        int take,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = statusFilter is null
            ? await conn.QueryAsync<SuggestionRow>(new CommandDefinition(
                "SELECT * FROM suggestions ORDER BY id DESC LIMIT @take;",
                new { take },
                cancellationToken: ct))
            : await conn.QueryAsync<SuggestionRow>(new CommandDefinition(
                "SELECT * FROM suggestions WHERE status = @status ORDER BY id DESC LIMIT @take;",
                new { status = statusFilter.Value.ToWire(), take },
                cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<Suggestion?> FindOpenByThreadAsync(
        string source,
        string sourceRef,
        DateTimeOffset notOlderThan,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<SuggestionRow>(new CommandDefinition(
            """
            SELECT * FROM suggestions
            WHERE source = @source
              AND source_ref = @sourceRef
              AND status = 'pending'
              AND created_at >= @notOlderThan
            ORDER BY id DESC
            LIMIT 1;
            """,
            new { source, sourceRef, notOlderThan = notOlderThan.ToString("O") },
            cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<bool> PickAsync(long id, int slotIndex, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE suggestions
            SET picked_slot = @slotIndex, updated_at = @now
            WHERE id = @id AND status = 'pending';
            """,
            new { id, slotIndex, now = now.ToString("O") },
            cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> UpdateDraftAsync(long id, string draftReply, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE suggestions
            SET draft_reply = @draftReply, updated_at = @now
            WHERE id = @id AND status = 'pending';
            """,
            new { id, draftReply, now = now.ToString("O") },
            cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> SetStatusAsync(
        long id,
        SuggestionStatus newStatus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var respondedAt = newStatus == SuggestionStatus.Responded ? now.ToString("O") : (string?)null;
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE suggestions
            SET status = @status,
                responded_at = COALESCE(@respondedAt, responded_at),
                updated_at = @now
            WHERE id = @id;
            """,
            new
            {
                id,
                status = newStatus.ToWire(),
                respondedAt,
                now = now.ToString("O"),
            },
            cancellationToken: ct));
        return affected > 0;
    }

    /// <summary>Setzt alle <c>pending</c>-Einträge, die älter als <paramref name="cutoff"/> sind, auf <c>dismissed</c>.</summary>
    public async Task<int> ExpirePendingAsync(DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE suggestions
            SET status = 'dismissed', updated_at = @now
            WHERE status = 'pending' AND created_at < @cutoff;
            """,
            new { cutoff = cutoff.ToString("O"), now = now.ToString("O") },
            cancellationToken: ct));
    }

    private static Suggestion MapToDomain(SuggestionRow r)
    {
        var slots = string.IsNullOrEmpty(r.slots_json)
            ? Array.Empty<SuggestionSlot>()
            : JsonSerializer.Deserialize<SuggestionSlot[]>(r.slots_json, JsonOpts)
              ?? Array.Empty<SuggestionSlot>();

        return new Suggestion(
            Id: r.id,
            Source: r.source,
            SourceRef: r.source_ref,
            Intent: r.intent,
            Topic: r.topic,
            Requester: r.requester,
            QuotedText: r.quoted_text,
            Slots: slots,
            DraftReply: r.draft_reply,
            Status: SuggestionStatusExtensions.ParseWire(r.status),
            PickedSlot: r.picked_slot.HasValue ? (int)r.picked_slot.Value : null,
            CreatedAt: DateTimeOffset.Parse(r.created_at),
            UpdatedAt: DateTimeOffset.Parse(r.updated_at),
            RespondedAt: string.IsNullOrEmpty(r.responded_at)
                ? null
                : DateTimeOffset.Parse(r.responded_at));
    }

    private sealed record SuggestionRow(
        long id,
        string source,
        string source_ref,
        string intent,
        string? topic,
        string? requester,
        string? quoted_text,
        string slots_json,
        string draft_reply,
        string status,
        long? picked_slot,
        string created_at,
        string updated_at,
        string? responded_at);
}
