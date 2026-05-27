using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Imap;

/// <summary>
/// Pollt pro Tick alle aktivierten IMAP-Accounts. Cursor pro Folder ist
/// (UIDVALIDITY, lastUid). Ändert sich UIDVALIDITY, wird der Folder
/// als initial-sync behandelt — sonst Suche per UID &gt; lastUid.
/// </summary>
public sealed class ImapObserver : ISourceObserver
{
    public const string SourceKey = "imap";
    private const int MaxBodyChars = 2000;

    private readonly SourceAccountRepository _accounts;
    private readonly SourceCursorRepository _cursors;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<ImapObserver> _logger;

    public ImapObserver(
        SourceAccountRepository accounts,
        SourceCursorRepository cursors,
        Func<DateTimeOffset> clock,
        ILogger<ImapObserver> logger)
    {
        _accounts = accounts;
        _cursors = cursors;
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
                    _logger.LogDebug("IMAP-Account {Id} hat keine freigegebenen Ordner — übersprungen.", account.Id);
                    continue;
                }

                var creds = ImapCredentials.Parse(account.CredentialsJson);
                var cursor = await LoadCursorAsync(account.Id, ct);

                using var client = new MailKit.Net.Imap.ImapClient();
                await client.ConnectAsync(
                    creds.ImapHost,
                    creds.ImapPort,
                    creds.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
                    ct);
                await client.AuthenticateAsync(creds.Username, creds.Password, ct);

                foreach (var folderName in account.Allowlist)
                {
                    try
                    {
                        await PollFolderAsync(client, folderName, cursor, all, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "IMAP-Folder {Folder} (Account {Id}) konnte nicht gepollt werden.",
                            folderName, account.Id);
                    }
                }

                await client.DisconnectAsync(true, ct);
                await SaveCursorAsync(account.Id, cursor, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IMAP-Account {Id} ({Name}) Sync fehlgeschlagen.", account.Id, account.DisplayName);
            }
        }

        return all;
    }

    private async Task PollFolderAsync(
        MailKit.Net.Imap.ImapClient client,
        string folderName,
        Dictionary<string, FolderCursor> cursor,
        List<RawSignal> output,
        CancellationToken ct)
    {
        var folder = await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var uidValidity = folder.UidValidity;
        var hasCursor = cursor.TryGetValue(folderName, out var prev);
        var isInitialOrReset = !hasCursor || prev!.UidValidity != uidValidity;

        if (isInitialOrReset)
        {
            // Initial-Sync oder UIDVALIDITY-Wechsel: nur Cursor setzen, alte Mails überspringen.
            var nextUid = folder.UidNext?.Id ?? 1;
            cursor[folderName] = new FolderCursor(uidValidity, nextUid - 1);
            _logger.LogInformation(
                "IMAP-Folder {Folder} initial sync — Cursor auf UID={Uid} gesetzt, alte Mails verworfen.",
                folderName, nextUid - 1);
            return;
        }

        var lastUid = prev!.LastUid;
        var search = SearchQuery.Uids(new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue));
        var hits = await folder.SearchAsync(search, ct);
        if (hits.Count == 0) return;

        foreach (var uid in hits)
        {
            try
            {
                var message = await folder.GetMessageAsync(uid, ct);
                var signal = BuildSignal(message, folderName);
                if (signal is not null)
                {
                    output.Add(signal);
                }
                if (uid.Id > cursor[folderName].LastUid)
                {
                    cursor[folderName] = new FolderCursor(uidValidity, uid.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IMAP: Mail UID={Uid} in {Folder} konnte nicht gelesen werden.", uid, folderName);
            }
        }
    }

    private static RawSignal? BuildSignal(MimeMessage message, string folderName)
    {
        var fromMailbox = message.From.Mailboxes.FirstOrDefault();
        if (fromMailbox is null) return null;

        var bodyRaw = message.TextBody;
        if (string.IsNullOrWhiteSpace(bodyRaw))
        {
            // Fallback auf HTML → Plain (sehr grob, reicht für Cheap-Filter).
            bodyRaw = StripHtml(message.HtmlBody ?? "");
        }
        if (string.IsNullOrWhiteSpace(bodyRaw)) return null;

        var body = bodyRaw.Length > MaxBodyChars
            ? bodyRaw[..MaxBodyChars] + "…"
            : bodyRaw;

        var metadata = new Dictionary<string, string>
        {
            ["messageId"] = message.MessageId ?? "",
            ["from"] = fromMailbox.Address,
            ["fromName"] = fromMailbox.Name ?? "",
            ["subject"] = message.Subject ?? "",
            ["folder"] = folderName,
        };
        if (!string.IsNullOrEmpty(message.InReplyTo))
        {
            metadata["inReplyTo"] = message.InReplyTo;
        }
        if (message.References != null && message.References.Count > 0)
        {
            metadata["references"] = string.Join(" ", message.References);
        }

        // Thread-Key = From-Address (Lower-Case): mehrere Mails vom selben Absender
        // innerhalb 24 h werden vom Reasoner als ein Thread behandelt.
        var threadKey = fromMailbox.Address.ToLowerInvariant();

        return new RawSignal(
            Source: SourceKey,
            SourceRef: threadKey,
            Sender: string.IsNullOrEmpty(fromMailbox.Name) ? fromMailbox.Address : fromMailbox.Name,
            Text: BuildText(message.Subject, body),
            ReceivedAt: message.Date.ToUniversalTime(),
            Metadata: metadata);
    }

    private static string BuildText(string? subject, string body)
    {
        if (string.IsNullOrWhiteSpace(subject)) return body;
        return $"Subject: {subject}\n\n{body}";
    }

    private static string StripHtml(string html)
    {
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var collapsed = System.Text.RegularExpressions.Regex.Replace(noTags, "\\s+", " ");
        return collapsed.Trim();
    }

    // --- Cursor JSON helpers ---

    private static readonly JsonSerializerOptions CursorJsonOpts = new();

    private async Task<Dictionary<string, FolderCursor>> LoadCursorAsync(long accountId, CancellationToken ct)
    {
        var raw = await _cursors.GetAsync(SourceKey, accountId, ct);
        if (string.IsNullOrEmpty(raw)) return new Dictionary<string, FolderCursor>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FolderCursor>>(raw, CursorJsonOpts)
                ?? new Dictionary<string, FolderCursor>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "IMAP-Cursor für Account {Id} konnte nicht geparst werden — wird zurückgesetzt.", accountId);
            return new Dictionary<string, FolderCursor>();
        }
    }

    private async Task SaveCursorAsync(long accountId, Dictionary<string, FolderCursor> cursor, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(cursor, CursorJsonOpts);
        await _cursors.SetAsync(SourceKey, accountId, json, _clock(), ct);
    }

    private sealed record FolderCursor(uint UidValidity, uint LastUid);
}
