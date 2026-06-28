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
        WatchJobResultSummary.Extract(j.LastResultJson),
        j.CreatedAt);

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
