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

public sealed class AgentRunnerTimeContextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task HandleAsync_PrependsTimeContextSystemMessage_BeforeHistory()
    {
        var local = new DateTime(2026, 5, 20, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);

        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk("Alles klar."));

        var calendarContextProvider = new FakeCalendarProvider();
        var calendarContext = new CalendarContextBuilder(
            calendarContextProvider,
            Options.Create(new CalendarOptions { SearchHorizonDays = 14 }),
            Berlin);

        var runner = new AgentRunner(
            llm,
            tools: Array.Empty<NauAssist.Backend.Features.Agent.ITool>(),
            options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            logger: NullLogger<AgentRunner>.Instance,
            clockContext: clock,
            calendarContext: calendarContext);

        var history = new[]
        {
            new LlmMessage("user", "Was steht nächste Woche an?"),
        };

        await foreach (var _ in runner.HandleAsync(history, default)) { }

        llm.CapturedCalls.Should().HaveCount(1);
        var msgs = llm.CapturedCalls[0].Messages;

        // Erste Message ist die Zeit-Kontext-System-Message, dann folgt die History.
        msgs[0].Role.Should().Be("system");
        msgs[0].Content.Should().Contain("[Zeit-Kontext");
        msgs[0].Content.Should().Contain("2026-05-20");
        msgs[0].Content.Should().Contain("Mittwoch");
        msgs[0].Content.Should().Contain("Nächste Woche:  2026-05-25 (Mo) bis 2026-05-31 (So)");
        msgs[0].Content.Should().Contain("Nächstes WE:    2026-05-30 (Sa) bis 2026-05-31 (So)");

        msgs[1].Role.Should().Be("user");
        msgs[1].Content.Should().Be("Was steht nächste Woche an?");
    }
}
