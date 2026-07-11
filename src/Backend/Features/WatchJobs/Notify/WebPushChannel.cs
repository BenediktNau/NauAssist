using NauAssist.Backend.Features.AutonomousAgent.Push;

namespace NauAssist.Backend.Features.WatchJobs.Notify;

/// <summary>Adapter: der bestehende <see cref="WebPushSender"/> als Watch-Job-Kanal "webpush".</summary>
public sealed class WebPushChannel : INotificationChannel
{
    private readonly WebPushSender _push;

    public WebPushChannel(WebPushSender push) => _push = push;

    public string Name => "webpush";

    public async Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
        => await _push.BroadcastAsync(
            new PushNotificationPayload(notification.Title, notification.Body, notification.Url, notification.Tag),
            ct) > 0;
}
