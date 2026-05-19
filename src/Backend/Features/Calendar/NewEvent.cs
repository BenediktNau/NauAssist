namespace NauAssist.Backend.Features.Calendar;

public sealed record NewEvent(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location);
