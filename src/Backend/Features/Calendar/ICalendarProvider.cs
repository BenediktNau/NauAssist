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

    /// <summary>
    /// Löscht einen Termin anhand der Provider-Event-ID.
    /// Bei <paramref name="scope"/>=<see cref="EventScope.Series"/> wird die ganze
    /// Serie gelöscht (sofern <paramref name="eventId"/> eine Serien-Instanz ist).
    /// </summary>
    Task DeleteEventAsync(string eventId, EventScope scope, CancellationToken ct);

    /// <summary>
    /// Aktualisiert einzelne Felder eines bestehenden Termins. Nicht gesetzte
    /// Felder in <paramref name="update"/> bleiben unverändert.
    /// Bei <paramref name="scope"/>=<see cref="EventScope.Series"/> wird das
    /// Master-Event der Serie aktualisiert.
    /// </summary>
    Task UpdateEventAsync(string eventId, EventUpdate update, EventScope scope, CancellationToken ct);
}
