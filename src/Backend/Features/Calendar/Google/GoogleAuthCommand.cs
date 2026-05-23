using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Calendar.Google;

public static class GoogleAuthCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleAuthCommand");
        var auth = services.GetRequiredService<GoogleAuthService>();

        try
        {
            logger.LogInformation("Starte OAuth-Flow gegen Google (Console).");
            var (url, flow) = await auth.StartAuthorizationAsync(ct);

            var receiver = new ConsoleCodeReceiver();
            var reqUrl = new AuthorizationCodeRequestUrl(new Uri(url))
            {
                RedirectUri = GoogleAuthService.RedirectUri,
            };
            var codeResponse = await receiver.ReceiveCodeAsync(reqUrl, ct);

            await auth.ExchangeCodeAsync(flow, codeResponse.Code, ct);
            logger.LogInformation("OAuth-Flow erfolgreich. Tokens persistiert.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth-Flow fehlgeschlagen.");
            return 1;
        }
    }
}
