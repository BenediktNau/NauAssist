using System.Text;
using NauAssist.Backend.Features.AutonomousAgent.Push;
using NauAssist.Backend.Features.Chat;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Benachrichtigt beim Feuern eines Watch-Jobs: Web-Push (falls Kanal gewünscht) plus eine
/// proaktive Assistant-Nachricht in der Chat-History (Deep-Link-Ziel des Push). Phase 1 kennt
/// nur den Kanal <c>webpush</c>; unbekannte Kanäle (z.B. <c>pushover</c>, Phase 2) werden
/// geloggt und ignoriert.
/// </summary>
public sealed class WatchJobNotifier
{
    private const string WebPushChannel = "webpush";

    private readonly WebPushSender _push;
    private readonly MessageRepository _messages;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<WatchJobNotifier> _logger;

    public WatchJobNotifier(
        WebPushSender push,
        MessageRepository messages,
        Func<DateTimeOffset> clock,
        ILogger<WatchJobNotifier> logger)
    {
        _push = push;
        _messages = messages;
        _clock = clock;
        _logger = logger;
    }

    public async Task NotifyAsync(WatchJob job, WatchJudgeResult result, CancellationToken ct)
    {
        var body = BuildBody(job, result);

        // Proaktive Chat-Nachricht — taucht in der History auf und ist das Ziel des Push-Deep-Links.
        await _messages.AddAsync(
            new Message(
                Id: 0,
                SessionId: ChatSessions.Default,
                Role: MessageRole.Assistant,
                Content: body,
                ProposalsJson: null,
                Incomplete: false,
                CreatedAt: _clock()),
            ct);

        foreach (var channel in job.Notify.Channels)
        {
            if (!string.Equals(channel, WebPushChannel, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "WatchJob {Id}: Kanal '{Channel}' wird in Phase 1 nicht unterstützt — übersprungen.",
                    job.Id, channel);
            }
        }

        if (job.Notify.Channels.Any(c => string.Equals(c, WebPushChannel, StringComparison.OrdinalIgnoreCase)))
        {
            await _push.BroadcastAsync(
                new PushNotificationPayload(
                    Title: job.Title,
                    Body: Truncate(result.Summary, 200),
                    Url: "/chat",
                    Tag: $"watch-{job.Id}"),
                ct);
        }
    }

    private static string BuildBody(WatchJob job, WatchJudgeResult result)
    {
        var sb = new StringBuilder();
        sb.Append("🟢 ").Append(job.Title).Append(": ").Append(result.Summary);

        foreach (var e in result.Evidence)
        {
            sb.Append("\n• ");
            sb.Append(string.IsNullOrWhiteSpace(e.Shop) ? "Treffer" : e.Shop);
            if (!string.IsNullOrWhiteSpace(e.Price)) sb.Append(" — ").Append(e.Price);
            if (!string.IsNullOrWhiteSpace(e.Url)) sb.Append(" → ").Append(e.Url);
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
