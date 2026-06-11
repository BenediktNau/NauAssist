using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;
using NauAssist.Backend.Features.Infrastructure.Auth;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Meldet dem Frontend, welche optionalen Features aktiv sind, damit die UI
/// entsprechende Bereiche ein- oder ausblenden kann (z.B. WhatsApp-Section).
/// Der auth-Block ist alles, was das Frontend über OIDC wissen muss (BFF) —
/// bleibt deshalb auch mit aktivierter Auth anonym erreichbar.
/// </summary>
public static class CapabilitiesEndpoints
{
    public static IEndpointRouteBuilder MapCapabilitiesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/capabilities", (IOptions<WhatsAppOptions> whatsApp, IOptions<AuthOptions> auth) =>
            Results.Ok(new CapabilitiesDto(
                whatsApp.Value.Enabled,
                new AuthCapabilitiesDto(auth.Value.Enabled, AuthEndpoints.LoginPath))))
            .AllowAnonymous();

        return app;
    }

    private sealed record CapabilitiesDto(bool WhatsApp, AuthCapabilitiesDto Auth);

    private sealed record AuthCapabilitiesDto(bool Enabled, string LoginUrl);
}
