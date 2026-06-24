using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

/// <summary>
/// Auth-Toggle (Spec §1/§5/§10): Auth aus → exakt heutiges Verhalten, kein Login.
/// Auth an → /api/* erfordert Session (401), während health/capabilities//auth/me
/// anonym erreichbar bleiben (das Frontend braucht sie vor dem Login).
/// </summary>
public sealed class AuthToggleTests
{
    private sealed class AuthEnabledAppFactory : TestAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // UseSetting statt ConfigureAppConfiguration: Program.cs liest den
            // Auth-Block imperativ beim Startup — nur Host-Settings sind dort
            // schon sichtbar. Keycloak läuft in Tests nicht; OIDC-Metadata wird
            // lazy geladen, erst ein echter Login-Versuch kontaktiert die Authority.
            builder.UseSetting("Auth:Enabled", "true");
            builder.UseSetting("Auth:Authority", "http://localhost:9");
            builder.UseSetting("Auth:Realm", "nauassist");
            builder.UseSetting("Auth:ClientId", "nauassist-web");
            builder.UseSetting("Auth:ClientSecret", "test-secret");
            builder.UseSetting("Auth:RequireHttpsMetadata", "false");
        }
    }

    [Fact]
    public async Task AuthAus_ApiLaeuftOhneLogin_CapabilitiesMeldenDisabled()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var history = await client.GetAsync("/api/chat/history");
        history.StatusCode.Should().Be(HttpStatusCode.OK);

        var caps = await client.GetFromJsonAsync<JsonElement>("/api/capabilities");
        caps.GetProperty("auth").GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AuthAn_ApiVerlangtSession_OffeneEndpointsBleibenOffen()
    {
        using var factory = new AuthEnabledAppFactory();
        using var client = factory.CreateClient();

        var caps = await client.GetFromJsonAsync<JsonElement>("/api/capabilities");
        caps.GetProperty("auth").GetProperty("enabled").GetBoolean().Should().BeTrue();
        caps.GetProperty("auth").GetProperty("loginUrl").GetString().Should().Be("/auth/login");

        (await client.GetAsync("/api/chat/history")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/suggestions/")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
    }
}
