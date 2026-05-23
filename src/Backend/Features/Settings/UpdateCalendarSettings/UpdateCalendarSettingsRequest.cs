using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateCalendarSettings;

public sealed record UpdateCalendarSettingsRequest(
    string CalendarId,
    string WorkingHoursStart,
    string WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays,
    string? GoogleClientId,
    string? GoogleClientSecret) : IRequest<UpdateCalendarSettingsResult>;

public sealed record UpdateCalendarSettingsResult(bool Ok, string? Error);
