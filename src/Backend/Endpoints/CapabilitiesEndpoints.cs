using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Meldet dem Frontend, welche optionalen Features aktiv sind, damit die UI
/// entsprechende Bereiche ein- oder ausblenden kann (z.B. WhatsApp-Section).
/// </summary>
public static class CapabilitiesEndpoints
{
    public static IEndpointRouteBuilder MapCapabilitiesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/capabilities", (IOptions<WhatsAppOptions> whatsApp) =>
            Results.Ok(new CapabilitiesDto(whatsApp.Value.Enabled)));

        return app;
    }

    private sealed record CapabilitiesDto(bool WhatsApp);
}
