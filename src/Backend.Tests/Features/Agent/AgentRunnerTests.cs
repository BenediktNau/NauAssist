using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentRunnerTests
{
    private static readonly ClockContext DefaultClock = new ClockContext(
        () => DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

    private static readonly CalendarContextBuilder DefaultCalendarContext = new CalendarContextBuilder(
        new FakeCalendarProvider(),
        new FakeSettingsRepo(searchHorizon: 14),
        TimeZoneInfo.Utc);

    [Fact]
    public async Task Run_PureTextResponse_YieldsTokensAndDone()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(
            new TextDeltaChunk("Hallo"),
            new TextDeltaChunk(", "),
            new TextDeltaChunk("Welt"));

        var runner = new AgentRunner(llm, Array.Empty<ITool>(),
            Options.Create(new AgentOptions()),
            NullLogger<AgentRunner>.Instance,
            DefaultClock,
            DefaultCalendarContext);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "Hi") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.OfType<TokenEvent>().Select(t => t.Text).Should().Equal("Hallo", ", ", "Welt");
        events.Last().Should().BeOfType<DoneEvent>();
    }

    [Fact]
    public async Task Run_WhenLlmCallsTool_ExecutesAndFeedsResultBack()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            Id: "call-1",
            Name: "echo",
            Arguments: JsonDocument.Parse("""{"value":"hi"}""").RootElement)));
        llm.QueueResponse(new TextDeltaChunk("fertig"));

        var echoTool = new EchoTool();
        var runner = new AgentRunner(llm, new[] { (ITool)echoTool },
            Options.Create(new AgentOptions()),
            NullLogger<AgentRunner>.Instance,
            DefaultClock,
            DefaultCalendarContext);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "Hi") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.OfType<ToolStartedEvent>().Should().Contain(ts => ts.Name == "echo");
        events.OfType<ToolFinishedEvent>().Should().Contain(tf => tf.Name == "echo" && tf.Ok);
        events.OfType<TokenEvent>().Single().Text.Should().Be("fertig");
        events.Last().Should().BeOfType<DoneEvent>();
        echoTool.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Run_PresentProposalsTool_EmitsProposalsEvent_WithoutCallingTool()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            Id: "call-1",
            Name: "present_proposals",
            Arguments: JsonDocument.Parse("""
                {"slots":[
                    {"start":"2026-05-27T10:00:00+02:00","end":"2026-05-27T11:00:00+02:00","note":"Vormittag"},
                    {"start":"2026-05-27T14:00:00+02:00","end":"2026-05-27T15:00:00+02:00","note":"Nachmittag"}
                ]}
                """).RootElement)));
        llm.QueueResponse(new TextDeltaChunk("Zwei Slots für dich"));

        var present = new PresentProposalsTool();
        var runner = new AgentRunner(llm, new[] { (ITool)present },
            Options.Create(new AgentOptions()),
            NullLogger<AgentRunner>.Instance,
            DefaultClock,
            DefaultCalendarContext);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "Vorschläge?") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.OfType<ProposalsEvent>().Should().HaveCount(1);
        events.OfType<ProposalsEvent>().Single().Slots.Should().HaveCount(2);
        events.OfType<TokenEvent>().Single().Text.Should().Be("Zwei Slots für dich");
    }

    [Fact]
    public async Task Run_PresentProposals_WithMalformedDate_SkipsInvalidSlot_NoCrash()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            Id: "call-1",
            Name: "present_proposals",
            Arguments: JsonDocument.Parse("""
                {"slots":[
                    {"start":"20to6-05-28T15:00:00+02:00","end":"2026-05-28T16:00:00+02:00"},
                    {"start":"2026-05-27T10:00:00+02:00","end":"2026-05-27T11:00:00+02:00","note":"valid"}
                ]}
                """).RootElement)));
        llm.QueueResponse(new TextDeltaChunk("done"));

        var runner = new AgentRunner(llm, new[] { (ITool)new PresentProposalsTool() },
            Options.Create(new AgentOptions()),
            NullLogger<AgentRunner>.Instance,
            DefaultClock,
            DefaultCalendarContext);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "?") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        var proposals = events.OfType<ProposalsEvent>().Single();
        proposals.Slots.Should().HaveCount(1);
        proposals.Slots[0].Note.Should().Be("valid");
        events.Last().Should().BeOfType<DoneEvent>();
    }

    [Fact]
    public async Task Run_ExceedsIterationLimit_EmitsErrorEvent()
    {
        var llm = new FakeLlmClient();
        for (var i = 0; i < 6; i++)
        {
            llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
                Id: $"call-{i}",
                Name: "echo",
                Arguments: JsonDocument.Parse("""{"value":"loop"}""").RootElement)));
        }

        var echoTool = new EchoTool();
        var runner = new AgentRunner(llm, new[] { (ITool)echoTool },
            Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            NullLogger<AgentRunner>.Instance,
            DefaultClock,
            DefaultCalendarContext);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "loop") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.Last().Should().BeOfType<ErrorEvent>();
        echoTool.ExecutionCount.Should().Be(5);
    }

    [Fact]
    public async Task Run_ToolThrows_EmitsToolFinishedNotOkAndContinues()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall("c1", "boom", JsonDocument.Parse("{}").RootElement)));
        llm.QueueResponse(new TextDeltaChunk("hab den Fehler bemerkt"));

        var runner = new AgentRunner(llm, new[] { (ITool)new ThrowingTool() },
            Options.Create(new AgentOptions()),
            NullLogger<AgentRunner>.Instance,
            DefaultClock,
            DefaultCalendarContext);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "?") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.OfType<ToolFinishedEvent>().Should().Contain(tf => tf.Name == "boom" && !tf.Ok);
        events.OfType<TokenEvent>().Single().Text.Should().Be("hab den Fehler bemerkt");
    }

    private sealed class EchoTool : ITool
    {
        public int ExecutionCount { get; private set; }
        public string Name => "echo";
        public string Description => "Echo-Test-Tool";
        public JsonElement ParameterSchema { get; } =
            JsonDocument.Parse("""{"type":"object"}""").RootElement;
        public Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
        {
            ExecutionCount++;
            return Task.FromResult(args);
        }
    }

    private sealed class ThrowingTool : ITool
    {
        public string Name => "boom";
        public string Description => "Wirft.";
        public JsonElement ParameterSchema { get; } =
            JsonDocument.Parse("""{"type":"object"}""").RootElement;
        public Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct) =>
            throw new InvalidOperationException("kaputt");
    }
}
