namespace NauAssist.Backend.Features.Settings;

public sealed record VapidSettings(
    string PublicKey,
    string PrivateKey,
    string Subject)
{
    public bool IsConfigured =>
        !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(PrivateKey);
}
