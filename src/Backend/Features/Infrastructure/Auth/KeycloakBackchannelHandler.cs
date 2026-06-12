namespace NauAssist.Backend.Features.Infrastructure.Auth;

/// <summary>
/// Schreibt OIDC-Backchannel-Requests (Metadata, Token-Exchange, UserInfo) von der
/// externen HTTPS-Keycloak-URL auf die interne HTTP-URL um — vermeidet TLS-Hairpin
/// durch den Reverse-Proxy (Container → öffentliche Domain → Proxy → zurück).
/// Browser-Redirects nutzen weiterhin die externe Authority.
/// (Übernommen aus dem Abrechner, dort produktiv hinter Coolify/Traefik.)
/// </summary>
public sealed class KeycloakBackchannelHandler(string externalUrl, string internalUrl) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsoluteUri.StartsWith(externalUrl, StringComparison.OrdinalIgnoreCase) == true)
        {
            var rewritten = internalUrl.TrimEnd('/') + request.RequestUri.AbsoluteUri[externalUrl.Length..];
            request.RequestUri = new Uri(rewritten);
        }

        InnerHandler ??= new HttpClientHandler();
        var response = await base.SendAsync(request, cancellationToken);

        // Interne URLs im Response-Body ersetzen (z.B. OIDC-Discovery-Dokument),
        // damit authorization_endpoint im Browser erreichbar ist.
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Contains(internalUrl, StringComparison.OrdinalIgnoreCase))
            {
                response.Content = new StringContent(
                    body.Replace(internalUrl, externalUrl, StringComparison.OrdinalIgnoreCase),
                    System.Text.Encoding.UTF8,
                    mediaType);
            }
        }

        return response;
    }
}
