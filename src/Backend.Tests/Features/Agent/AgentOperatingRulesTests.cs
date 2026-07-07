using AwesomeAssertions;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentOperatingRulesTests
{
    private static readonly string[] BaseTools = ["lookup_free_slots", "create_event"];

    [Fact]
    public void Compose_ContainsHeaderAndBaseRules_Always()
    {
        var text = AgentOperatingRules.Compose(BaseTools);

        text.Should().StartWith("[Agent-Spielregeln — verbindlich]");
        text.Should().Contain("lookup_free_slots");
        text.Should().Contain("Zeit-Kontext-Block");
    }

    [Fact]
    public void Compose_WatchJobParagraph_OnlyWithWatchJobTool()
    {
        AgentOperatingRules.Compose(BaseTools).Should().NotContain("create_watch_job");
        AgentOperatingRules.Compose([.. BaseTools, "create_watch_job"]).Should().Contain("create_watch_job");
    }

    [Fact]
    public void Compose_WebParagraph_OnlyWithWebSearchTool()
    {
        AgentOperatingRules.Compose(BaseTools).Should().NotContain("web_search");
        var withWeb = AgentOperatingRules.Compose([.. BaseTools, "web_search"]);
        withWeb.Should().Contain("web_search");
        withWeb.Should().Contain("fetch_webpage");
    }
}
