using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace NauAssist.Backend.Features.Infrastructure.Auth;

/// <summary>
/// BFF-Pattern (wie Abrechner): Cookie-Session + serverseitiger OIDC-Code-Flow
/// gegen Keycloak. Der Browser bekommt nie ein Token, nur das HttpOnly-Cookie.
/// Wird nur registriert, wenn <see cref="AuthOptions.Enabled"/> — sonst verhält
/// sich die App exakt wie im Single-User-Betrieb.
/// </summary>
public static class AuthWiring
{
    public const string SessionCookieName = "nauassist.session";

    public static void AddBffAuth(this WebApplicationBuilder builder, AuthOptions auth)
    {
        auth.Validate();

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Hinter dem Proxy kommt der Request per X-Forwarded-Proto als https an
            // → Secure-Cookie in Produktion, lokal über http weiterhin nutzbar.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            // API-Calls bekommen 401/403 statt HTML-Redirects — das Frontend
            // entscheidet selbst, wann es auf /auth/login umleitet.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        })
        .AddOpenIdConnect(options =>
        {
            options.Authority = auth.IssuerUrl;
            options.ClientId = auth.ClientId;
            options.ClientSecret = auth.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false; // sub bleibt sub
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.RequireHttpsMetadata = auth.RequireHttpsMetadata;

            // Keycloak kann kein PAR; .NET 10 aktiviert es sonst automatisch → 405.
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");

            // Backchannel übers interne Compose-Netz statt Hairpin durch den Proxy.
            if (!string.IsNullOrEmpty(auth.InternalUrl))
            {
                var internalIssuer = $"{auth.InternalUrl.TrimEnd('/')}/realms/{auth.Realm}";
                options.MetadataAddress = $"{internalIssuer}/.well-known/openid-configuration";
                options.RequireHttpsMetadata = false;
                options.BackchannelHttpHandler =
                    new KeycloakBackchannelHandler(auth.IssuerUrl, internalIssuer);
                // Keycloak stellt Tokens mit der öffentlichen Issuer-URL aus.
                options.TokenValidationParameters.ValidIssuer = auth.IssuerUrl;
            }
        });

        // Alles erfordert Login — Ausnahmen (health, capabilities, /auth/*,
        // Frontend-Fallback) tragen explizit AllowAnonymous. Die Policy nennt das
        // Cookie-Schema explizit, damit unauthentifizierte API-Calls im 401-Pfad
        // des Cookie-Handlers landen statt im OIDC-Redirect (Default-Challenge).
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                    CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build());

        // X-Forwarded-Proto/Host vom Coolify-Proxy übernehmen, sonst stimmen
        // Redirect-URI und Secure-Cookie-Erkennung nicht.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }

    public static void UseBffAuth(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<UserContextMiddleware>();
    }
}
