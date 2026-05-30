using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class SourceAccountsWhatsAppTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public SourceAccountsWhatsAppTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_WhatsApp_MissingSessionId_Returns400()
    {
        var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/source-accounts/", new
        {
            kind = "whatsapp",
            displayName = "WA",
            credentials = new { phoneLabel = "+49" },
            allowlist = Array.Empty<string>(),
        });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WhatsApp_Valid_RedactsCredentialsWithoutSecrets()
    {
        var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/source-accounts/", new
        {
            kind = "whatsapp",
            displayName = "WA-Privat",
            credentials = new { sessionId = "sess-xyz", phoneLabel = "+49 151" },
            allowlist = new[] { "chatA@s.whatsapp.net" },
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await res.Content.ReadFromJsonAsync<AccountDto>();
        dto!.Kind.Should().Be("whatsapp");
        dto.Credentials["sessionId"].Should().Be("sess-xyz");
        dto.Credentials["phoneLabel"].Should().Be("+49 151");
        dto.Credentials.Should().NotContainKey("password");
        dto.Credentials.Should().NotContainKey("accessToken");
    }

    private sealed record AccountDto(
        long Id,
        string Kind,
        string DisplayName,
        Dictionary<string, string?> Credentials,
        string[] Allowlist,
        bool Enabled);
}
