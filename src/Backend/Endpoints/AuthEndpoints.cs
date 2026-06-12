using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// BFF-Endpoints für den serverseitigen OIDC-Flow (nur gemappt, wenn Auth an).
/// Login löst den Keycloak-Redirect aus, Logout beendet Cookie- und SSO-Session,
/// /auth/me liefert dem Frontend den Login-Status.
/// </summary>
public static class AuthEndpoints
{
    public const string LoginPath = "/auth/login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/login", (HttpContext ctx) =>
        {
            var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault() ?? "/";
            // Open-Redirect-Guard: nur relative Pfade, keine protokoll-relativen URLs.
            if (returnUrl.StartsWith("//") || !Uri.TryCreate(returnUrl, UriKind.Relative, out _))
            {
                returnUrl = "/";
            }

            var props = new AuthenticationProperties { RedirectUri = returnUrl, IsPersistent = true };
            return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
            {
                RedirectUri = "/",
            });
        }).AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Ok(new MeDto(false, null, null, null));
            }

            return Results.Ok(new MeDto(
                IsAuthenticated: true,
                Sub: user.FindFirstValue("sub"),
                Username: user.FindFirstValue("preferred_username"),
                Email: user.FindFirstValue("email")));
        }).AllowAnonymous();

        return app;
    }

    private sealed record MeDto(bool IsAuthenticated, string? Sub, string? Username, string? Email);
}
