using System.Text.Json;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Was in <c>source_accounts.credentials_json</c> für einen WhatsApp-Account liegt.
/// Bewusst minimal: der eigentliche Auth-State (QR-Session, Schlüssel) wohnt im
/// Sidecar-Volume, nicht in der NauAssist-DB.
/// </summary>
public sealed class WhatsAppCredentials
{
    public string SessionId { get; init; } = "";
    public string PhoneLabel { get; init; } = "";

    public static WhatsAppCredentials Parse(string credentialsJson)
    {
        var parsed = JsonSerializer.Deserialize<WhatsAppCredentials>(
            credentialsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.SessionId))
        {
            throw new ArgumentException("WhatsApp-Credentials unvollständig (sessionId erforderlich).");
        }
        return parsed;
    }
}
