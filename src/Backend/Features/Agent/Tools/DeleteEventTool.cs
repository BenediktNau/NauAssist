using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.DeleteEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class DeleteEventTool : ITool
{
    public string Name => "delete_event";
    public string Description =>
        "Löscht einen Termin aus dem Kalender, nachdem der User bestätigt hat. " +
        "Voraussetzung ist eine gültige event_id, die zuvor über get_calendar_range " +
        "ermittelt wurde. Bei Serien-Instanzen (is_series_instance=true) den scope " +
        "explizit mitgeben: 'instance' für die einzelne Instanz, 'series' für die " +
        "gesamte Serie. Default ist 'instance'.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "event_id": { "type": "string", "description": "Provider-Event-ID (z. B. aus get_calendar_range)." },
            "scope":    { "type": "string", "enum": ["instance", "series"], "description": "Bei Serien: 'instance' (nur dieser Termin) oder 'series' (gesamte Serie). Default: instance." }
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
        var scope = ParseScope(args);
        var response = await _mediator.Send(new DeleteEventRequest(eventId, scope), ct);
        return JsonSerializer.SerializeToElement(new
        {
            event_id = response.EventId,
            scope = response.Scope.ToString().ToLowerInvariant(),
            status = "deleted",
        });
    }

    private static EventScope ParseScope(JsonElement args)
    {
        if (args.TryGetProperty("scope", out var el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString()?.ToLowerInvariant() switch
            {
                "series" => EventScope.Series,
                "instance" => EventScope.Instance,
                null or "" => EventScope.Instance,
                var other => throw new ArgumentException($"Unbekannter scope '{other}'. Erlaubt: instance, series."),
            };
        }
        return EventScope.Instance;
    }
}
