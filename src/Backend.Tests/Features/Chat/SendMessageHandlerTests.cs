using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Chat.SendMessage;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class SendMessageHandlerTests
{
    [Fact]
    public async Task Handle_HappyPath_PersistsUserAndAssistant_YieldsDone()
    {
        using var temp = new TempSqliteDb();
        var messages = new MessageRepository(temp.AppDb);

        var fakeLlm = new FakeLlmClient();
        fakeLlm.QueueResponse(new TextDeltaChunk("Hallo "), new TextDeltaChunk("Benedikt"));

        var runner = BuildRunner(fakeLlm);
        var handler = BuildHandler(messages, runner);

        var events = await Collect(handler, new SendMessageRequest("default", "Hi"));

        events.OfType<SseToken>().Select(t => t.Text).Should().Equal("Hallo ", "Benedikt");
        events.Last().Should().BeOfType<SseDone>();

        var persisted = (await messages.GetRecentAsync("default", 10, CancellationToken.None))
            .Reverse().ToList();
        persisted.Should().HaveCount(2);
        persisted[0].Role.Should().Be(MessageRole.User);
        persisted[0].Content.Should().Be("Hi");
        persisted[1].Role.Should().Be(MessageRole.Assistant);
        persisted[1].Content.Should().Be("Hallo Benedikt");
        persisted[1].Incomplete.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DoneEvent_AssignsMessageIdInSseDone()
    {
        using var temp = new TempSqliteDb();
        var messages = new MessageRepository(temp.AppDb);

        var fakeLlm = new FakeLlmClient();
        fakeLlm.QueueResponse(new TextDeltaChunk("ok"));

        var handler = BuildHandler(messages, BuildRunner(fakeLlm));

        var events = await Collect(handler, new SendMessageRequest("default", "Hi"));

        var done = events.OfType<SseDone>().Single();
        var persistedAssistant = (await messages.GetRecentAsync("default", 1, CancellationToken.None))[0];
        done.MessageId.Should().Be(persistedAssistant.Id);
    }

    [Fact]
    public async Task Handle_ProposalsEvent_PersistsProposalsJsonOnAssistantMessage()
    {
        using var temp = new TempSqliteDb();
        var messages = new MessageRepository(temp.AppDb);

        var fakeLlm = new FakeLlmClient();
        fakeLlm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            "c1",
            PresentProposalsTool.ToolName,
            JsonDocument.Parse("""{"slots":[{"start":"2026-05-20T09:00:00Z","end":"2026-05-20T10:00:00Z"}]}""").RootElement)));
        fakeLlm.QueueResponse(new TextDeltaChunk("Vorschlag: Mi 09:00."));

        var handler = BuildHandler(messages, BuildRunner(fakeLlm));

        var events = await Collect(handler, new SendMessageRequest("default", "Termin?"));

        events.OfType<SseProposals>().Should().ContainSingle();
        events.Last().Should().BeOfType<SseDone>();

        var assistant = (await messages.GetRecentAsync("default", 5, CancellationToken.None))
            .Single(m => m.Role == MessageRole.Assistant);
        assistant.ProposalsJson.Should().NotBeNull();
        assistant.ProposalsJson!.Should().Contain("2026-05-20T09:00:00");
        assistant.Content.Should().Be("Vorschlag: Mi 09:00.");
    }

    [Fact]
    public async Task Handle_ToolLoopLimit_PersistsPartialAsIncomplete_YieldsError()
    {
        using var temp = new TempSqliteDb();
        var messages = new MessageRepository(temp.AppDb);

        var fakeLlm = new FakeLlmClient();
        // Unbekanntes Tool → AgentRunner loopt bis MaxToolIterations und yieldet ErrorEvent.
        for (var i = 0; i < 6; i++)
        {
            fakeLlm.QueueResponse(new ToolCallChunk(new LlmToolCall(
                $"c{i}", "no_such_tool",
                JsonDocument.Parse("{}").RootElement)));
        }

        var handler = BuildHandler(messages, BuildRunner(fakeLlm));

        var events = await Collect(handler, new SendMessageRequest("default", "Hi"));

        events.Last().Should().BeOfType<SseError>();

        var assistant = (await messages.GetRecentAsync("default", 5, CancellationToken.None))
            .Single(m => m.Role == MessageRole.Assistant);
        assistant.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PassesPriorHistoryToRunner()
    {
        using var temp = new TempSqliteDb();
        var messages = new MessageRepository(temp.AppDb);

        await messages.AddAsync(new Message(0, "default", MessageRole.User, "alt-frage", null, false,
            DateTimeOffset.Parse("2026-05-19T09:00:00Z")), CancellationToken.None);
        await messages.AddAsync(new Message(0, "default", MessageRole.Assistant, "alt-antwort", null, false,
            DateTimeOffset.Parse("2026-05-19T09:01:00Z")), CancellationToken.None);

        var fakeLlm = new FakeLlmClient();
        fakeLlm.QueueResponse(new TextDeltaChunk("ok"));

        var handler = BuildHandler(messages, BuildRunner(fakeLlm));

        await Collect(handler, new SendMessageRequest("default", "neue-frage"));

        fakeLlm.CapturedCalls.Should().ContainSingle();
        var sent = fakeLlm.CapturedCalls[0].Messages;
        // First message is the prepended time-context system message; skip it.
        sent.Skip(1).Select(m => m.Content).Should().Equal("alt-frage", "alt-antwort", "neue-frage");
    }

    private static readonly ClockContext DefaultClock = new ClockContext(
        () => DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

    private static AgentRunner BuildRunner(ILlmClient llm) =>
        new(
            llm,
            tools: Array.Empty<ITool>(),
            options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            logger: NullLogger<AgentRunner>.Instance,
            clockContext: DefaultClock);

    private static SendMessageHandler BuildHandler(MessageRepository messages, AgentRunner runner) =>
        new(
            messages,
            runner,
            clock: () => DateTimeOffset.Parse("2026-05-19T10:00:00Z"),
            logger: NullLogger<SendMessageHandler>.Instance);

    private static async Task<List<SseEvent>> Collect(SendMessageHandler handler, SendMessageRequest request)
    {
        var events = new List<SseEvent>();
        await foreach (var ev in handler.Handle(request, CancellationToken.None))
        {
            events.Add(ev);
        }
        return events;
    }
}
