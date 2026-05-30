using FluentAssertions;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

namespace NauAssist.Backend.Tests.Features.AutonomousAgent;

public sealed class WhatsAppCredentialsTests
{
    [Fact]
    public void Parse_Roundtrips()
    {
        var c = WhatsAppCredentials.Parse("{\"sessionId\":\"abc-123\",\"phoneLabel\":\"+49 151 000\"}");

        c.SessionId.Should().Be("abc-123");
        c.PhoneLabel.Should().Be("+49 151 000");
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        var c = WhatsAppCredentials.Parse("{\"SessionId\":\"X\"}");
        c.SessionId.Should().Be("X");
    }

    [Fact]
    public void Parse_MissingSessionId_Throws()
    {
        var act = () => WhatsAppCredentials.Parse("{\"phoneLabel\":\"+49\"}");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_EmptySessionId_Throws()
    {
        var act = () => WhatsAppCredentials.Parse("{\"sessionId\":\"  \"}");
        act.Should().Throw<ArgumentException>();
    }
}
