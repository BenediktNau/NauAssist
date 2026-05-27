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

    /// <summary>Lese den Persona-Memory-Text (max 400 Zeichen). Leer wenn nichts gesetzt.</summary>
    Task<string> GetUserPersonaAsync(CancellationToken ct);

    /// <summary>Speichert den Persona-Memory-Text. Wird hart auf 400 Zeichen gekürzt.</summary>
    Task SetUserPersonaAsync(string text, CancellationToken ct);

    /// <summary>Liefert VAPID-Keys + Subject. Public/Private können leer sein, wenn noch nichts generiert wurde.</summary>
    Task<VapidSettings> GetVapidAsync(CancellationToken ct);

    /// <summary>Speichert VAPID-Public/Private/Subject — atomar.</summary>
    Task SetVapidAsync(VapidSettings vapid, CancellationToken ct);
}
