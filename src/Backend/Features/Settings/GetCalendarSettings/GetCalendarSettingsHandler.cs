using Google.Apis.Auth.OAuth2.Responses;
using Mediator;
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Features.Settings.GetCalendarSettings;

public sealed class GetCalendarSettingsHandler
    : IRequestHandler<GetCalendarSettingsRequest, GetCalendarSettingsResult>
{
    private readonly IAppSettingsRepository _settings;
    private readonly SqliteDataStore _dataStore;

    public GetCalendarSettingsHandler(
        IAppSettingsRepository settings,
        SqliteDataStore dataStore)
    {
        _settings = settings;
        _dataStore = dataStore;
    }

    public async ValueTask<GetCalendarSettingsResult> Handle(
        GetCalendarSettingsRequest request, CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var creds = await _settings.GetGoogleCredentialsAsync(ct);

        var token = await _dataStore.GetAsync<TokenResponse>("nauassist-default");
        var isConnected = token is not null;

        return new GetCalendarSettingsResult(
            CalendarId: cal.CalendarId,
            WorkingHoursStart: cal.WorkingHoursStart.ToString("HH:mm"),
            WorkingHoursEnd: cal.WorkingHoursEnd.ToString("HH:mm"),
            DefaultDurationMinutes: cal.DefaultDurationMinutes,
            SearchHorizonDays: cal.SearchHorizonDays,
            HasGoogleCredentials: creds is not null,
            IsConnected: isConnected);
    }
}
