using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleCalendarProvider : ICalendarProvider
{
    private readonly GoogleAuthService _auth;
    private readonly CalendarOptions _options;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(
        GoogleAuthService auth,
        IOptions<CalendarOptions> options,
        ILogger<GoogleCalendarProvider> logger)
    {
        _auth = auth;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var service = await CreateServiceAsync(ct);
        var req = service.Events.List(_options.GoogleCalendarId);
        req.TimeMinDateTimeOffset = from;
        req.TimeMaxDateTimeOffset = to;
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        req.MaxResults = 250;

        var resp = await req.ExecuteAsync(ct);

        return resp.Items
            .Select(e => GoogleEventMapper.Map(e, TimeZoneInfo.Utc))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    public async Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct)
    {
        var service = await CreateServiceAsync(ct);
        var googleEvent = new Event
        {
            Summary = ev.Title,
            Description = ev.Description,
            Location = ev.Location,
            Start = new EventDateTime { DateTimeDateTimeOffset = ev.Start },
            End = new EventDateTime { DateTimeDateTimeOffset = ev.End },
        };

        var created = await service.Events.Insert(googleEvent, _options.GoogleCalendarId).ExecuteAsync(ct);
        _logger.LogInformation("Google-Event {EventId} angelegt für '{Title}' am {Start}.", created.Id, ev.Title, ev.Start);
        return created.Id;
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
