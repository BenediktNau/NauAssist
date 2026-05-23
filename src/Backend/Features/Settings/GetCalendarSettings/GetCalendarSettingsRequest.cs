using Mediator;

namespace NauAssist.Backend.Features.Settings.GetCalendarSettings;

public sealed record GetCalendarSettingsRequest : IRequest<GetCalendarSettingsResult>;

public sealed record GetCalendarSettingsResult(
    string CalendarId,
    string WorkingHoursStart,
    string WorkingHoursEnd,
    int DefaultDurationMinutes,
    int SearchHorizonDays,
    bool HasGoogleCredentials,
    bool IsConnected);
