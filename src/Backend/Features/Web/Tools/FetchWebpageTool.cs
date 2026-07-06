using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.Web.Tools;

/// <summary>
/// Chat-Tool „fetch_webpage": liest eine Webseite als reduzierten Text (SSRF-gehärteter
/// Fetch, siehe <see cref="SsrfGuard"/>). Der Text wird zusätzlich zu MaxFetchBytes auf
/// <see cref="MaxTextChars"/> gekappt — Schutz des LLM-Kontextfensters.
/// </summary>
public sealed class FetchWebpageTool : ITool
{
    internal const int MaxTextChars = 6_000;

    public string Name => "fetch_webpage";

    public string Description =>
        "Lädt eine Webseite und liefert ihren Inhalt als Text — z. B. um einen web_search-Treffer " +
        "im Detail zu lesen. Nur absolute http(s)-URLs.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "url": { "type": "string", "description": "Absolute http(s)-URL der Seite" }
          },
          "required": ["url"]
        }
        """).RootElement;

    private readonly IWebFetch _fetch;

    public FetchWebpageTool(IWebFetch fetch)
    {
        _fetch = fetch;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        // Nur echte String-URL akzeptieren: bei { "url": 123 } o. Ä. würde GetString()
        // sonst eine InvalidOperationException Richtung Agent-Loop werfen (Kontraktbruch).
        var url = args.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(url) || !SsrfGuard.IsAllowedUrl(url, out _))
        {
            return JsonSerializer.SerializeToElement(new
            {
                ok = false,
                error = "url fehlt oder ist keine absolute http(s)-URL.",
            });
        }

        var doc = await _fetch.FetchAsync(url, etag: null, ct);
        var truncated = doc.TextContent.Length > MaxTextChars;
        var text = truncated ? doc.TextContent[..MaxTextChars] : doc.TextContent;

        // Fetch wirft designbedingt nicht (leeres Dokument bei Fehlern) — leeren Text
        // dem LLM als möglichen Fehler kennzeichnen.
        return text.Length == 0
            ? JsonSerializer.SerializeToElement(new
            {
                ok = true,
                url = doc.Url,
                status = doc.StatusCode,
                text,
                truncated,
                hint = "Seite lieferte keinen Text (Fehler, Block oder leere Seite).",
            })
            : JsonSerializer.SerializeToElement(new { ok = true, url = doc.Url, status = doc.StatusCode, text, truncated });
    }
}
