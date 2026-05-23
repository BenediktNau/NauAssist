namespace NauAssist.Backend.Features.Settings;

public sealed record CalendarUserSettings(
    string CalendarId,
    TimeOnly WorkingHoursStart,
    TimeOnly WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays);
