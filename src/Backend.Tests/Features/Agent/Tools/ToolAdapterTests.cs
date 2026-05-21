using System.Text.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;
using NauAssist.Backend.Features.Rules.DeleteRule;
using NauAssist.Backend.Features.Rules.ListRules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class ToolAdapterTests
{
    [Fact]
    public async Task LookupFreeSlotsTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<LookupFreeSlotsRequest, LookupFreeSlotsResponse>(
            new LookupFreeSlotsResponse(Array.Empty<SlotAnnotation>()));
        var tool = new LookupFreeSlotsTool(mediator);

        var args = JsonDocument.Parse("""
            {"from":"2026-05-27T10:00:00+02:00","to":"2026-05-27T18:00:00+02:00","duration_minutes":60}
            """).RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        mediator.SentRequests.Should().ContainSingle();
        var req = (LookupFreeSlotsRequest)mediator.SentRequests[0];
        req.DurationMinutes.Should().Be(60);
        result.TryGetProperty("annotations", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CreateEventTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<CreateEventRequest, CreateEventResponse>(
            new CreateEventResponse("event-id-42"));
        var tool = new CreateEventTool(mediator, TimeZoneInfo.Utc);

        var args = JsonDocument.Parse("""
            {"title":"Pierre","start":"2026-05-27T14:00:00+02:00","end":"2026-05-27T15:00:00+02:00"}
            """).RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        var req = (CreateEventRequest)mediator.SentRequests[0];
        req.Title.Should().Be("Pierre");
        result.GetProperty("event_id").GetString().Should().Be("event-id-42");
    }

    [Fact]
    public async Task GetCalendarRangeTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<GetCalendarRangeRequest, GetCalendarRangeResponse>(
            new GetCalendarRangeResponse(Array.Empty<CalendarEvent>()));
        var tool = new GetCalendarRangeTool(mediator);

        var args = JsonDocument.Parse("""
            {"from":"2026-05-27T00:00:00+02:00","to":"2026-05-28T00:00:00+02:00"}
            """).RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        mediator.SentRequests.Should().ContainSingle(r => r is GetCalendarRangeRequest);
    }

    [Fact]
    public async Task ListRulesTool_NoArgs_ReturnsList()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<ListRulesRequest, ListRulesResponse>(
            new ListRulesResponse(Array.Empty<Rule>()));
        var tool = new ListRulesTool(mediator);

        var result = await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        result.TryGetProperty("rules", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AddRuleTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        var rule = new Rule(1, "Mi 19-20", DayOfWeekFlags.Wednesday,
            new TimeOnly(19, 0), new TimeOnly(20, 0), RuleHardness.Hard, DateTimeOffset.UtcNow);
        mediator.SetupResponse<AddRuleRequest, AddRuleResponse>(new AddRuleResponse(rule));
        var tool = new AddRuleTool(mediator);

        var args = JsonDocument.Parse("""
            {"text":"Mi 19-20 Sport","days_of_week":["wednesday"],"time_start":"19:00","time_end":"20:00","hardness":"hard"}
            """).RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        var req = (AddRuleRequest)mediator.SentRequests[0];
        req.DaysOfWeek.Should().Be(DayOfWeekFlags.Wednesday);
        req.TimeRangeStart.Should().Be(new TimeOnly(19, 0));
        result.GetProperty("rule_id").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task DeleteRuleTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<DeleteRuleRequest, DeleteRuleResponse>(new DeleteRuleResponse(true));
        var tool = new DeleteRuleTool(mediator);

        var args = JsonDocument.Parse("""{"rule_id":42}""").RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        ((DeleteRuleRequest)mediator.SentRequests[0]).Id.Should().Be(42);
        result.GetProperty("deleted").GetBoolean().Should().BeTrue();
    }
}
