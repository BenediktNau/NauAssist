using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.AutonomousAgent;
using NauAssist.Backend.Features.AutonomousAgent.Classification;
using NauAssist.Backend.Features.AutonomousAgent.Sources;

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
            DraftReplyGenerator draftGen,
            Func<DateTimeOffset> clock,
            ILogger<DraftReplyGenerator> draftLog,
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

            var now = clock();
            var ok = await repo.PickAsync(id, body.SlotIndex, now, ct);
            if (!ok) return Results.Conflict(new { error = "pick_failed" });

            // On-Demand: Draft anhand des gewählten Slots verfeinern.
            try
            {
                var refined = await draftGen.RefineAsync(
                    quotedText: s.QuotedText,
                    topic: s.Topic,
                    requester: s.Requester,
                    pickedSlot: s.Slots[body.SlotIndex],
                    locale: "de-DE",
                    ct);
                if (!string.IsNullOrWhiteSpace(refined))
                {
                    await repo.UpdateDraftAsync(id, refined, now, ct);
                }
            }
            catch (Exception ex)
            {
                draftLog.LogWarning(ex, "Draft-Verfeinerung nach Pick fehlgeschlagen für Suggestion {Id}.", id);
            }

            var updated = await repo.GetAsync(id, ct);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapPatch("/{id:long}/draft", async (
            long id,
            DraftPayload body,
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

            await repo.UpdateDraftAsync(id, body.Text ?? string.Empty, clock(), ct);
            var updated = await repo.GetAsync(id, ct);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapPost("/{id:long}/send", async (
            long id,
            SendPayload body,
            SuggestionRepository repo,
            SourceAccountRepository accounts,
            IEnumerable<ISourceSender> senders,
            Func<DateTimeOffset> clock,
            ILogger<SuggestionsEndpointsTag> log,
            CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            if (s is null) return Results.NotFound();
            if (s.Status != SuggestionStatus.Pending)
            {
                return Results.BadRequest(new { error = "suggestion_not_pending" });
            }

            var text = (body.Text ?? s.DraftReply ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return Results.BadRequest(new { error = "empty_draft" });
            }

            var sender = senders.FirstOrDefault(x => x.Source == s.Source);
            if (sender is null)
            {
                return Results.BadRequest(new { error = "no_sender_for_source", source = s.Source });
            }

            var candidates = await accounts.ListEnabledAsync(s.Source, ct);
            var account = candidates.FirstOrDefault(a => a.Allowlist.Contains(s.SourceRef));
            if (account is null)
            {
                return Results.BadRequest(new { error = "no_matching_account", source = s.Source });
            }

            try
            {
                await sender.SendAsync(account, s.SourceRef, text, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Direkt-Send für Suggestion {Id} fehlgeschlagen.", id);
                return Results.BadRequest(new { error = "send_failed", detail = ex.Message });
            }

            var now = clock();
            // Speichere finalen Draft-Text + setze auf 'responded'.
            await repo.UpdateDraftAsync(id, text, now, ct);
            await repo.SetStatusAsync(id, SuggestionStatus.Responded, now, ct);

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
    private sealed record DraftPayload(string? Text);
    private sealed record SendPayload(string? Text);

    /// <summary>Logger-Kategorie für Send-Endpoint — eigener Typ, damit der Logger-Name lesbar bleibt.</summary>
    private sealed class SuggestionsEndpointsTag;

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
