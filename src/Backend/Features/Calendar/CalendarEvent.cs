namespace NauAssist.Backend.Features.Calendar;

public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location);
