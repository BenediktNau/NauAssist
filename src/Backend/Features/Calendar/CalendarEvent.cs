namespace NauAssist.Backend.Features.Calendar;

public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location,
    bool IsAllDay = false,
    string? SeriesId = null)
{
    /// <summary>
    /// True, wenn dieses Event eine Instanz einer wiederkehrenden Serie ist.
    /// SeriesId trägt dann die Master-Event-ID des Providers.
    /// </summary>
    public bool IsSeriesInstance => SeriesId is not null;
}
