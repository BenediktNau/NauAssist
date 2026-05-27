using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.Agent;

public sealed class AgentRunner
{
    private readonly ILlmClient _llm;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentRunner> _logger;
    private readonly ClockContext _clockContext;
    private readonly CalendarContextBuilder _calendarContext;
    private readonly IAppSettingsRepository _settings;

    public AgentRunner(
        ILlmClient llm,
        IEnumerable<ITool> tools,
        IOptions<AgentOptions> options,
        ILogger<AgentRunner> logger,
        ClockContext clockContext,
        CalendarContextBuilder calendarContext,
        IAppSettingsRepository settings)
    {
        _llm = llm;
        _tools = tools.ToDictionary(t => t.Name);
        _options = options.Value;
        _logger = logger;
        _clockContext = clockContext;
        _calendarContext = calendarContext;
        _settings = settings;
    }

    public async IAsyncEnumerable<AgentStreamEvent> HandleAsync(
        IReadOnlyList<LlmMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var toolDefs = _tools.Values.Select(t => t.ToDefinition()).ToList();
        var snapshot = _clockContext.Build();
        var conversation = new List<LlmMessage>
        {
            new LlmMessage("system", AgentOperatingRules.Text),
            new LlmMessage("system", BuildTimeContextBlock(snapshot)),
        };

        var calendarBlock = await _calendarContext.BuildAsync(snapshot, ct);
        if (!string.IsNullOrWhiteSpace(calendarBlock))
        {
            conversation.Add(new LlmMessage("system", calendarBlock));
        }

        var persona = await _settings.GetUserPersonaAsync(ct);
        if (!string.IsNullOrWhiteSpace(persona))
        {
            conversation.Add(new LlmMessage(
                "system",
                $"[Was du über den User weißt — read-only Kontext aus dem autonomen Agenten]\n{persona}"));
        }

        conversation.AddRange(history);

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

                _logger.LogInformation("Tool-Call {Name} args={Args}", call.Name, call.Arguments.GetRawText());

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

    private IReadOnlyList<SlotInfo> ParseSlotsFromPresentProposalsArgs(JsonElement args)
    {
        var slots = new List<SlotInfo>();
        if (!args.TryGetProperty("slots", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return slots;
        }

        foreach (var s in arr.EnumerateArray())
        {
            var startRaw = s.TryGetProperty("start", out var sEl) ? sEl.GetString() : null;
            var endRaw = s.TryGetProperty("end", out var eEl) ? eEl.GetString() : null;

            if (!DateTimeOffset.TryParse(startRaw, out var start) ||
                !DateTimeOffset.TryParse(endRaw, out var end))
            {
                _logger.LogWarning(
                    "present_proposals: Slot mit ungültigem Datum übersprungen (start={Start}, end={End}).",
                    startRaw, endRaw);
                continue;
            }

            string? note = null;
            if (s.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
            {
                note = noteEl.GetString();
            }
            slots.Add(new SlotInfo(start, end, note));
        }
        return slots;
    }

    private static string BuildTimeContextBlock(TimeSnapshot s)
    {
        static string ShortDay(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "Mo",
            DayOfWeek.Tuesday => "Di",
            DayOfWeek.Wednesday => "Mi",
            DayOfWeek.Thursday => "Do",
            DayOfWeek.Friday => "Fr",
            DayOfWeek.Saturday => "Sa",
            DayOfWeek.Sunday => "So",
            _ => "?",
        };

        var nowIso = s.NowLocal.ToString("yyyy-MM-ddTHH:mm:sszzz");

        return
            $"[Zeit-Kontext — verbindlich, alle Daten in {s.Timezone}]\n" +
            $"Jetzt:          {nowIso} ({s.WeekdayDe}, KW {s.IsoWeek})\n" +
            $"Heute:          {s.Today:yyyy-MM-dd} ({ShortDay(s.Today.DayOfWeek)})\n" +
            $"Morgen:         {s.Tomorrow:yyyy-MM-dd} ({ShortDay(s.Tomorrow.DayOfWeek)})\n" +
            $"Diese Woche:    {s.ThisWeek.Start:yyyy-MM-dd} (Mo) bis {s.ThisWeek.End:yyyy-MM-dd} (So)\n" +
            $"Nächste Woche:  {s.NextWeek.Start:yyyy-MM-dd} (Mo) bis {s.NextWeek.End:yyyy-MM-dd} (So)\n" +
            $"Dieses WE:      {s.ThisWeekend.Start:yyyy-MM-dd} (Sa) bis {s.ThisWeekend.End:yyyy-MM-dd} (So)\n" +
            $"Nächstes WE:    {s.NextWeekend.Start:yyyy-MM-dd} (Sa) bis {s.NextWeekend.End:yyyy-MM-dd} (So)\n" +
            "\n" +
            "Wochenkonvention: Montag ist der erste Tag der Woche (ISO 8601).\n" +
            "\"Nächste Woche\" = Mo–So der KW nach der aktuellen.\n" +
            "\"Dieses Wochenende\" = der Sa+So in der aktuellen KW.\n" +
            "\"Nächstes Wochenende\" = der Sa+So in der nächsten KW.\n" +
            "Wenn heute Sa/So ist, ist \"dieses Wochenende\" das laufende; \"nächstes Wochenende\" ist 7 Tage später.";
    }
}
