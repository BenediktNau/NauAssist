using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class CapabilitiesEndpointTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public CapabilitiesEndpointTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Capabilities_DefaultsToWhatsAppDisabled()
    {
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/api/capabilities");

        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<CapsDto>();
        dto!.WhatsApp.Should().BeFalse();
    }

    [Fact]
    public async Task Capabilities_ReflectsEnabledFlag()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AutonomousAgent:WhatsApp:Enabled", "true");
            builder.UseSetting("AutonomousAgent:WhatsApp:SidecarBaseUrl", "http://localhost:9");
        });
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/capabilities");

        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<CapsDto>();
        dto!.WhatsApp.Should().BeTrue();
    }

    private sealed record CapsDto(bool WhatsApp);
}
