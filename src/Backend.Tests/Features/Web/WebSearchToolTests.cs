using System.Text.Json;
using AwesomeAssertions;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Features.Web.Tools;

namespace NauAssist.Backend.Tests.Features.Web;

public sealed class WebSearchToolTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private sealed class FakeWebSearch : IWebSearch
    {
        public IReadOnlyList<WebSearchHit> Hits { get; set; } = [];
        public int? ReceivedMaxResults;
        public string? ReceivedQuery;

        public Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            ReceivedQuery = query;
            ReceivedMaxResults = maxResults;
            return Task.FromResult(Hits);
        }
    }

    [Fact]
    public async Task Search_MapsHitsToCompactJson()
    {
        var search = new FakeWebSearch
        {
            Hits = [new WebSearchHit("Titel", "https://example.org", "Snippet-Text")],
        };
        var tool = new WebSearchTool(search);

        var result = await tool.ExecuteAsync(Args("""{ "query": "midea portasplit" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        var hits = result.GetProperty("results").EnumerateArray().ToList();
        hits.Should().HaveCount(1);
        hits[0].GetProperty("title").GetString().Should().Be("Titel");
        hits[0].GetProperty("url").GetString().Should().Be("https://example.org");
        hits[0].GetProperty("snippet").GetString().Should().Be("Snippet-Text");
        search.ReceivedQuery.Should().Be("midea portasplit");
    }

    [Fact]
    public async Task Search_DefaultsToFiveResults_AndClampsToEight()
    {
        var search = new FakeWebSearch();
        var tool = new WebSearchTool(search);

        await tool.ExecuteAsync(Args("""{ "query": "x" }"""), CancellationToken.None);
        search.ReceivedMaxResults.Should().Be(5);

        await tool.ExecuteAsync(Args("""{ "query": "x", "max_results": 50 }"""), CancellationToken.None);
        search.ReceivedMaxResults.Should().Be(8);

        await tool.ExecuteAsync(Args("""{ "query": "x", "max_results": 0 }"""), CancellationToken.None);
        search.ReceivedMaxResults.Should().Be(1);
    }

    [Fact]
    public async Task Search_WithoutQuery_ReturnsError()
    {
        var tool = new WebSearchTool(new FakeWebSearch());

        var result = await tool.ExecuteAsync(Args("""{ "max_results": 3 }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Search_NonStringQuery_ReturnsError()
    {
        var tool = new WebSearchTool(new FakeWebSearch());

        var result = await tool.ExecuteAsync(Args("""{ "query": 123 }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Search_NonIntegerMaxResults_FallsBackToDefault()
    {
        var search = new FakeWebSearch();
        var tool = new WebSearchTool(search);

        await tool.ExecuteAsync(Args("""{ "query": "x", "max_results": 3.7 }"""), CancellationToken.None);

        search.ReceivedMaxResults.Should().Be(5);
    }

    [Fact]
    public async Task Search_EmptyHits_ReturnsHintForLlm()
    {
        var tool = new WebSearchTool(new FakeWebSearch());

        var result = await tool.ExecuteAsync(Args("""{ "query": "x" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("results").GetArrayLength().Should().Be(0);
        result.GetProperty("hint").GetString().Should().NotBeNullOrEmpty();
    }
}
