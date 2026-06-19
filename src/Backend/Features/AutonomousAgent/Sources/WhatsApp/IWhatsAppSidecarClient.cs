namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Schmaler Client gegen den Node-Sidecar (Baileys). Als Interface, damit Observer
/// und Sender ohne echten HTTP-Stack testbar sind.
/// </summary>
public interface IWhatsAppSidecarClient
{
    Task<WhatsAppSession> CreateSessionAsync(string? sessionId, CancellationToken ct);
    Task<WhatsAppSessionStatus?> GetSessionAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<WhatsAppChat>> ListChatsAsync(string sessionId, CancellationToken ct);
    Task<WhatsAppMessagePage> GetMessagesAsync(string sessionId, long since, int limit, CancellationToken ct);
    Task SendAsync(string sessionId, string chatId, string text, CancellationToken ct);
    Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct);
}
