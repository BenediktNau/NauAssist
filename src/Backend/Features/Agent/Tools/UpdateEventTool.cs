using System.Globalization;
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.UpdateEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class UpdateEventTool : ITool
{
    public string Name => "update_event";
    public string Description =>
        "Ändert einen bestehenden Termin (z. B. verschieben, umbenennen, neuer Ort) " +
        "nach Bestätigung durch den User. Alle Felder außer event_id sind optional; " +
        "nicht gesetzte Felder bleiben unverändert. Bei All-Day-Einträgen analog " +
        "create_event: start/end im Format yyyy-MM-dd, end exklusiv. Bei Serien-" +
        "Instanzen (is_series_instance=true) den scope mitgeben: 'instance' (nur " +
        "diese Instanz) oder 'series' (Master, wirkt auf alle Instanzen). Default: instance.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "event_id":    { "type": "string", "description": "Provider-Event-ID, z. B. aus get_calendar_range." },
            "title":       { "type": ["string", "null"] },
            "start":       { "type": ["string", "null"] },
            "end":         { "type": ["string", "null"] },
            "description": { "type": ["string", "null"] },
            "location":    { "type": ["string", "null"] },
            "is_all_day":  { "type": ["boolean", "null"] },
            "scope":       { "type": "string", "enum": ["instance", "series"], "description": "Bei Serien: 'instance' oder 'series'. Default: instance." }
          },
          "required": ["event_id"]
        }
        """).RootElement;

    private readonly IMediator _mediator;
    private readonly TimeZoneInfo _zone;

    public UpdateEventTool(IMediator mediator, TimeZoneInfo zone)
    {
        _mediator = mediator;
        _zone = zone;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var eventId = args.GetProperty("event_id").GetString()!;

        bool? isAllDay = null;
        if (args.TryGetProperty("is_all_day", out var allDayEl))
        {
            if (allDayEl.ValueKind == JsonValueKind.True) isAllDay = true;
            else if (allDayEl.ValueKind == JsonValueKind.False) isAllDay = false;
        }

        DateTimeOffset? start = null;
        if (args.TryGetProperty("start", out var startEl) && startEl.ValueKind == JsonValueKind.String)
        {
            start = (isAllDay == true) ? ParseDateOnly(startEl.GetString()!) : DateTimeOffset.Parse(startEl.GetString()!);
        }

        DateTimeOffset? end = null;
        if (args.TryGetProperty("end", out var endEl) && endEl.ValueKind == JsonValueKind.String)
        {
            end = (isAllDay == true) ? ParseDateOnly(endEl.GetString()!) : DateTimeOffset.Parse(endEl.GetString()!);
        }

        var update = new EventUpdate(
            Title: args.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String ? titleEl.GetString() : null,
            Start: start,
            End: end,
            Description: args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null,
            Location: args.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.String ? locEl.GetString() : null,
            IsAllDay: isAllDay);

        var scope = ParseScope(args);
        var response = await _mediator.Send(new UpdateEventRequest(eventId, update, scope), ct);
        return JsonSerializer.SerializeToElement(new
        {
            event_id = response.EventId,
            scope = response.Scope.ToString().ToLowerInvariant(),
            status = "updated",
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

    private DateTimeOffset ParseDateOnly(string raw)
    {
        var date = DateOnly.ParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }
}
