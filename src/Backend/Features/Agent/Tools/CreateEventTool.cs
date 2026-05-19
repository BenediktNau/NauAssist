using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.CreateEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class CreateEventTool : ITool
{
    public string Name => "create_event";
    public string Description => "Legt einen neuen Termin im Kalender an, nachdem der User bestätigt hat.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Titel des Termins" },
            "start": { "type": "string", "format": "date-time" },
            "end":   { "type": "string", "format": "date-time" },
            "description": { "type": ["string", "null"] },
            "location":    { "type": ["string", "null"] }
          },
          "required": ["title", "start", "end"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public CreateEventTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var request = new CreateEventRequest(
            Title: args.GetProperty("title").GetString()!,
            Start: DateTimeOffset.Parse(args.GetProperty("start").GetString()!),
            End: DateTimeOffset.Parse(args.GetProperty("end").GetString()!),
            Description: args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null,
            Location: args.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.String ? locEl.GetString() : null);

        var response = await _mediator.Send(request, ct);
        return JsonSerializer.SerializeToElement(new { event_id = response.EventId, status = "created" });
    }
}
