using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Rules.ListRules;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class ListRulesTool : ITool
{
    public string Name => "list_rules";
    public string Description => "Listet alle gespeicherten Regeln auf.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    private readonly IMediator _mediator;

    public ListRulesTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var response = await _mediator.Send(new ListRulesRequest(), ct);
        var resultObj = new
        {
            rules = response.Rules.Select(r => new
            {
                id = r.Id,
                text = r.Text,
                days_of_week = (int)r.DaysOfWeek,
                time_start = r.TimeRangeStart?.ToString("HH:mm"),
                time_end = r.TimeRangeEnd?.ToString("HH:mm"),
                hardness = r.Hardness.ToString().ToLowerInvariant(),
            }),
        };
        return JsonSerializer.SerializeToElement(resultObj);
    }
}
