namespace NauAssist.Backend.Features.WatchJobs.Web;

/// <summary>
/// Bindet <c>AutonomousAgent:WatchJobs:Web</c>. Steuert den Web-Zugriff der Watcher:
/// SearXNG-Endpoint sowie Fetch-Limits. Default-SearXNG ist leer ⇒ Suche liefert
/// (geloggt) leere Treffer, bis eine Instanz konfiguriert ist.
/// </summary>
public sealed class WebOptions
{
    /// <summary>Basis-URL der SearXNG-Instanz, z.B. <c>http://searxng:8080</c> (ohne <c>/search</c>).</summary>
    public string SearxngBaseUrl { get; set; } = "";

    /// <summary>Obergrenze der Antwortgröße pro Fetch (Schutz gegen riesige Seiten).</summary>
    public int MaxFetchBytes { get; set; } = 2_000_000;

    /// <summary>Timeout pro Such-/Fetch-Aufruf.</summary>
    public int FetchTimeoutSeconds { get; set; } = 15;

    /// <summary>Identifizierbarer User-Agent (Fairness gegenüber Shops, robots-freundlich).</summary>
    public string UserAgent { get; set; } = "NauAssist-WatchJob/1.0 (+https://github.com/BenediktNau/NauAssist)";
}
