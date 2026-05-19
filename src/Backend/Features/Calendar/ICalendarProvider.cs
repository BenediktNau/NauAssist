namespace NauAssist.Backend.Features.Calendar;

public interface ICalendarProvider
{
    /// <summary>
    /// Liefert alle Events, die mit [from, to) überschneiden.
    /// Sortierung: aufsteigend nach Start.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);

    /// <summary>
    /// Legt einen neuen Termin an. Gibt die vom Provider vergebene Event-ID zurück.
    /// </summary>
    Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct);
}
