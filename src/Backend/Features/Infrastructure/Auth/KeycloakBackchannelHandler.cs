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
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsoluteUri.StartsWith(externalUrl, StringComparison.OrdinalIgnoreCase) == true)
        {
            var rewritten = internalUrl.TrimEnd('/') + request.RequestUri.AbsoluteUri[externalUrl.Length..];
            request.RequestUri = new Uri(rewritten);
        }

        InnerHandler ??= new HttpClientHandler();
        return base.SendAsync(request, cancellationToken);
    }
}
