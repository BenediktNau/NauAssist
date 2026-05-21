using System.Globalization;
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.CreateEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class CreateEventTool : ITool
{
    public string Name => "create_event";
    public string Description =>
        "Legt einen neuen Termin im Kalender an, nachdem der User bestätigt hat. " +
        "Setze is_all_day=true für ganztägige Einträge (Urlaub, Schulung); dann müssen " +
        "start und end im Format yyyy-MM-dd angegeben werden und end ist exklusiv " +
        "(1-Tages-Urlaub am 1.6. → start=2026-06-01, end=2026-06-02).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Titel des Termins" },
            "start": { "type": "string" },
            "end":   { "type": "string" },
            "description": { "type": ["string", "null"] },
            "location":    { "type": ["string", "null"] },
            "is_all_day":  { "type": "boolean", "default": false }
          },
          "required": ["title", "start", "end"]
        }
        """).RootElement;

    private readonly IMediator _mediator;
    private readonly TimeZoneInfo _zone;

    public CreateEventTool(IMediator mediator, TimeZoneInfo zone)
    {
        _mediator = mediator;
        _zone = zone;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var isAllDay =
            args.TryGetProperty("is_all_day", out var allDayEl) &&
            allDayEl.ValueKind == JsonValueKind.True;

        var startRaw = args.GetProperty("start").GetString()!;
        var endRaw = args.GetProperty("end").GetString()!;

        DateTimeOffset start, end;
        if (isAllDay)
        {
            start = ParseDateOnly(startRaw);
            end = ParseDateOnly(endRaw);
        }
        else
        {
            start = DateTimeOffset.Parse(startRaw);
            end = DateTimeOffset.Parse(endRaw);
        }

        var request = new CreateEventRequest(
            Title: args.GetProperty("title").GetString()!,
            Start: start,
            End: end,
            Description: args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null,
            Location: args.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.String ? locEl.GetString() : null,
            IsAllDay: isAllDay);

        var response = await _mediator.Send(request, ct);
        return JsonSerializer.SerializeToElement(new { event_id = response.EventId, status = "created" });
    }

    private DateTimeOffset ParseDateOnly(string raw)
    {
        var date = DateOnly.ParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }
}
