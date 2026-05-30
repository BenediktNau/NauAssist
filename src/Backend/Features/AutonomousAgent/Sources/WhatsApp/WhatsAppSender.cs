namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

public sealed class WhatsAppSender : ISourceSender
{
    private readonly IWhatsAppSidecarClient _client;

    public WhatsAppSender(IWhatsAppSidecarClient client)
    {
        _client = client;
    }

    public string Source => WhatsAppObserver.SourceKey;

    public async Task SendAsync(
        SourceAccount account,
        string targetRef,
        string body,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        // targetRef = chatId (= RawSignal.SourceRef). WhatsApp kennt keine Reply-Header → metadata ungenutzt.
        _ = metadata;
        var creds = WhatsAppCredentials.Parse(account.CredentialsJson);
        await _client.SendAsync(creds.SessionId, targetRef, body, ct);
    }
}
