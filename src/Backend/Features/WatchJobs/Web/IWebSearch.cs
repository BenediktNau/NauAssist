namespace NauAssist.Backend.Features.WatchJobs.Web;

/// <summary>Ein Treffer einer Web-Suche.</summary>
public sealed record WebSearchHit(string Title, string Url, string Snippet);

/// <summary>
/// Pluggable Web-Suche (Default: self-hosted SearXNG). Das Backend hat sonst keinen
/// Web-Zugriff — Ollama lokal kann nicht browsen. Implementierungen werfen nicht,
/// sondern liefern bei Fehlern eine leere Liste (Watcher dürfen nicht am Netz scheitern).
/// </summary>
public interface IWebSearch
{
    Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct);
}
