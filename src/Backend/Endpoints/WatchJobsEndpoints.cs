using System.Text.Json;
using NauAssist.Backend.Features.WatchJobs;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Read-only-Liste der laufenden Watch-Jobs des Users — Grundlage für die spätere
/// Watcher-UI-Section. Mutationen laufen in Phase 1 ausschließlich über die Chat-Tools
/// (create/list/cancel_watch_job).
/// </summary>
public static class WatchJobsEndpoints
{
    public static IEndpointRouteBuilder MapWatchJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/watch-jobs");

        group.MapGet("/", async (WatchJobRepository repo, CancellationToken ct) =>
        {
            var items = await repo.ListActiveByUserAsync(ct);
            return Results.Ok(items.Select(ToDto));
        });

        return app;
    }

    private static WatchJobDto ToDto(WatchJob j) => new(
        j.Id,
        j.Title,
        j.Goal,
        j.Kind.ToWire(),
        j.Status.ToWire(),
        j.CheckCount,
        j.LastCheckedAt,
        j.NextDueAt,
        ExtractSummary(j.LastResultJson),
        j.CreatedAt);

    private static string? ExtractSummary(string? lastResultJson)
    {
        if (string.IsNullOrWhiteSpace(lastResultJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(lastResultJson);
            return doc.RootElement.TryGetProperty("summary", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record WatchJobDto(
        long Id,
        string Title,
        string Goal,
        string Kind,
        string Status,
        int CheckCount,
        DateTimeOffset? LastCheckedAt,
        DateTimeOffset NextDueAt,
        string? LastSummary,
        DateTimeOffset CreatedAt);
}
