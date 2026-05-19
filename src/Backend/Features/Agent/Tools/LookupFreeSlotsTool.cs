using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class LookupFreeSlotsTool : ITool
{
    public string Name => "lookup_free_slots";
    public string Description =>
        "Sucht freie Slots im Kalender für einen Zeitbereich. Berücksichtigt aktive Regeln. " +
        "Liefert eine annotierte Liste von Kandidaten — der Agent wählt 2–3 daraus und ruft danach present_proposals.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "format": "date-time", "description": "ISO-8601-Beginn des Suchbereichs" },
            "to":   { "type": "string", "format": "date-time", "description": "ISO-8601-Ende des Suchbereichs (exklusiv)" },
            "duration_minutes": { "type": "integer", "minimum": 1, "description": "Gewünschte Slot-Länge in Minuten" }
          },
          "required": ["from", "to", "duration_minutes"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public LookupFreeSlotsTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var from = DateTimeOffset.Parse(args.GetProperty("from").GetString()!);
        var to = DateTimeOffset.Parse(args.GetProperty("to").GetString()!);
        var duration = args.GetProperty("duration_minutes").GetInt32();

        var response = await _mediator.Send(new LookupFreeSlotsRequest(from, to, duration), ct);

        var resultObj = new
        {
            annotations = response.Annotations.Select(a => new
            {
                start = a.Slot.Start.ToString("O"),
                end = a.Slot.End.ToString("O"),
                status = a.Status.ToString().ToLowerInvariant(),
                violated_by = a.ViolatedBy is null ? null : new { id = a.ViolatedBy.Id, text = a.ViolatedBy.Text },
            }),
        };
        return JsonSerializer.SerializeToElement(resultObj);
    }
}
