using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Imap;

public sealed class SmtpSender : ISourceSender
{
    public string Source => ImapObserver.SourceKey;

    public async Task SendAsync(
        SourceAccount account,
        string targetRef,
        string body,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        var creds = ImapCredentials.Parse(account.CredentialsJson);

        var toAddress = metadata?.GetValueOrDefault("from")
                        ?? throw new InvalidOperationException("Reply-Metadaten fehlen 'from'-Adresse — kann nicht antworten.");
        var subject = metadata?.GetValueOrDefault("subject") ?? "";
        var inReplyTo = metadata?.GetValueOrDefault("messageId");
        var references = metadata?.GetValueOrDefault("references");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(creds.FromName) ? creds.EffectiveFromAddress : creds.FromName,
            creds.EffectiveFromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = PrefixSubject(subject);
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            message.InReplyTo = inReplyTo.Trim('<', '>', ' ');
            // References-Header: existierende Chain + Original Message-ID (gemäß RFC 5322).
            var chain = string.IsNullOrEmpty(references)
                ? inReplyTo
                : $"{references} {inReplyTo}";
            foreach (var r in chain.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                message.References.Add(r.Trim('<', '>'));
            }
        }
        message.Body = new TextPart("plain") { Text = body };

        using var smtp = new MailKit.Net.Smtp.SmtpClient();
        await smtp.ConnectAsync(
            creds.SmtpHost,
            creds.SmtpPort,
            creds.SmtpStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
            ct);
        await smtp.AuthenticateAsync(creds.Username, creds.Password, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);

        _ = targetRef; // targetRef = thread-key = from-address; redundant zu metadata['from']
    }

    private static string PrefixSubject(string subject)
    {
        var trimmed = subject.TrimStart();
        if (trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("AW:", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }
        return $"Re: {subject}";
    }
}
