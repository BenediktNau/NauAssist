using System.Net;
using AwesomeAssertions;
using NauAssist.Backend.Features.Web;

namespace NauAssist.Backend.Tests.Features.Web;

public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("http://example.com/x", true)]
    [InlineData("https://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("/relative/path", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void IsAllowedUrl_AcceptsOnlyAbsoluteHttp(string url, bool expected)
    {
        SsrfGuard.IsAllowedUrl(url, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // Cloud-Metadaten
    [InlineData("100.64.0.1")]      // CGNAT
    [InlineData("0.0.0.0")]
    [InlineData("::1")]             // IPv6 loopback
    [InlineData("fe80::1")]         // IPv6 link-local
    [InlineData("fc00::1")]         // IPv6 unique local
    [InlineData("::ffff:10.0.0.1")] // IPv4-mapped privat
    [InlineData("::10.0.0.1")]      // IPv4-kompatibel privat
    [InlineData("::192.168.1.1")]   // IPv4-kompatibel privat
    [InlineData("::127.0.0.1")]     // IPv4-kompatibel loopback
    public void IsBlockedAddress_BlocksInternalAndPrivate(string ip)
    {
        SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]   // knapp außerhalb 172.16/12
    [InlineData("172.32.0.1")]   // knapp außerhalb 172.16/12
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")] // IPv4-mapped öffentlich
    public void IsBlockedAddress_AllowsPublic(string ip)
    {
        SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeFalse();
    }
}
