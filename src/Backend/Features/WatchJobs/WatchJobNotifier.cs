using System.Text;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.WatchJobs.Notify;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Benachrichtigt beim Feuern eines Watch-Jobs: proaktive Assistant-Nachricht in der
/// Chat-History (Deep-Link-Ziel der Pushes) plus alle in der Job-Spec gewünschten Kanäle.
/// Unbekannte Kanäle werden geloggt und ignoriert; ein fehlschlagender Kanal stoppt die anderen nicht.
/// </summary>
public sealed class WatchJobNotifier
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly MessageRepository _messages;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<WatchJobNotifier> _logger;

    public WatchJobNotifier(
        IEnumerable<INotificationChannel> channels,
        MessageRepository messages,
        Func<DateTimeOffset> clock,
        ILogger<WatchJobNotifier> logger)
    {
        _channels = channels.ToList();
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

        var notification = new WatchNotification(
            Title: job.Title,
            Body: Truncate(result.Summary, 200),
            Url: "/chat",
            Tag: $"watch-{job.Id}");

        foreach (var name in job.Notify.Channels)
        {
            var channel = _channels.FirstOrDefault(
                c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (channel is null)
            {
                _logger.LogInformation("WatchJob {Id}: unbekannter Kanal '{Channel}' — übersprungen.", job.Id, name);
                continue;
            }

            try
            {
                await channel.SendAsync(notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "WatchJob {Id}: Kanal '{Channel}' fehlgeschlagen.", job.Id, name);
            }
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
