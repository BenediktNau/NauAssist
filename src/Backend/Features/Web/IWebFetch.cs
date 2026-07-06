namespace NauAssist.Backend.Features.Web;

/// <summary>
/// Ergebnis eines HTTP-Fetch. <see cref="TextContent"/> ist bereits auf reinen Text
/// reduziert (Tags/Scripts entfernt) und größenbegrenzt — Kontext für das LLM (Chat-Tools, Watcher-Judge).
/// Bei <see cref="NotModified"/> (HTTP 304) ist <see cref="TextContent"/> leer.
/// </summary>
public sealed record WebDocument(string Url, int StatusCode, string? Etag, string TextContent, bool NotModified);

/// <summary>
/// Pluggable HTTP-Fetch mit Conditional-GET (ETag), Größen- und Timeout-Limit sowie
/// HTML→Text-Reduktion. Wirft nicht; Fehler werden als leeres Dokument signalisiert.
/// </summary>
public interface IWebFetch
{
    Task<WebDocument> FetchAsync(string url, string? etag, CancellationToken ct);
}
