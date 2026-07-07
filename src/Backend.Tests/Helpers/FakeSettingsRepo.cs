using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Tests.Helpers;

public sealed class FakeSettingsRepo : IAppSettingsRepository
{
    private readonly CalendarUserSettings _calendar;

    public FakeSettingsRepo(int searchHorizon = 14)
    {
        _calendar = new CalendarUserSettings(
            "primary", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, searchHorizon);
    }

    public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
        Task.FromResult(_calendar);
    public Task SetCalendarAsync(CalendarUserSettings s, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<LlmSettings> GetLlmAsync(CancellationToken ct) =>
        Task.FromResult(new LlmSettings("gemma4:26b"));
    public Task SetLlmAsync(LlmSettings s, CancellationToken ct) => Task.CompletedTask;
    public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
        Task.FromResult(new OllamaUserSettings("http://localhost:11434", null, 16384, 0.3));
    public Task SetOllamaAsync(OllamaUserSettings s, CancellationToken ct) => Task.CompletedTask;
    public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
        Task.FromResult<GoogleCredentials?>(null);
    public Task SetGoogleCredentialsAsync(GoogleCredentials c, CancellationToken ct) =>
        Task.CompletedTask;
    public Task<string> GetUserPersonaAsync(CancellationToken ct) => Task.FromResult(string.Empty);
    public Task SetUserPersonaAsync(string text, CancellationToken ct) => Task.CompletedTask;
    public Task<VapidSettings> GetVapidAsync(CancellationToken ct) =>
        Task.FromResult(new VapidSettings("", "", "mailto:test@example.org"));
    public Task SetVapidAsync(VapidSettings v, CancellationToken ct) => Task.CompletedTask;

    public PushoverSettings Pushover { get; set; } = new("", "");

    public Task<PushoverSettings> GetPushoverAsync(CancellationToken ct) => Task.FromResult(Pushover);

    public Task SetPushoverAsync(PushoverSettings settings, CancellationToken ct)
    {
        Pushover = settings;
        return Task.CompletedTask;
    }
}
