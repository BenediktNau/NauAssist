using System.Text.Json;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Liest die <c>summary</c> aus dem persistierten <c>last_result_json</c> eines Watch-Jobs.
/// Gemeinsam genutzt von <c>ListWatchJobsTool</c> und dem <c>GET /api/watch-jobs</c>-Endpoint,
/// damit beide Pfade konsistent bleiben.
/// </summary>
public static class WatchJobResultSummary
{
    public static string? Extract(string? lastResultJson)
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
}
