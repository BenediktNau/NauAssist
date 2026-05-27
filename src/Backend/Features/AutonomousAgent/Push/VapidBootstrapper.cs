using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Settings;
using WebPush;

namespace NauAssist.Backend.Features.AutonomousAgent.Push;

/// <summary>
/// Generiert beim ersten Start ein VAPID-Keypair, falls noch keins existiert.
/// Wird in Program.cs nach <c>DbInitializer</c> aufgerufen.
/// </summary>
public sealed class VapidBootstrapper
{
    private readonly IAppSettingsRepository _settings;
    private readonly ILogger<VapidBootstrapper> _logger;

    public VapidBootstrapper(IAppSettingsRepository settings, ILogger<VapidBootstrapper> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task EnsureKeysAsync(CancellationToken ct)
    {
        var existing = await _settings.GetVapidAsync(ct);
        if (existing.IsConfigured) return;

        var generated = VapidHelper.GenerateVapidKeys();
        var fresh = new VapidSettings(
            PublicKey: generated.PublicKey,
            PrivateKey: generated.PrivateKey,
            Subject: string.IsNullOrEmpty(existing.Subject) ? "mailto:agent@nauassist.local" : existing.Subject);

        await _settings.SetVapidAsync(fresh, ct);
        _logger.LogInformation("VAPID-Keypair generiert und gespeichert ({PubChars} Zeichen Public-Key).",
            fresh.PublicKey.Length);
    }
}
