using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Calendar.Google;

/// <summary>
/// CLI-Logik für `dotnet run --project src/Backend -- auth`.
/// Öffnet den Browser, lässt den User die App autorisieren, persistiert Tokens in SQLite.
/// </summary>
public static class GoogleAuthCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleAuthCommand");
        var auth = services.GetRequiredService<GoogleAuthService>();

        try
        {
            logger.LogInformation("Starte OAuth-Flow gegen Google. Browser öffnet sich gleich…");
            var credential = await auth.GetCredentialAsync(ct);
            logger.LogInformation("OAuth-Flow erfolgreich. Token-Ablauf: {Expiry}", credential.Token.ExpiresInSeconds);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth-Flow fehlgeschlagen.");
            return 1;
        }
    }
}
