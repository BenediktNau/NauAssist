using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleAuthService
{
    public const string UserId = "nauassist-default";
    public const string RedirectUri = "http://localhost";

    private readonly IAppSettingsRepository _settings;
    private readonly SqliteDataStore _dataStore;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IAppSettingsRepository settings,
        SqliteDataStore dataStore,
        ILogger<GoogleAuthService> logger)
    {
        _settings = settings;
        _dataStore = dataStore;
        _logger = logger;
    }

    public async Task<UserCredential> GetCredentialAsync(CancellationToken ct)
    {
        var clientSecrets = await LoadClientSecretsAsync(ct);
        var flow = BuildFlow(clientSecrets);

        var token = await _dataStore.GetAsync<TokenResponse>(UserId);
        if (token is null)
        {
            throw new NotAuthenticatedException(
                "Google-Kalender ist nicht verbunden. Bitte in den Settings autorisieren.");
        }

        var credential = new UserCredential(flow, UserId, token);
        if (credential.Token.IsStale)
        {
            _logger.LogInformation("Google-Token ist abgelaufen — refreshe.");
            await credential.RefreshTokenAsync(ct);
        }
        return credential;
    }

    public async Task<(string AuthUrl, GoogleAuthorizationCodeFlow Flow)> StartAuthorizationAsync(
        CancellationToken ct)
    {
        var clientSecrets = await LoadClientSecretsAsync(ct);
        var flow = BuildFlow(clientSecrets);
        var url = flow.CreateAuthorizationCodeRequest(RedirectUri).Build().AbsoluteUri;
        return (url, flow);
    }

    public async Task ExchangeCodeAsync(
        GoogleAuthorizationCodeFlow flow, string code, CancellationToken ct)
    {
        await flow.ExchangeCodeForTokenAsync(UserId, code, RedirectUri, ct);
    }

    public async Task<bool> IsConnectedAsync()
    {
        var token = await _dataStore.GetAsync<TokenResponse>(UserId);
        return token is not null;
    }

    public Task DisconnectAsync() => _dataStore.ClearAsync();

    private async Task<ClientSecrets> LoadClientSecretsAsync(CancellationToken ct)
    {
        var creds = await _settings.GetGoogleCredentialsAsync(ct);
        if (creds is null)
        {
            throw new NotAuthenticatedException(
                "Google-OAuth-Credentials nicht konfiguriert. Bitte Client-ID und -Secret in den Settings eintragen.");
        }
        return new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret };
    }

    private GoogleAuthorizationCodeFlow BuildFlow(ClientSecrets clientSecrets) =>
        new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = clientSecrets,
            Scopes = new[] { CalendarService.Scope.Calendar },
            DataStore = _dataStore,
        });
}
