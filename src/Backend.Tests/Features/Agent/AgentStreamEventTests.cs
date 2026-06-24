using AwesomeAssertions;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentStreamEventTests
{
    [Fact]
    public void Events_CanBeMatchedViaSwitch()
    {
        AgentStreamEvent[] events = {
            new TokenEvent("hallo"),
            new ToolStartedEvent("lookup_free_slots"),
            new ToolFinishedEvent("lookup_free_slots", Ok: true),
            new ProposalsEvent(new[] { new SlotInfo(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "Test") }),
            new DoneEvent(),
            new ErrorEvent("oops", "corr-1"),
        };

        var kinds = events.Select(e => e switch
        {
            TokenEvent => "token",
            ToolStartedEvent => "tool_started",
            ToolFinishedEvent => "tool_finished",
            ProposalsEvent => "proposals",
            DoneEvent => "done",
            ErrorEvent => "error",
            _ => "unknown",
        }).ToArray();

        kinds.Should().BeEquivalentTo(new[] { "token", "tool_started", "tool_finished", "proposals", "done", "error" });
    }
}
