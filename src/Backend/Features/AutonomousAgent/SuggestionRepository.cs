using System.Text.Json;
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.AutonomousAgent;

public sealed class SuggestionRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDb _db;
    private readonly IUserContext _user;

    public SuggestionRepository(AppDb db, IUserContext user)
    {
        _db = db;
        _user = user;
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
        IReadOnlyDictionary<string, string>? replyMetadata,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var slotsJson = JsonSerializer.Serialize(slots, JsonOpts);
        var metaJson = replyMetadata is null ? null : JsonSerializer.Serialize(replyMetadata, JsonOpts);
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO suggestions(
                user_id, source, source_ref, intent, topic, requester, quoted_text,
                slots_json, draft_reply, reply_metadata_json, status, created_at, updated_at)
            VALUES(
                @userId, @source, @sourceRef, @intent, @topic, @requester, @quotedText,
                @slotsJson, @draftReply, @metaJson, 'pending', @now, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                userId = _user.UserId,
                source,
                sourceRef,
                intent,
                topic,
                requester,
                quotedText,
                slotsJson,
                draftReply,
                metaJson,
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
            RespondedAt: null,
            ReplyMetadata: replyMetadata);
    }

    public async Task<Suggestion?> GetAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<SuggestionRow>(new CommandDefinition(
            "SELECT id, source, source_ref, intent, topic, requester, quoted_text, slots_json, draft_reply, reply_metadata_json, status, picked_slot, created_at, updated_at, responded_at FROM suggestions WHERE id = @id AND user_id = @userId;",
            new { id, userId = _user.UserId },
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
                "SELECT id, source, source_ref, intent, topic, requester, quoted_text, slots_json, draft_reply, reply_metadata_json, status, picked_slot, created_at, updated_at, responded_at FROM suggestions WHERE user_id = @userId ORDER BY id DESC LIMIT @take;",
                new { userId = _user.UserId, take },
                cancellationToken: ct))
            : await conn.QueryAsync<SuggestionRow>(new CommandDefinition(
                "SELECT id, source, source_ref, intent, topic, requester, quoted_text, slots_json, draft_reply, reply_metadata_json, status, picked_slot, created_at, updated_at, responded_at FROM suggestions WHERE user_id = @userId AND status = @status ORDER BY id DESC LIMIT @take;",
                new { userId = _user.UserId, status = statusFilter.Value.ToWire(), take },
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
            SELECT id, source, source_ref, intent, topic, requester, quoted_text, slots_json, draft_reply, reply_metadata_json, status, picked_slot, created_at, updated_at, responded_at FROM suggestions
            WHERE user_id = @userId
              AND source = @source
              AND source_ref = @sourceRef
              AND status = 'pending'
              AND created_at >= @notOlderThan
            ORDER BY id DESC
            LIMIT 1;
            """,
            new { userId = _user.UserId, source, sourceRef, notOlderThan = notOlderThan.ToString("O") },
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
            WHERE id = @id AND user_id = @userId AND status = 'pending';
            """,
            new { id, userId = _user.UserId, slotIndex, now = now.ToString("O") },
            cancellationToken: ct));
        return affected > 0;
    }

    /// <summary>
    /// Aktualisiert eine bestehende <c>pending</c>-Suggestion mit neuem Topic/Requester/QuotedText/Slots/Draft.
    /// Wird vom Reasoner genutzt, wenn innerhalb 24 h im selben Thread weitergeschrieben wurde.
    /// </summary>
    public async Task<bool> UpdateContentAsync(
        long id,
        string? topic,
        string? requester,
        string? quotedText,
        IReadOnlyList<SuggestionSlot> slots,
        string draftReply,
        IReadOnlyDictionary<string, string>? replyMetadata,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var slotsJson = JsonSerializer.Serialize(slots, JsonOpts);
        var metaJson = replyMetadata is null ? null : JsonSerializer.Serialize(replyMetadata, JsonOpts);
        using var conn = _db.OpenConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE suggestions
            SET topic = @topic,
                requester = @requester,
                quoted_text = @quotedText,
                slots_json = @slotsJson,
                draft_reply = @draftReply,
                reply_metadata_json = COALESCE(@metaJson, reply_metadata_json),
                picked_slot = NULL,
                updated_at = @now
            WHERE id = @id AND user_id = @userId AND status = 'pending';
            """,
            new { id, userId = _user.UserId, topic, requester, quotedText, slotsJson, draftReply, metaJson, now = now.ToString("O") },
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
            WHERE id = @id AND user_id = @userId AND status = 'pending';
            """,
            new { id, userId = _user.UserId, draftReply, now = now.ToString("O") },
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
            WHERE id = @id AND user_id = @userId;
            """,
            new
            {
                id,
                userId = _user.UserId,
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
            WHERE user_id = @userId AND status = 'pending' AND created_at < @cutoff;
            """,
            new { userId = _user.UserId, cutoff = cutoff.ToString("O"), now = now.ToString("O") },
            cancellationToken: ct));
    }

    private static Suggestion MapToDomain(SuggestionRow r)
    {
        var slots = string.IsNullOrEmpty(r.slots_json)
            ? Array.Empty<SuggestionSlot>()
            : JsonSerializer.Deserialize<SuggestionSlot[]>(r.slots_json, JsonOpts)
              ?? Array.Empty<SuggestionSlot>();

        IReadOnlyDictionary<string, string>? meta = null;
        if (!string.IsNullOrEmpty(r.reply_metadata_json))
        {
            meta = JsonSerializer.Deserialize<Dictionary<string, string>>(r.reply_metadata_json, JsonOpts);
        }

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
                : DateTimeOffset.Parse(r.responded_at),
            ReplyMetadata: meta);
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
        string? reply_metadata_json,
        string status,
        long? picked_slot,
        string created_at,
        string updated_at,
        string? responded_at);
}
