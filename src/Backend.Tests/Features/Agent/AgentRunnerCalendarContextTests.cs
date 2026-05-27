using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentRunnerCalendarContextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static (AgentRunner runner, FakeLlmClient llm) Build(FakeCalendarProvider provider)
    {
        var local = new DateTime(2026, 5, 21, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        var builder = new CalendarContextBuilder(
            provider, new FakeSettingsRepo(searchHorizon: 14), Berlin);

        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk("Ok."));

        var runner = new AgentRunner(
            llm,
            tools: Array.Empty<NauAssist.Backend.Features.Agent.ITool>(),
            options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            logger: NullLogger<AgentRunner>.Instance,
            clockContext: clock,
            calendarContext: builder,
            settings: new FakeSettingsRepo());

        return (runner, llm);
    }

    [Fact]
    public async Task HandleAsync_NoAllDayEvents_PrependsOnlyTimeContext()
    {
        var (runner, llm) = Build(new FakeCalendarProvider());
        var history = new[] { new LlmMessage("user", "Hi") };

        await foreach (var _ in runner.HandleAsync(history, default)) { }

        var msgs = llm.CapturedCalls[0].Messages;
        msgs[0].Role.Should().Be("system");
        msgs[0].Content.Should().Contain("[Agent-Spielregeln");
        msgs[1].Role.Should().Be("system");
        msgs[1].Content.Should().Contain("[Zeit-Kontext");
        msgs[2].Role.Should().Be("user");
    }

    [Fact]
    public async Task HandleAsync_WithAllDayEvent_AppendsSecondSystemMessage()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e1", Title: "Schulung Köln",
            Start: new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var (runner, llm) = Build(provider);
        var history = new[] { new LlmMessage("user", "Was steht nächste Woche an?") };

        await foreach (var _ in runner.HandleAsync(history, default)) { }

        var msgs = llm.CapturedCalls[0].Messages;
        msgs[0].Role.Should().Be("system");
        msgs[0].Content.Should().Contain("[Agent-Spielregeln");
        msgs[1].Role.Should().Be("system");
        msgs[1].Content.Should().Contain("[Zeit-Kontext");
        msgs[2].Role.Should().Be("system");
        msgs[2].Content.Should().Contain("Schulung Köln");
        msgs[2].Content.Should().Contain("Mi 27.5.–Fr 29.5.");
        msgs[3].Role.Should().Be("user");
    }
}
