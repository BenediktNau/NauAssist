using NauAssist.Backend.Features.WatchJobs;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Watch-Jobs für die Watcher-UI: Gesamtliste (alle Status) plus Statuswechsel
/// pause/resume/cancel. Das Anlegen läuft weiterhin über das Chat-Tool create_watch_job.
/// </summary>
public static class WatchJobsEndpoints
{
    public static IEndpointRouteBuilder MapWatchJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/watch-jobs");

        group.MapGet("/", async (WatchJobRepository repo, CancellationToken ct) =>
        {
            // Alle Status — die UI zeigt auch erledigte/pausierte Watcher.
            var items = await repo.ListByUserAsync(100, ct);
            return Results.Ok(items.Select(ToDto));
        });

        group.MapPost("/{id:long}/pause", (long id, WatchJobRepository repo, CancellationToken ct)
            => SetStatusAsync(repo, id, WatchJobStatus.Paused, ct));
        group.MapPost("/{id:long}/resume", (long id, WatchJobRepository repo, CancellationToken ct)
            => SetStatusAsync(repo, id, WatchJobStatus.Active, ct));
        group.MapPost("/{id:long}/cancel", (long id, WatchJobRepository repo, CancellationToken ct)
            => SetStatusAsync(repo, id, WatchJobStatus.Completed, ct));

        return app;
    }

    private static async Task<IResult> SetStatusAsync(
        WatchJobRepository repo, long id, WatchJobStatus status, CancellationToken ct)
    {
        var ok = await repo.SetStatusAsync(id, status, firedHash: null, ct);
        return ok ? Results.NoContent() : Results.NotFound();
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
