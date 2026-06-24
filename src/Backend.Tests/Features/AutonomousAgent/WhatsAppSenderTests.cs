using AwesomeAssertions;
using NauAssist.Backend.Features.AutonomousAgent.Sources;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

namespace NauAssist.Backend.Tests.Features.AutonomousAgent;

public sealed class WhatsAppSenderTests
{
    [Fact]
    public async Task Send_CallsSidecarWithSessionChatAndBody()
    {
        var fake = new CapturingSidecar();
        var sender = new WhatsAppSender(fake);
        var account = new SourceAccount(
            Id: 1,
            Kind: WhatsAppObserver.SourceKey,
            DisplayName: "Test",
            CredentialsJson: "{\"sessionId\":\"sess9\",\"phoneLabel\":\"+49\"}",
            Allowlist: new[] { "chatA@s.whatsapp.net" },
            Enabled: true,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch);

        await sender.SendAsync(account, "chatA@s.whatsapp.net", "Hallo zurück", null, CancellationToken.None);

        fake.SessionId.Should().Be("sess9");
        fake.ChatId.Should().Be("chatA@s.whatsapp.net");
        fake.Text.Should().Be("Hallo zurück");
    }

    private sealed class CapturingSidecar : IWhatsAppSidecarClient
    {
        public string? SessionId { get; private set; }
        public string? ChatId { get; private set; }
        public string? Text { get; private set; }

        public Task SendAsync(string sessionId, string chatId, string text, CancellationToken ct)
        {
            SessionId = sessionId;
            ChatId = chatId;
            Text = text;
            return Task.CompletedTask;
        }

        public Task<WhatsAppSession> CreateSessionAsync(string? sessionId, CancellationToken ct) =>
            Task.FromResult(new WhatsAppSession("sess9", "connected"));
        public Task<WhatsAppSessionStatus?> GetSessionAsync(string sessionId, CancellationToken ct) =>
            Task.FromResult<WhatsAppSessionStatus?>(null);
        public Task<IReadOnlyList<WhatsAppChat>> ListChatsAsync(string sessionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WhatsAppChat>>(Array.Empty<WhatsAppChat>());
        public Task<WhatsAppMessagePage> GetMessagesAsync(string sessionId, long since, int limit, CancellationToken ct) =>
            Task.FromResult(new WhatsAppMessagePage(Array.Empty<WhatsAppMessage>(), since));
        public Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct) =>
            Task.FromResult(new WhatsAppResolveResult($"{phone}@s.whatsapp.net", null, true));
        public Task DeleteSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
    }
}
