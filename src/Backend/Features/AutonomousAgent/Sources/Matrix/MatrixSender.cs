namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Matrix;

public sealed class MatrixSender : ISourceSender
{
    private readonly MatrixClient _client;

    public MatrixSender(MatrixClient client)
    {
        _client = client;
    }

    public string Source => MatrixObserver.SourceKey;

    public async Task SendAsync(SourceAccount account, string targetRef, string body, CancellationToken ct)
    {
        var creds = MatrixCredentials.Parse(account.CredentialsJson);
        await _client.SendTextMessageAsync(creds, targetRef, body, ct);
    }
}
