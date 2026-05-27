using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Imap;

/// <summary>
/// Dünner Wrapper um MailKit fürs Folder-Listing. Connect/Disconnect je Aufruf —
/// kein Connection-Pooling für den 20-min-Tick nötig.
/// </summary>
public sealed class ImapClient
{
    public async Task<IReadOnlyList<string>> ListFoldersAsync(ImapCredentials creds, CancellationToken ct)
    {
        using var client = new MailKit.Net.Imap.ImapClient();
        await client.ConnectAsync(
            creds.ImapHost,
            creds.ImapPort,
            creds.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            ct);
        await client.AuthenticateAsync(creds.Username, creds.Password, ct);

        var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], cancellationToken: ct);
        var result = folders
            .Where(f => (f.Attributes & FolderAttributes.NonExistent) == 0)
            .Select(f => f.FullName)
            .ToList();

        await client.DisconnectAsync(true, ct);
        return result;
    }
}
