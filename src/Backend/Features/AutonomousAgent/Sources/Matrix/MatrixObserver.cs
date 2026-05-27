using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Matrix;

/// <summary>
/// Iteriert über alle enabled Matrix-Accounts, syncht sie inkrementell und
/// gibt empfangene Text-Nachrichten als <see cref="RawSignal"/> zurück.
/// </summary>
public sealed class MatrixObserver : ISourceObserver
{
    public const string SourceKey = "matrix";

    private readonly SourceAccountRepository _accounts;
    private readonly SourceCursorRepository _cursors;
    private readonly MatrixClient _client;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<MatrixObserver> _logger;

    public MatrixObserver(
        SourceAccountRepository accounts,
        SourceCursorRepository cursors,
        MatrixClient client,
        Func<DateTimeOffset> clock,
        ILogger<MatrixObserver> logger)
    {
        _accounts = accounts;
        _cursors = cursors;
        _client = client;
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
                var creds = MatrixCredentials.Parse(account.CredentialsJson);
                if (account.Allowlist.Count == 0)
                {
                    _logger.LogDebug("Matrix-Account {Id} hat keine erlaubten Räume — übersprungen.", account.Id);
                    continue;
                }

                var since = await _cursors.GetAsync(SourceKey, account.Id, ct);
                var sync = await _client.SyncAsync(creds, since, account.Allowlist, ct);

                if (!string.IsNullOrEmpty(sync.NextBatch))
                {
                    await _cursors.SetAsync(SourceKey, account.Id, sync.NextBatch, _clock(), ct);
                }

                if (string.IsNullOrEmpty(since))
                {
                    // Initial-Sync: nur Cursor setzen, alte Nachrichten nicht klassifizieren.
                    _logger.LogInformation(
                        "Matrix-Account {Id} initial sync — {Count} Nachrichten verworfen, Cursor gesetzt.",
                        account.Id, sync.Messages.Count);
                    continue;
                }

                foreach (var m in sync.Messages)
                {
                    // SourceRef = nur Raum-ID, damit Thread-Awareness im selben Raum greift.
                    // Event-ID wandert später separat mit, wenn wir Reply-Threading machen.
                    all.Add(new RawSignal(
                        Source: SourceKey,
                        SourceRef: m.RoomId,
                        Sender: m.Sender,
                        Text: m.Body,
                        ReceivedAt: m.Timestamp));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Matrix-Account {Id} ({Name}) Sync fehlgeschlagen.", account.Id, account.DisplayName);
            }
        }

        return all;
    }
}
