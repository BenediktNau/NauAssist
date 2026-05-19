using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class GetCalendarRangeTool : ITool
{
    public string Name => "get_calendar_range";
    public string Description => "Liefert alle Termine im angefragten Zeitbereich (z. B. um Kontext zu schaffen).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "format": "date-time" },
            "to":   { "type": "string", "format": "date-time" }
          },
          "required": ["from", "to"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public GetCalendarRangeTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var from = DateTimeOffset.Parse(args.GetProperty("from").GetString()!);
        var to = DateTimeOffset.Parse(args.GetProperty("to").GetString()!);

        var response = await _mediator.Send(new GetCalendarRangeRequest(from, to), ct);
        var resultObj = new
        {
            events = response.Events.Select(e => new
            {
                id = e.Id,
                title = e.Title,
                start = e.Start.ToString("O"),
                end = e.End.ToString("O"),
                description = e.Description,
                location = e.Location,
            }),
        };
        return JsonSerializer.SerializeToElement(resultObj);
    }
}
