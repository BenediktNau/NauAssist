namespace NauAssist.Backend.Features.Settings;

/// <summary>Pushover-Zugangsdaten (https://pushover.net): App-Token + User-Key.</summary>
public sealed record PushoverSettings(string Token, string UserKey)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(UserKey);
}
