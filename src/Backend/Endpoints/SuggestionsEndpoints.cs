using NauAssist.Backend.Features.AutonomousAgent;

namespace NauAssist.Backend.Endpoints;

public static class SuggestionsEndpoints
{
    public static IEndpointRouteBuilder MapSuggestionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/suggestions");

        group.MapGet("/", async (
            string? status,
            int? take,
            SuggestionRepository repo,
            CancellationToken ct) =>
        {
            SuggestionStatus? filter = null;
            if (!string.IsNullOrEmpty(status))
            {
                try
                {
                    filter = SuggestionStatusExtensions.ParseWire(status);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }

            var items = await repo.ListAsync(filter, take ?? 100, ct);
            return Results.Ok(items.Select(ToDto));
        });

        group.MapGet("/{id:long}", async (long id, SuggestionRepository repo, CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            return s is null ? Results.NotFound() : Results.Ok(ToDto(s));
        });

        group.MapPost("/{id:long}/pick", async (
            long id,
            PickPayload body,
            SuggestionRepository repo,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            if (s is null) return Results.NotFound();
            if (s.Status != SuggestionStatus.Pending)
            {
                return Results.BadRequest(new { error = "suggestion_not_pending" });
            }
            if (body.SlotIndex < 0 || body.SlotIndex >= s.Slots.Count)
            {
                return Results.BadRequest(new { error = "slot_index_out_of_range" });
            }

            var ok = await repo.PickAsync(id, body.SlotIndex, clock(), ct);
            if (!ok) return Results.Conflict(new { error = "pick_failed" });

            var updated = await repo.GetAsync(id, ct);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapPost("/{id:long}/dismiss", async (
            long id,
            SuggestionRepository repo,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            if (s is null) return Results.NotFound();
            await repo.SetStatusAsync(id, SuggestionStatus.Dismissed, clock(), ct);
            return Results.NoContent();
        });

        group.MapPost("/poll-now", async (
            AutonomousAgentScheduler scheduler,
            CancellationToken ct) =>
        {
            var result = await scheduler.RunTickAsync(TickTrigger.Manual, ct);
            return Results.Ok(result);
        });

        return app;
    }

    private static SuggestionDto ToDto(Suggestion s) => new(
        s.Id,
        s.Source,
        s.SourceRef,
        s.Intent,
        s.Topic,
        s.Requester,
        s.QuotedText,
        s.Slots.Select(slot => new SlotDto(slot.Start, slot.End, slot.Note)).ToArray(),
        s.DraftReply,
        s.Status.ToWire(),
        s.PickedSlot,
        s.CreatedAt,
        s.UpdatedAt,
        s.RespondedAt);

    private sealed record PickPayload(int SlotIndex);

    private sealed record SuggestionDto(
        long Id,
        string Source,
        string SourceRef,
        string Intent,
        string? Topic,
        string? Requester,
        string? QuotedText,
        IReadOnlyList<SlotDto> Slots,
        string DraftReply,
        string Status,
        int? PickedSlot,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? RespondedAt);

    private sealed record SlotDto(DateTimeOffset Start, DateTimeOffset End, string? Note);
}
