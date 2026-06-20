using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Pollt pro Tick alle aktivierten WhatsApp-Accounts beim Sidecar (gepufferte
/// Nachrichten seit dem letzten Cursor) und liefert sie als <see cref="RawSignal"/>.
/// Strukturell parallel zu <c>MatrixObserver</c>/<c>ImapObserver</c>.
/// </summary>
public sealed class WhatsAppObserver : ISourceObserver
{
    public const string SourceKey = "whatsapp";

    private readonly SourceAccountRepository _accounts;
    private readonly SourceCursorRepository _cursors;
    private readonly IWhatsAppSidecarClient _client;
    private readonly WhatsAppOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<WhatsAppObserver> _logger;

    public WhatsAppObserver(
        SourceAccountRepository accounts,
        SourceCursorRepository cursors,
        IWhatsAppSidecarClient client,
        IOptions<WhatsAppOptions> options,
        Func<DateTimeOffset> clock,
        ILogger<WhatsAppObserver> logger)
    {
        _accounts = accounts;
        _cursors = cursors;
        _client = client;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public string Source => SourceKey;

    public async Task<IReadOnlyList<RawSignal>> PollAsync(CancellationToken ct)
    {
        var enabled = await _accounts.ListEnabledAsync(SourceKey, ct);
        if (enabled.Count == 0) return Array.Empty<RawSignal>();

        var all = new List<RawSignal>();
        foreach (var account in enabled)
        {
            try
            {
                if (account.Allowlist.Count == 0)
                {
                    _logger.LogDebug("WhatsApp-Account {Id} hat keine freigegebenen Chats — übersprungen.", account.Id);
                    continue;
                }

                var creds = WhatsAppCredentials.Parse(account.CredentialsJson);
                var since = await _cursors.GetAsync(SourceKey, account.Id, ct);
                var sinceSeq = long.TryParse(since, out var parsed) ? parsed : 0L;

                var page = await _client.GetMessagesAsync(
                    creds.SessionId, sinceSeq, _options.MessageBatchLimit, ct);

                if (string.IsNullOrEmpty(since))
                {
                    // Initial-Sync: Cursor-Baseline setzen (auch bei 0), alte Nachrichten verwerfen.
                    await _cursors.SetAsync(SourceKey, account.Id, page.Cursor.ToString(), _clock(), ct);
                    _logger.LogInformation(
                        "WhatsApp-Account {Id} initial sync — {Count} Nachrichten verworfen, Cursor={Cursor}.",
                        account.Id, page.Messages.Count, page.Cursor);
                    continue;
                }

                if (page.Cursor > sinceSeq)
                {
                    await _cursors.SetAsync(SourceKey, account.Id, page.Cursor.ToString(), _clock(), ct);
                }

                var allow = account.Allowlist
                    .Select(WhatsAppJid.Normalize)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var m in page.Messages)
                {
                    if (m.FromMe) continue;                  // eigene Nachrichten ignorieren (Loop-Schutz)
                    if (!allow.Contains(WhatsAppJid.Normalize(m.ChatId))) continue; // nur freigegebene Chats (JID-normalisiert)
                    if (string.IsNullOrWhiteSpace(m.Text)) continue;

                    var body = m.Text.Length > _options.MaxBodyChars
                        ? m.Text[.._options.MaxBodyChars] + "…"
                        : m.Text;

                    var metadata = new Dictionary<string, string>
                    {
                        ["chatId"] = m.ChatId,
                        ["messageId"] = m.MsgId,
                    };
                    if (!string.IsNullOrEmpty(m.Sender)) metadata["senderJid"] = m.Sender;
                    if (!string.IsNullOrEmpty(m.ChatName)) metadata["chatName"] = m.ChatName;

                    all.Add(new RawSignal(
                        Source: SourceKey,
                        SourceRef: m.ChatId,
                        Sender: string.IsNullOrEmpty(m.SenderName) ? m.Sender : m.SenderName,
                        Text: body,
                        ReceivedAt: m.Ts > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(m.Ts)
                            : _clock(),
                        Metadata: metadata));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WhatsApp-Account {Id} ({Name}) Poll fehlgeschlagen.", account.Id, account.DisplayName);
            }
        }

        return all;
    }
}
