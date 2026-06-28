using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.WatchJobs.Tools;

/// <summary>Listet die laufenden (aktiven/pausierten) Watch-Jobs des Users für „Was beobachtest du gerade?".</summary>
public sealed class ListWatchJobsTool : ITool
{
    public string Name => "list_watch_jobs";

    public string Description =>
        "Listet die aktuell laufenden Hintergrund-Beobachter (Watch-Jobs) des Users mit Status und letztem Befund.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    private readonly WatchJobRepository _repo;

    public ListWatchJobsTool(WatchJobRepository repo)
    {
        _repo = repo;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var jobs = await _repo.ListActiveByUserAsync(ct);
        var result = new
        {
            jobs = jobs.Select(j => new
            {
                id = j.Id,
                title = j.Title,
                status = j.Status.ToString().ToLowerInvariant(),
                check_count = j.CheckCount,
                last_summary = WatchJobResultSummary.Extract(j.LastResultJson),
                next_due_at = j.NextDueAt.ToString("O"),
            }),
        };
        return JsonSerializer.SerializeToElement(result);
    }
}
