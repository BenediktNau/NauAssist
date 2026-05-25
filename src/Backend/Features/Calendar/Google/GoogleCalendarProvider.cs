using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleCalendarProvider : ICalendarProvider
{
    private readonly GoogleAuthService _auth;
    private readonly IAppSettingsRepository _settings;
    private readonly TimeZoneInfo _zone;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(
        GoogleAuthService auth,
        IAppSettingsRepository settings,
        TimeZoneInfo zone,
        ILogger<GoogleCalendarProvider> logger)
    {
        _auth = auth;
        _settings = settings;
        _zone = zone;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var service = await CreateServiceAsync(ct);
        var req = service.Events.List(cal.CalendarId);
        req.TimeMinDateTimeOffset = from;
        req.TimeMaxDateTimeOffset = to;
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        req.MaxResults = 250;

        var resp = await req.ExecuteAsync(ct);

        return resp.Items
            .Select(e => GoogleEventMapper.Map(e, _zone))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    public async Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var service = await CreateServiceAsync(ct);
        var googleEvent = new Event
        {
            Summary = ev.Title,
            Description = ev.Description,
            Location = ev.Location,
        };

        if (ev.IsAllDay)
        {
            googleEvent.Start = new EventDateTime { Date = ev.Start.ToString("yyyy-MM-dd") };
            googleEvent.End   = new EventDateTime { Date = ev.End.ToString("yyyy-MM-dd") };
        }
        else
        {
            googleEvent.Start = new EventDateTime { DateTimeDateTimeOffset = ev.Start };
            googleEvent.End   = new EventDateTime { DateTimeDateTimeOffset = ev.End };
        }

        var created = await service.Events.Insert(googleEvent, cal.CalendarId).ExecuteAsync(ct);
        _logger.LogInformation(
            "Google-Event {EventId} angelegt für '{Title}' am {Start} (AllDay={AllDay}).",
            created.Id, ev.Title, ev.Start, ev.IsAllDay);
        return created.Id;
    }

    public async Task DeleteEventAsync(string eventId, EventScope scope, CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var service = await CreateServiceAsync(ct);

        var targetId = await ResolveTargetIdAsync(service, cal.CalendarId, eventId, scope, ct);
        await service.Events.Delete(cal.CalendarId, targetId).ExecuteAsync(ct);
        _logger.LogInformation(
            "Google-Event {EventId} gelöscht (scope={Scope}, target={TargetId}).",
            eventId, scope, targetId);
    }

    public async Task UpdateEventAsync(string eventId, EventUpdate update, EventScope scope, CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var service = await CreateServiceAsync(ct);

        var targetId = await ResolveTargetIdAsync(service, cal.CalendarId, eventId, scope, ct);
        var existing = await service.Events.Get(cal.CalendarId, targetId).ExecuteAsync(ct);

        if (update.Title is not null)
        {
            existing.Summary = update.Title;
        }
        if (update.Description is not null)
        {
            existing.Description = update.Description;
        }
        if (update.Location is not null)
        {
            existing.Location = update.Location;
        }

        // is_all_day kann den Modus umschalten; deshalb erst bestimmen, in welchem
        // Modus die neuen Start/End-Werte zu schreiben sind.
        var existingIsAllDay = !string.IsNullOrEmpty(existing.Start?.Date);
        var targetIsAllDay = update.IsAllDay ?? existingIsAllDay;

        if (update.Start is not null || update.End is not null || update.IsAllDay is not null)
        {
            existing.Start ??= new EventDateTime();
            existing.End ??= new EventDateTime();

            if (targetIsAllDay)
            {
                if (update.Start is { } s)
                {
                    existing.Start = new EventDateTime { Date = s.ToString("yyyy-MM-dd") };
                }
                else if (update.IsAllDay == true && !existingIsAllDay && existing.Start.DateTimeDateTimeOffset is { } sDt)
                {
                    existing.Start = new EventDateTime { Date = sDt.ToString("yyyy-MM-dd") };
                }
                if (update.End is { } e)
                {
                    existing.End = new EventDateTime { Date = e.ToString("yyyy-MM-dd") };
                }
                else if (update.IsAllDay == true && !existingIsAllDay && existing.End.DateTimeDateTimeOffset is { } eDt)
                {
                    existing.End = new EventDateTime { Date = eDt.ToString("yyyy-MM-dd") };
                }
            }
            else
            {
                if (update.Start is { } s)
                {
                    existing.Start = new EventDateTime { DateTimeDateTimeOffset = s };
                }
                else if (update.IsAllDay == false && existingIsAllDay && !string.IsNullOrEmpty(existing.Start.Date))
                {
                    // Date → DateTime: Mitternacht in lokaler Zeitzone
                    var d = System.Globalization.CultureInfo.InvariantCulture;
                    var dateOnly = DateOnly.ParseExact(existing.Start.Date, "yyyy-MM-dd", d);
                    var local = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                    existing.Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(local, _zone.GetUtcOffset(local)) };
                }
                if (update.End is { } e)
                {
                    existing.End = new EventDateTime { DateTimeDateTimeOffset = e };
                }
                else if (update.IsAllDay == false && existingIsAllDay && !string.IsNullOrEmpty(existing.End.Date))
                {
                    var d = System.Globalization.CultureInfo.InvariantCulture;
                    var dateOnly = DateOnly.ParseExact(existing.End.Date, "yyyy-MM-dd", d);
                    var local = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                    existing.End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(local, _zone.GetUtcOffset(local)) };
                }
            }
        }

        await service.Events.Update(existing, cal.CalendarId, targetId).ExecuteAsync(ct);
        _logger.LogInformation(
            "Google-Event {EventId} aktualisiert (scope={Scope}, target={TargetId}, AllDay={AllDay}).",
            eventId, scope, targetId, targetIsAllDay);
    }

    /// <summary>
    /// Liefert die effektiv zu modifizierende Event-ID. Bei <c>Series</c> wird
    /// die Master-ID über <c>recurringEventId</c> der Instanz aufgelöst; ist das
    /// Event keine Serien-Instanz, bleibt die ID unverändert (Series-Scope ist
    /// dann ein No-Op-Hinweis und behandelt das Event als Einzeltermin).
    /// </summary>
    private static async Task<string> ResolveTargetIdAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        EventScope scope,
        CancellationToken ct)
    {
        if (scope != EventScope.Series)
        {
            return eventId;
        }

        var instance = await service.Events.Get(calendarId, eventId).ExecuteAsync(ct);
        return string.IsNullOrEmpty(instance.RecurringEventId) ? eventId : instance.RecurringEventId;
    }

    private async Task<CalendarService> CreateServiceAsync(CancellationToken ct)
    {
        var credential = await _auth.GetCredentialAsync(ct);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "NauAssist",
        });
    }
}
