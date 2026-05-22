using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace NauAssist.Backend.Features.Calendar.Google;

/// <summary>
/// Headless OAuth-CodeReceiver für Container ohne Browser.
/// Druckt die Auth-URL und liest den vom Nutzer manuell aus der Browser-Adresszeile
/// kopierten 'code'-Parameter von der Standard-Eingabe.
/// </summary>
public sealed class ConsoleCodeReceiver : ICodeReceiver
{
    public string RedirectUri => "http://localhost";

    public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url, CancellationToken ct)
    {
        var authUrl = url.Build().AbsoluteUri;

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════");
        Console.WriteLine(" NauAssist · Google-OAuth (headless)");
        Console.WriteLine("════════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("1) Öffne diese URL in einem Browser auf einem anderen Gerät:");
        Console.WriteLine();
        Console.WriteLine($"   {authUrl}");
        Console.WriteLine();
        Console.WriteLine("2) Nach 'Erlauben' versucht der Browser, eine Seite zu laden, die");
        Console.WriteLine("   nicht erreichbar ist (Page-Not-Found / Connection refused).");
        Console.WriteLine("   Das ist Absicht.");
        Console.WriteLine();
        Console.WriteLine("3) Kopiere aus der Adresszeile den Wert hinter 'code=' bis zum");
        Console.WriteLine("   ersten '&'-Zeichen (oder bis zum Ende) und füge ihn hier ein:");
        Console.WriteLine();
        Console.Write("Code: ");

        var code = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException("Kein Code eingegeben — OAuth-Flow abgebrochen.");
        }

        return Task.FromResult(new AuthorizationCodeResponseUrl { Code = code });
    }
}
