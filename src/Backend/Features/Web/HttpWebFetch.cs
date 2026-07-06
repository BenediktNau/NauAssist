using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Web;

/// <summary>
/// <see cref="IWebFetch"/> über <see cref="IHttpClientFactory"/>: GET mit
/// Conditional-Request (<c>If-None-Match</c>), Größen- und Timeout-Cap sowie einfacher
/// HTML→Text-Reduktion für den Judge-Kontext. Defensiv: Fehler ⇒ leeres Dokument + Log.
/// </summary>
public sealed partial class HttpWebFetch : IWebFetch
{
    /// <summary>Eigener, SSRF-gehärteter Client (vgl. <see cref="SsrfGuard"/>) — getrennt vom SearXNG-Client.</summary>
    public const string HttpClientName = "WebFetch";

    private readonly IHttpClientFactory _httpFactory;
    private readonly WebOptions _options;
    private readonly ILogger<HttpWebFetch> _logger;

    public HttpWebFetch(IHttpClientFactory httpFactory, IOptions<WebOptions> options, ILogger<HttpWebFetch> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WebDocument> FetchAsync(string url, string? etag, CancellationToken ct)
    {
        // Erste Verteidigungslinie: nur absolute http(s)-URLs. Der eigentliche IP-Block
        // (intern/privat/Redirect-Hops) sitzt im ConnectCallback des Fetch-HttpClients.
        if (!SsrfGuard.IsAllowedUrl(url, out _))
        {
            _logger.LogWarning("Fetch von {Url} abgelehnt: nur absolute http(s)-URLs erlaubt.", url);
            return new WebDocument(url, 0, etag, "", NotModified: false);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.FetchTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            if (!string.IsNullOrEmpty(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var client = _httpFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            var responseEtag = response.Headers.ETag?.Tag ?? etag;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new WebDocument(url, 304, responseEtag, "", NotModified: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Fetch von {Url} lieferte HTTP {Status}.", url, (int)response.StatusCode);
                return new WebDocument(url, (int)response.StatusCode, responseEtag, "", NotModified: false);
            }

            var raw = await ReadCappedAsync(response, timeout.Token);
            var text = HtmlToText(raw);
            return new WebDocument(url, (int)response.StatusCode, responseEtag, text, NotModified: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fetch von {Url} fehlgeschlagen.", url);
            return new WebDocument(url, 0, etag, "", NotModified: false);
        }
    }

    private async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length < _options.MaxFetchBytes
               && (read = await stream.ReadAsync(
                       chunk.AsMemory(0, (int)Math.Min(chunk.Length, _options.MaxFetchBytes - buffer.Length)),
                       ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Grobe HTML→Text-Reduktion: script/style raus, Tags strippen, Entities decoden, Whitespace normalisieren.</summary>
    internal static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var withoutScripts = ScriptStyleRegex().Replace(html, " ");
        var withoutTags = TagRegex().Replace(withoutScripts, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
