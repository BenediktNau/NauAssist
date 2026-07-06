using System.Text.Json;
using AwesomeAssertions;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Features.Web.Tools;

namespace NauAssist.Backend.Tests.Features.Web;

public sealed class FetchWebpageToolTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private sealed class FakeWebFetch : IWebFetch
    {
        public WebDocument Result { get; set; } = new("https://example.org", 200, null, "Inhalt", false);

        public Task<WebDocument> FetchAsync(string url, string? etag, CancellationToken ct)
            => Task.FromResult(Result);
    }

    [Fact]
    public async Task Fetch_ReturnsTextStatusAndUrl()
    {
        var tool = new FetchWebpageTool(new FakeWebFetch());

        var result = await tool.ExecuteAsync(Args("""{ "url": "https://example.org" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("url").GetString().Should().Be("https://example.org");
        result.GetProperty("status").GetInt32().Should().Be(200);
        result.GetProperty("text").GetString().Should().Be("Inhalt");
        result.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Fetch_TruncatesLongTextAt6000Chars()
    {
        var fetch = new FakeWebFetch
        {
            Result = new WebDocument("https://example.org", 200, null, new string('a', 10_000), false),
        };
        var tool = new FetchWebpageTool(fetch);

        var result = await tool.ExecuteAsync(Args("""{ "url": "https://example.org" }"""), CancellationToken.None);

        result.GetProperty("text").GetString()!.Length.Should().Be(6000);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Fetch_InvalidUrl_ReturnsError()
    {
        var tool = new FetchWebpageTool(new FakeWebFetch());

        foreach (var badArgs in new[] { """{ }""", """{ "url": "not-a-url" }""", """{ "url": "file:///etc/passwd" }""", """{ "url": 123 }""" })
        {
            var result = await tool.ExecuteAsync(Args(badArgs), CancellationToken.None);
            result.GetProperty("ok").GetBoolean().Should().BeFalse();
            result.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Fetch_EmptyText_ReturnsHintForLlm()
    {
        var fetch = new FakeWebFetch
        {
            Result = new WebDocument("https://example.org", 0, null, "", false),
        };
        var tool = new FetchWebpageTool(fetch);

        var result = await tool.ExecuteAsync(Args("""{ "url": "https://example.org" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("status").GetInt32().Should().Be(0);
        result.GetProperty("hint").GetString().Should().NotBeNullOrEmpty();
    }
}
