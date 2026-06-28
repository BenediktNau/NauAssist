using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.WatchJobs.Tools;

/// <summary>Stoppt (cancel ⇒ completed) oder pausiert (pause ⇒ paused) einen Watch-Job des Users.</summary>
public sealed class CancelWatchJobTool : ITool
{
    public string Name => "cancel_watch_job";

    public string Description =>
        "Stoppt oder pausiert einen laufenden Watch-Job. mode='cancel' beendet ihn, mode='pause' setzt ihn aus.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "integer", "description": "ID des Watch-Jobs (siehe list_watch_jobs)" },
            "mode": { "type": "string", "enum": ["cancel", "pause"], "description": "cancel = endgültig stoppen, pause = aussetzen (wieder fortsetzbar)" }
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
        if (string.Equals(mode, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = WatchJobStatus.Completed;
        }
        else if (string.Equals(mode, "pause", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = WatchJobStatus.Paused;
        }
        else
        {
            return JsonSerializer.SerializeToElement(
                new { ok = false, error = $"mode ist erforderlich und muss 'cancel' oder 'pause' sein (war: '{mode}')." });
        }

        var ok = await _repo.SetStatusAsync(id, newStatus, firedHash: null, ct);
        if (!ok)
        {
            return JsonSerializer.SerializeToElement(new { ok = false, error = $"Kein Watch-Job mit id {id} gefunden." });
        }

        return JsonSerializer.SerializeToElement(new
        {
            ok = true,
            id,
            status = newStatus.ToString().ToLowerInvariant(),
        });
    }
}
