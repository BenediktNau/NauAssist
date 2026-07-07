using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.WatchJobs.Tools;

/// <summary>
/// Stoppt (cancel ⇒ completed), pausiert (pause ⇒ paused) oder setzt einen pausierten Watch-Job
/// des Users fort (resume ⇒ active).
/// </summary>
public sealed class CancelWatchJobTool : ITool
{
    public string Name => "cancel_watch_job";

    public string Description =>
        "Stoppt, pausiert oder setzt einen Watch-Job fort. mode='cancel' beendet ihn, mode='pause' setzt ihn aus, " +
        "mode='resume' setzt einen pausierten Job fort.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "integer", "description": "ID des Watch-Jobs (siehe list_watch_jobs)" },
            "mode": { "type": "string", "enum": ["cancel", "pause", "resume"], "description": "cancel = endgültig stoppen, pause = aussetzen (wieder fortsetzbar), resume = pausierten Job fortsetzen" }
          },
          "required": ["id", "mode"]
        }
        """).RootElement;

    private readonly WatchJobRepository _repo;

    public CancelWatchJobTool(WatchJobRepository repo)
    {
        _repo = repo;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
        {
            return JsonSerializer.SerializeToElement(new { ok = false, error = "id ist erforderlich." });
        }

        var mode = args.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String
            ? modeEl.GetString()
            : null;

        // mode ist Pflicht und muss explizit sein — kein destruktiver Default. Ein fehlendes
        // oder vertipptes mode darf einen Job nicht unwiderruflich abschließen (completed fällt
        // aus der aktiven Liste, nur Neuanlage möglich).
        WatchJobStatus newStatus;
        IReadOnlyCollection<WatchJobStatus> allowedFrom;
        if (string.Equals(mode, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = WatchJobStatus.Completed;
            allowedFrom = new[] { WatchJobStatus.Active, WatchJobStatus.Paused };
        }
        else if (string.Equals(mode, "pause", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = WatchJobStatus.Paused;
            allowedFrom = new[] { WatchJobStatus.Active };
        }
        else if (string.Equals(mode, "resume", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = WatchJobStatus.Active;
            allowedFrom = new[] { WatchJobStatus.Paused };
        }
        else
        {
            return JsonSerializer.SerializeToElement(
                new { ok = false, error = $"mode ist erforderlich und muss 'cancel', 'pause' oder 'resume' sein (war: '{mode}')." });
        }

        var ok = await _repo.SetStatusAsync(id, newStatus, firedHash: null, ct, allowedFrom);
        if (!ok)
        {
            return JsonSerializer.SerializeToElement(
                new { ok = false, error = $"Kein Watch-Job mit id {id} im passenden Zustand gefunden." });
        }

        return JsonSerializer.SerializeToElement(new
        {
            ok = true,
            id,
            status = newStatus.ToString().ToLowerInvariant(),
        });
    }
}
