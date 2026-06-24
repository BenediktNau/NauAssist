using AwesomeAssertions;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

namespace NauAssist.Backend.Tests.Features.AutonomousAgent;

public sealed class WhatsAppJidTests
{
    [Theory]
    [InlineData("4915112345678@s.whatsapp.net", "4915112345678@s.whatsapp.net")]
    [InlineData("4915112345678:7@s.whatsapp.net", "4915112345678@s.whatsapp.net")] // Device-Suffix
    [InlineData("4915112345678_1@s.whatsapp.net", "4915112345678@s.whatsapp.net")] // Agent-Suffix
    [InlineData("4915112345678@c.us", "4915112345678@s.whatsapp.net")]              // c.us → s.whatsapp.net
    [InlineData("153737280586099@lid", "153737280586099@lid")]                      // lid bleibt lid
    [InlineData("123456789-1620000000@g.us", "123456789-1620000000@g.us")]          // Gruppe unverändert
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_CanonicalisesJid(string? input, string expected)
    {
        WhatsAppJid.Normalize(input).Should().Be(expected);
    }
}
