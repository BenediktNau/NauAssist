using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.DeleteEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class DeleteEventTool : ITool
{
    public string Name => "delete_event";
    public string Description =>
        "Löscht einen Termin aus dem Kalender, nachdem der User bestätigt hat. " +
        "Voraussetzung ist eine gültige event_id, die zuvor über get_calendar_range " +
        "ermittelt wurde.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "event_id": { "type": "string", "description": "Provider-Event-ID (z. B. aus get_calendar_range)." }
          },
          "required": ["event_id"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public DeleteEventTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var eventId = args.GetProperty("event_id").GetString()!;
        var response = await _mediator.Send(new DeleteEventRequest(eventId), ct);
        return JsonSerializer.SerializeToElement(new { event_id = response.EventId, status = "deleted" });
    }
}
