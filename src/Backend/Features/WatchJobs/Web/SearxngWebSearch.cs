using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.WatchJobs.Web;

/// <summary>
/// <see cref="IWebSearch"/> über die JSON-API einer self-hosted SearXNG-Instanz
/// (<c>{BaseUrl}/search?q=…&amp;format=json</c>). Defensiv: jeder Fehler (kein Endpoint,
/// Netzfehler, kaputtes JSON) ⇒ leere Trefferliste + Log, niemals eine Exception.
/// </summary>
public sealed class SearxngWebSearch : IWebSearch
{
    public const string HttpClientName = "WatchJobsWeb";

    private readonly IHttpClientFactory _httpFactory;
    private readonly WebOptions _options;
    private readonly ILogger<SearxngWebSearch> _logger;

    public SearxngWebSearch(IHttpClientFactory httpFactory, IOptions<WebOptions> options, ILogger<SearxngWebSearch> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.SearxngBaseUrl))
        {
            _logger.LogWarning("WatchJob-Suche übersprungen: SearxngBaseUrl ist nicht konfiguriert.");
            return Array.Empty<WebSearchHit>();
        }

        // Deployment-Hinweis: SearXNG liefert nur dann JSON, wenn `json` in `settings.yml`
        // unter `search.formats` aktiviert ist (default oft nur html/csv/rss). Ist es das nicht,
        // kommt eine HTML-Fehlerseite zurück → ParseResults liefert leer → Watcher prüfen still ins Leere.
        var url = $"{_options.SearxngBaseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json";

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.FetchTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

            using var client = _httpFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SearXNG-Suche '{Query}' lieferte HTTP {Status}.", query, (int)response.StatusCode);
                return Array.Empty<WebSearchHit>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            return ParseResults(doc, maxResults);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SearXNG-Suche '{Query}' fehlgeschlagen.", query);
            return Array.Empty<WebSearchHit>();
        }
    }

    private static IReadOnlyList<WebSearchHit> ParseResults(JsonDocument doc, int maxResults)
    {
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<WebSearchHit>();
        }

        var hits = new List<WebSearchHit>();
        foreach (var item in results.EnumerateArray())
        {
            if (hits.Count >= maxResults) break;

            var resultUrl = GetString(item, "url");
            if (string.IsNullOrWhiteSpace(resultUrl)) continue;

            hits.Add(new WebSearchHit(
                Title: GetString(item, "title") ?? resultUrl,
                Url: resultUrl,
                Snippet: GetString(item, "content") ?? ""));
        }

        return hits;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
