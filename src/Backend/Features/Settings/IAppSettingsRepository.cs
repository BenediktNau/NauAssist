namespace NauAssist.Backend.Features.Settings;

public interface IAppSettingsRepository
{
    Task<LlmSettings> GetLlmAsync(CancellationToken ct);
    Task SetLlmAsync(LlmSettings settings, CancellationToken ct);

    Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct);
    Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct);

    Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct);
    Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct);

    /// <summary>Null, wenn ClientId leer ist; sonst gefülltes Record.</summary>
    Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct);

    /// <summary>Speichert Credentials und löscht in derselben Transaktion alle google_oauth-Einträge.</summary>
    Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct);
}
