using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.WatchJobs.Notify;

/// <summary>
/// Kanal "pushover": sendet über die Pushover-Message-API (https://pushover.net/api).
/// Ohne konfigurierte Zugangsdaten wird still übersprungen (false) — der Kanal ist optional.
/// </summary>
public sealed class PushoverChannel : INotificationChannel
{
    public const string HttpClientName = "Pushover";
    private const string MessagesUrl = "https://api.pushover.net/1/messages.json";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IAppSettingsRepository _settings;
    private readonly ILogger<PushoverChannel> _logger;

    public PushoverChannel(
        IHttpClientFactory httpFactory,
        IAppSettingsRepository settings,
        ILogger<PushoverChannel> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    public string Name => "pushover";

    public async Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
    {
        var s = await _settings.GetPushoverAsync(ct);
        if (!s.IsConfigured)
        {
            _logger.LogInformation("Pushover nicht konfiguriert — Kanal wird übersprungen.");
            return false;
        }

        var fields = new Dictionary<string, string>
        {
            ["token"] = s.Token,
            ["user"] = s.UserKey,
            ["title"] = notification.Title,
            ["message"] = notification.Body,
        };
        // Pushover braucht absolute http(s)-URLs; PWA-interne Pfade wie "/chat" sind dort nutzlos
        // und würden von Uri.TryCreate(..., UriKind.Absolute) unter Unix fälschlich als "file:///chat" akzeptiert.
        if (Uri.TryCreate(notification.Url, UriKind.Absolute, out var absoluteUrl)
            && (absoluteUrl.Scheme == Uri.UriSchemeHttp || absoluteUrl.Scheme == Uri.UriSchemeHttps))
        {
            fields["url"] = notification.Url!;
            fields["url_title"] = "In NauAssist öffnen";
        }

        var client = _httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(MessagesUrl, new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Pushover-Send fehlgeschlagen: HTTP {Status} — {Body}",
                (int)response.StatusCode, Truncate(body, 200));
            return false;
        }

        return true;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
