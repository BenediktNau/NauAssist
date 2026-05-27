using System.Text.Json;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Settings;
using WebPush;

namespace NauAssist.Backend.Features.AutonomousAgent.Push;

/// <summary>
/// Sendet Push-Notifications an alle aktiven Subscriptions.
/// Räumt 410-Gone-/404-Subscriptions automatisch aus der DB.
/// </summary>
public sealed class WebPushSender
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PushSubscriptionRepository _subs;
    private readonly IAppSettingsRepository _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(
        PushSubscriptionRepository subs,
        IAppSettingsRepository settings,
        Func<DateTimeOffset> clock,
        ILogger<WebPushSender> logger)
    {
        _subs = subs;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> BroadcastAsync(PushNotificationPayload payload, CancellationToken ct)
    {
        var vapid = await _settings.GetVapidAsync(ct);
        if (!vapid.IsConfigured)
        {
            _logger.LogWarning("VAPID nicht konfiguriert — Push wird übersprungen.");
            return 0;
        }

        var subs = await _subs.ListAsync(ct);
        if (subs.Count == 0) return 0;

        var details = new VapidDetails(vapid.Subject, vapid.PublicKey, vapid.PrivateKey);
        var client = new WebPushClient();
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var sent = 0;

        foreach (var sub in subs)
        {
            var target = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
            try
            {
                await client.SendNotificationAsync(target, json, details, ct);
                await _subs.TouchAsync(sub.Id, _clock(), ct);
                sent++;
            }
            catch (WebPushException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.Gone
                || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "Push-Endpoint {Endpoint} ist abgelaufen ({Status}) — Subscription wird entfernt.",
                    sub.Endpoint, (int)ex.StatusCode);
                await _subs.DeleteAsync(sub.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push an Subscription {Id} fehlgeschlagen.", sub.Id);
            }
        }

        return sent;
    }
}

public sealed record PushNotificationPayload(
    string Title,
    string Body,
    string? Url,
    string? Tag);
