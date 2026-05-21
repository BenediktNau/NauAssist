using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Chat.SendMessage;

public sealed class SendMessageHandler : IStreamCommandHandler<SendMessageRequest, SseEvent>
{
    private const int HistoryWindow = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly MessageRepository _messages;
    private readonly AgentRunner _runner;
    private readonly ChatContextCutoff _cutoff;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        MessageRepository messages,
        AgentRunner runner,
        ChatContextCutoff cutoff,
        Func<DateTimeOffset> clock,
        ILogger<SendMessageHandler> logger)
    {
        _messages = messages;
        _runner = runner;
        _cutoff = cutoff;
        _clock = clock;
        _logger = logger;
    }

    public async IAsyncEnumerable<SseEvent> Handle(
        SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await _messages.AddAsync(
            new Message(0, request.SessionId, MessageRole.User, request.UserText, null, false, _clock()),
            ct);

        var cutoffTime = await _cutoff.ComputeAsync(request.SessionId, ct);
        var recent = await _messages.GetSinceAsync(request.SessionId, cutoffTime, HistoryWindow, ct);
        var ordered = recent.Reverse().ToList();
        var history = ordered.Select(MapToLlmMessage).ToList();

        var accumulated = new StringBuilder();
        IReadOnlyList<SlotInfo>? lastProposals = null;
        var terminated = false;

        await foreach (var ev in _runner.HandleAsync(history, ct).WithCancellation(ct))
        {
            switch (ev)
            {
                case TokenEvent t:
                    accumulated.Append(t.Text);
                    yield return new SseToken(t.Text);
                    break;
                case ToolStartedEvent ts:
                    yield return new SseToolStarted(ts.Name);
                    break;
                case ToolFinishedEvent tf:
                    yield return new SseToolFinished(tf.Name, tf.Ok);
                    break;
                case ProposalsEvent p:
                    lastProposals = p.Slots;
                    yield return new SseProposals(p.Slots);
                    break;
                case DoneEvent:
                {
                    var id = await PersistAssistantAsync(
                        request.SessionId, accumulated.ToString(), lastProposals, incomplete: false, ct);
                    terminated = true;
                    yield return new SseDone(id);
                    yield break;
                }
                case ErrorEvent e:
                {
                    var id = await PersistAssistantAsync(
                        request.SessionId, accumulated.ToString(), lastProposals, incomplete: true, ct);
                    _logger.LogWarning("AgentRunner Error: {Message} (msg-id {Id})", e.Message, id);
                    terminated = true;
                    yield return new SseError(e.Message, e.CorrelationId);
                    yield break;
                }
            }
        }

        if (!terminated)
        {
            await PersistAssistantAsync(
                request.SessionId, accumulated.ToString(), lastProposals, incomplete: true, ct);
        }
    }

    private async Task<long> PersistAssistantAsync(
        string sessionId,
        string content,
        IReadOnlyList<SlotInfo>? proposals,
        bool incomplete,
        CancellationToken ct)
    {
        var proposalsJson = proposals is null
            ? null
            : JsonSerializer.Serialize(proposals, JsonOptions);

        var saved = await _messages.AddAsync(
            new Message(0, sessionId, MessageRole.Assistant, content, proposalsJson, incomplete, _clock()),
            ct);
        return saved.Id;
    }

    private static LlmMessage MapToLlmMessage(Message m) => m.Role switch
    {
        MessageRole.User => new LlmMessage("user", m.Content),
        MessageRole.Assistant => new LlmMessage("assistant", m.Content),
        _ => throw new InvalidOperationException($"Unbekannte MessageRole {m.Role}"),
    };
}
