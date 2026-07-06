using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.Web.Tools;

/// <summary>
/// Chat-Tool „web_search": Web-Suche über die konfigurierte SearXNG-Instanz.
/// Wird nur registriert, wenn <c>Web:SearxngBaseUrl</c> gesetzt ist (Program.cs).
/// </summary>
public sealed class WebSearchTool : ITool
{
    private const int DefaultResults = 5;
    private const int MaxResults = 8;

    public string Name => "web_search";

    public string Description =>
        "Sucht im Web nach aktuellen Informationen (News, Preise, Öffnungszeiten, Verfügbarkeiten) " +
        "und liefert Treffer mit Titel, URL und Snippet.";

    // JsonDocument bewusst nicht disposed — das JsonElement hält den Parent am Leben.
    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Suchanfrage" },
            "max_results": { "type": "integer", "description": "Maximale Trefferzahl (1-8, Default 5)" }
          },
          "required": ["query"]
        }
        """).RootElement;

    private readonly IWebSearch _search;

    public WebSearchTool(IWebSearch search)
    {
        _search = search;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        // Nur echte String-Query akzeptieren: bei { "query": 123 } o. Ä. würde GetString()
        // sonst eine InvalidOperationException Richtung Agent-Loop werfen (Kontraktbruch).
        var query = args.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.SerializeToElement(new { ok = false, error = "query fehlt oder ist leer." });
        }

        var maxResults = DefaultResults;
        // TryGetInt32 statt GetInt32: bei { "max_results": 3.7 } beim Default bleiben,
        // statt eine FormatException zu werfen.
        if (args.TryGetProperty("max_results", out var max)
            && max.ValueKind == JsonValueKind.Number
            && max.TryGetInt32(out var n))
        {
            maxResults = Math.Clamp(n, 1, MaxResults);
        }

        var hits = await _search.SearchAsync(query, maxResults, ct);
        var results = hits.Select(h => new { title = h.Title, url = h.Url, snippet = h.Snippet }).ToList();

        // Die Suche wirft designbedingt nicht (leere Liste bei Fehlern) — dem LLM ehrlich
        // signalisieren, dass es „keine Treffer" von „Suche kaputt/unkonfiguriert" nicht
        // unterscheiden kann.
        return hits.Count == 0
            ? JsonSerializer.SerializeToElement(new
            {
                ok = true,
                results,
                hint = "Keine Treffer (oder Suche nicht erreichbar). Sag dem User ehrlich, dass du nichts gefunden hast.",
            })
            : JsonSerializer.SerializeToElement(new { ok = true, results });
    }
}
