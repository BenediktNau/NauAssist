using Google.Apis.Auth.OAuth2.Responses;
using Mediator;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Features.Infrastructure.Auth;

namespace NauAssist.Backend.Features.Settings.GetCalendarSettings;

public sealed class GetCalendarSettingsHandler
    : IRequestHandler<GetCalendarSettingsRequest, GetCalendarSettingsResult>
{
    private readonly IAppSettingsRepository _settings;
    private readonly SqliteDataStore _dataStore;
    private readonly IUserContext _user;

    public GetCalendarSettingsHandler(
        IAppSettingsRepository settings,
        SqliteDataStore dataStore,
        IUserContext user)
    {
        _settings = settings;
        _dataStore = dataStore;
        _user = user;
    }

    public async ValueTask<GetCalendarSettingsResult> Handle(
        GetCalendarSettingsRequest request, CancellationToken ct)
    {
        var cal = await _settings.GetCalendarAsync(ct);
        var creds = await _settings.GetGoogleCredentialsAsync(ct);

        var token = await _dataStore.GetAsync<TokenResponse>(_user.UserId);
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
