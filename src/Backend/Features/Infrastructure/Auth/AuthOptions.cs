namespace NauAssist.Backend.Features.Infrastructure.Auth;

/// <summary>
/// Optionale Keycloak-Auth (BFF-Pattern). <see cref="Enabled"/> = false →
/// heutiges Single-User-Verhalten, kein Login, kein Keycloak nötig.
/// </summary>
public sealed class AuthOptions
{
    public bool Enabled { get; set; }

    /// <summary>Öffentliche Keycloak-Basis-URL ohne /realms/-Suffix (z.B. https://auth.example.com).</summary>
    public string Authority { get; set; } = "";

    public string Realm { get; set; } = "nauassist";

    public string ClientId { get; set; } = "nauassist-web";

    /// <summary>Confidential-Client-Secret — bleibt ausschließlich im Backend.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Optional: interne Keycloak-URL (z.B. http://keycloak:8080) für den
    /// OIDC-Backchannel übers Compose-Netz — vermeidet TLS-Hairpin durch den Proxy.
    /// </summary>
    public string? InternalUrl { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public void Validate()
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(Authority))
            throw new InvalidOperationException("Auth__Authority fehlt (Keycloak-Basis-URL ohne /realms/...).");
        if (string.IsNullOrWhiteSpace(Realm))
            throw new InvalidOperationException("Auth__Realm fehlt.");
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("Auth__ClientId fehlt.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("Auth__ClientSecret fehlt (confidential Client, BFF).");
    }

    public string IssuerUrl => $"{Authority.TrimEnd('/')}/realms/{Realm}";
}
