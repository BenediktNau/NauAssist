using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateCalendarSettings;

public sealed class UpdateCalendarSettingsHandler
    : IRequestHandler<UpdateCalendarSettingsRequest, UpdateCalendarSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public UpdateCalendarSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<UpdateCalendarSettingsResult> Handle(
        UpdateCalendarSettingsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CalendarId))
        {
            return new(false, "CalendarId darf nicht leer sein.");
        }

        if (!TimeOnly.TryParseExact(request.WorkingHoursStart, "HH:mm", out var start))
        {
            return new(false, "WorkingHoursStart muss im Format HH:mm sein.");
        }
        if (!TimeOnly.TryParseExact(request.WorkingHoursEnd, "HH:mm", out var end))
        {
            return new(false, "WorkingHoursEnd muss im Format HH:mm sein.");
        }
        if (end <= start)
        {
            return new(false, "Ende der Arbeitszeit muss nach dem Anfang liegen.");
        }

        if (request.DefaultDurationMinutes <= 0 || request.DefaultDurationMinutes > 24 * 60)
        {
            return new(false, "DefaultDurationMinutes muss zwischen 1 und 1440 liegen.");
        }
        if (request.SearchHorizonDays <= 0 || request.SearchHorizonDays > 365)
        {
            return new(false, "SearchHorizonDays muss zwischen 1 und 365 liegen.");
        }

        await _settings.SetCalendarAsync(
            new CalendarUserSettings(
                request.CalendarId,
                start, end,
                request.DefaultDurationMinutes,
                request.SearchHorizonDays),
            ct);

        var hasNewId = request.GoogleClientId is not null;
        var hasNewSecret = request.GoogleClientSecret is not null;

        if (hasNewId || hasNewSecret)
        {
            var existing = await _settings.GetGoogleCredentialsAsync(ct);
            var newId = request.GoogleClientId ?? existing?.ClientId ?? "";
            var newSecret = request.GoogleClientSecret switch
            {
                null => existing?.ClientSecret ?? "",
                ""   => "",
                var s => s,
            };

            var clearAll = string.IsNullOrEmpty(newId) || string.IsNullOrEmpty(newSecret);
            await _settings.SetGoogleCredentialsAsync(
                new GoogleCredentials(
                    ClientId: clearAll ? "" : newId,
                    ClientSecret: clearAll ? "" : newSecret),
                ct);
        }

        return new(true, null);
    }
}
