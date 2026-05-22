using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleAuthService
{
    private readonly CalendarOptions _options;
    private readonly SqliteDataStore _dataStore;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IOptions<CalendarOptions> options,
        SqliteDataStore dataStore,
        ILogger<GoogleAuthService> logger)
    {
        _options = options.Value;
        _dataStore = dataStore;
        _logger = logger;
    }

    /// <summary>
    /// Liefert Credentials. Wenn noch keine im Store: löst interaktiven OAuth-Flow aus
    /// (öffnet Browser auf der lokalen Maschine).
    /// </summary>
    public async Task<UserCredential> GetCredentialAsync(CancellationToken ct)
    {
        var credentialsPath = Path.GetFullPath(_options.GoogleCredentialsPath);
        if (!File.Exists(credentialsPath))
        {
            throw new InvalidOperationException(
                $"Google-OAuth-Client-Secret nicht gefunden unter '{credentialsPath}'. " +
                "Bitte Datei aus der Google Cloud Console (OAuth 2.0 Client ID, Type: Desktop) herunterladen und dorthin legen.");
        }

        await using var stream = File.OpenRead(credentialsPath);
        var clientSecrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;

        var isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        ICodeReceiver? codeReceiver = isContainer ? new ConsoleCodeReceiver() : null;

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            new[] { CalendarService.Scope.Calendar },
            user: "nauassist-default",
            taskCancellationToken: ct,
            dataStore: _dataStore,
            codeReceiver: codeReceiver);

        if (credential.Token.IsStale)
        {
            _logger.LogInformation("Google-Token ist abgelaufen — refreshe.");
            await credential.RefreshTokenAsync(ct);
        }

        return credential;
    }
}
