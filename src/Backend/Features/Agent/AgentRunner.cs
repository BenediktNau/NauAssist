using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Agent;

public sealed class AgentRunner
{
    private readonly ILlmClient _llm;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentRunner> _logger;

    public AgentRunner(
        ILlmClient llm,
        IEnumerable<ITool> tools,
        IOptions<AgentOptions> options,
        ILogger<AgentRunner> logger)
    {
        _llm = llm;
        _tools = tools.ToDictionary(t => t.Name);
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<AgentStreamEvent> HandleAsync(
        IReadOnlyList<LlmMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var toolDefs = _tools.Values.Select(t => t.ToDefinition()).ToList();
        var conversation = history.ToList();

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var pendingToolCalls = new List<LlmToolCall>();

            await foreach (var chunk in _llm.ChatStreamAsync(conversation, toolDefs, ct).WithCancellation(ct))
            {
                switch (chunk)
                {
                    case TextDeltaChunk text:
                        yield return new TokenEvent(text.Text);
                        break;
                    case ToolCallChunk toolCall:
                        pendingToolCalls.Add(toolCall.Call);
                        break;
                }
            }

            if (pendingToolCalls.Count == 0)
            {
                yield return new DoneEvent();
                yield break;
            }

            conversation.Add(new LlmMessage(
                Role: "assistant",
                Content: null,
                ToolCalls: pendingToolCalls));

            foreach (var call in pendingToolCalls)
            {
                if (call.Name == PresentProposalsTool.ToolName)
                {
                    var slots = ParseSlotsFromPresentProposalsArgs(call.Arguments);
                    yield return new ProposalsEvent(slots);
                    yield return new ToolFinishedEvent(call.Name, Ok: true);
                    conversation.Add(new LlmMessage(
                        Role: "tool",
                        Content: """{"ok":true}""",
                        ToolCallId: call.Id));
                    continue;
                }

                if (!_tools.TryGetValue(call.Name, out var tool))
                {
                    _logger.LogWarning("LLM hat ein unbekanntes Tool gerufen: {Name}", call.Name);
                    yield return new ToolStartedEvent(call.Name);
                    yield return new ToolFinishedEvent(call.Name, Ok: false);
                    conversation.Add(new LlmMessage(
                        Role: "tool",
                        Content: $$"""{"error":"unknown_tool","name":"{{call.Name}}"}""",
                        ToolCallId: call.Id));
                    continue;
                }

                yield return new ToolStartedEvent(call.Name);

                JsonElement result;
                bool ok;
                try
                {
                    result = await tool.ExecuteAsync(call.Arguments, ct);
                    ok = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tool {Name} hat geworfen.", call.Name);
                    var errObj = new { error = ex.GetType().Name, message = ex.Message };
                    result = JsonSerializer.SerializeToElement(errObj);
                    ok = false;
                }

                yield return new ToolFinishedEvent(call.Name, ok);
                conversation.Add(new LlmMessage(
                    Role: "tool",
                    Content: result.GetRawText(),
                    ToolCallId: call.Id));
            }
        }

        _logger.LogWarning("Tool-Loop-Limit ({Max}) erreicht ohne finale Text-Antwort.", _options.MaxToolIterations);
        yield return new ErrorEvent("Ich komme da gerade nicht weiter (Tool-Loop-Limit erreicht).");
    }

    private static IReadOnlyList<SlotInfo> ParseSlotsFromPresentProposalsArgs(JsonElement args)
    {
        var slots = new List<SlotInfo>();
        if (!args.TryGetProperty("slots", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return slots;
        }

        foreach (var s in arr.EnumerateArray())
        {
            var start = DateTimeOffset.Parse(s.GetProperty("start").GetString()!);
            var end = DateTimeOffset.Parse(s.GetProperty("end").GetString()!);
            string? note = null;
            if (s.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
            {
                note = noteEl.GetString();
            }
            slots.Add(new SlotInfo(start, end, note));
        }
        return slots;
    }
}
