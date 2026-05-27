using System.Text.Json;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Imap;

public sealed class ImapCredentials
{
    public string ImapHost { get; init; } = "";
    public int ImapPort { get; init; } = 993;
    public bool ImapSsl { get; init; } = true;
    public string SmtpHost { get; init; } = "";
    public int SmtpPort { get; init; } = 587;
    public bool SmtpStartTls { get; init; } = true;
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }

    public string EffectiveFromAddress => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;

    public static ImapCredentials Parse(string credentialsJson)
    {
        var parsed = JsonSerializer.Deserialize<ImapCredentials>(
            credentialsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.ImapHost)
            || string.IsNullOrWhiteSpace(parsed.SmtpHost)
            || string.IsNullOrWhiteSpace(parsed.Username)
            || string.IsNullOrWhiteSpace(parsed.Password))
        {
            throw new ArgumentException("IMAP-Credentials unvollständig (host/username/password erforderlich).");
        }
        return parsed;
    }
}
