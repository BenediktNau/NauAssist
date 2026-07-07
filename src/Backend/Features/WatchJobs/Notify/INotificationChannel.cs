namespace NauAssist.Backend.Features.WatchJobs.Notify;

/// <summary>Was beim Feuern eines Watch-Jobs verschickt wird — kanalneutral.</summary>
public sealed record WatchNotification(string Title, string Body, string? Url, string? Tag);

/// <summary>
/// Ein Benachrichtigungskanal (webpush, pushover, …). Implementierungen werden über DI
/// als Collection eingesammelt; <see cref="Name"/> ist der Wire-Name, wie er in
/// <c>WatchJobNotify.Channels</c> steht.
/// </summary>
public interface INotificationChannel
{
    string Name { get; }

    /// <summary>Sendet; false, wenn der Kanal nicht konfiguriert ist oder nichts zugestellt wurde.</summary>
    Task<bool> SendAsync(WatchNotification notification, CancellationToken ct);
}
