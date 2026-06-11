using NauAssist.Backend.Features.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.AutonomousAgent.Sources;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.AutonomousAgent;

public sealed class WhatsAppObserverTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 30, 10, 0, 0, TimeSpan.Zero);
    private const string SourceKey = WhatsAppObserver.SourceKey;

    private static (WhatsAppObserver observer, SourceAccountRepository accounts, SourceCursorRepository cursors)
        Build(TempSqliteDb db, IWhatsAppSidecarClient client)
    {
        var accounts = new SourceAccountRepository(db.AppDb, new UserContextHolder());
        var cursors = new SourceCursorRepository(db.AppDb, new UserContextHolder());
        var observer = new WhatsAppObserver(
            accounts,
            cursors,
            client,
            Options.Create(new WhatsAppOptions { Enabled = true, MaxBodyChars = 2000, MessageBatchLimit = 200 }),
            () => Now,
            NullLogger<WhatsAppObserver>.Instance);
        return (observer, accounts, cursors);
    }

    private static Task<SourceAccount> AddAccountAsync(SourceAccountRepository accounts, params string[] allowlist) =>
        accounts.AddAsync(
            SourceKey,
            "Test-WA",
            "{\"sessionId\":\"sess1\",\"phoneLabel\":\"+49\"}",
            allowlist,
            Now,
            CancellationToken.None);

    private static WhatsAppMessage Msg(
        long seq, string chatId, string text, bool fromMe = false,
        string? sender = "111@s.whatsapp.net", string? senderName = "Alice") =>
        new(seq, $"m{seq}", chatId, "Chat A", sender, senderName, text, Now.ToUnixTimeMilliseconds(), fromMe);

    [Fact]
    public async Task Poll_NoAccounts_ReturnsEmpty()
    {
        using var db = new TempSqliteDb();
        var (observer, _, _) = Build(db, new FakeSidecar());

        var signals = await observer.PollAsync(CancellationToken.None);

        signals.Should().BeEmpty();
    }

    [Fact]
    public async Task Poll_EmptyAllowlist_SkipsAndDoesNotCallSidecar()
    {
        using var db = new TempSqliteDb();
        var fake = new FakeSidecar();
        var (observer, accounts, _) = Build(db, fake);
        await AddAccountAsync(accounts);

        var signals = await observer.PollAsync(CancellationToken.None);

        signals.Should().BeEmpty();
        fake.GetMessagesCalls.Should().Be(0);
    }

    [Fact]
    public async Task Poll_InitialSync_DiscardsMessagesAndSetsCursor()
    {
        using var db = new TempSqliteDb();
        var fake = new FakeSidecar
        {
            Page = new WhatsAppMessagePage(new[] { Msg(5, "chatA", "hallo") }, Cursor: 5),
        };
        var (observer, accounts, cursors) = Build(db, fake);
        var account = await AddAccountAsync(accounts, "chatA");

        var signals = await observer.PollAsync(CancellationToken.None);

        signals.Should().BeEmpty(); // Initial-Sync verwirft
        (await cursors.GetAsync(SourceKey, account.Id, CancellationToken.None)).Should().Be("5");
    }

    [Fact]
    public async Task Poll_MapsAllowlistedMessage_SkipsOthersAndFromMe()
    {
        using var db = new TempSqliteDb();
        var fake = new FakeSidecar
        {
            Page = new WhatsAppMessagePage(new[]
            {
                Msg(2, "chatA", "Hast du Freitag Zeit?"),
                Msg(3, "chatB", "nicht freigegeben"),
                Msg(4, "chatA", "meine eigene", fromMe: true),
            }, Cursor: 4),
        };
        var (observer, accounts, cursors) = Build(db, fake);
        var account = await AddAccountAsync(accounts, "chatA");
        await cursors.SetAsync(SourceKey, account.Id, "1", Now, CancellationToken.None); // kein Initial-Sync

        var signals = await observer.PollAsync(CancellationToken.None);

        signals.Should().HaveCount(1);
        var s = signals[0];
        s.Source.Should().Be(SourceKey);
        s.SourceRef.Should().Be("chatA");
        s.Sender.Should().Be("Alice");
        s.Text.Should().Be("Hast du Freitag Zeit?");
        s.Metadata!["chatId"].Should().Be("chatA");
        s.Metadata!["messageId"].Should().Be("m2");
        s.Metadata!["senderJid"].Should().Be("111@s.whatsapp.net");

        fake.LastSince.Should().Be(1);
        (await cursors.GetAsync(SourceKey, account.Id, CancellationToken.None)).Should().Be("4");
    }

    [Fact]
    public async Task Poll_TruncatesLongBody()
    {
        using var db = new TempSqliteDb();
        var fake = new FakeSidecar
        {
            Page = new WhatsAppMessagePage(new[] { Msg(2, "chatA", new string('x', 2500)) }, Cursor: 2),
        };
        var (observer, accounts, cursors) = Build(db, fake);
        var account = await AddAccountAsync(accounts, "chatA");
        await cursors.SetAsync(SourceKey, account.Id, "1", Now, CancellationToken.None);

        var signals = await observer.PollAsync(CancellationToken.None);

        signals.Should().HaveCount(1);
        signals[0].Text.Should().HaveLength(2001); // 2000 + "…"
        signals[0].Text.Should().EndWith("…");
    }

    private sealed class FakeSidecar : IWhatsAppSidecarClient
    {
        public WhatsAppMessagePage Page { get; set; } = new(Array.Empty<WhatsAppMessage>(), 0);
        public int GetMessagesCalls { get; private set; }
        public long LastSince { get; private set; }

        public Task<WhatsAppMessagePage> GetMessagesAsync(string sessionId, long since, int limit, CancellationToken ct)
        {
            GetMessagesCalls++;
            LastSince = since;
            return Task.FromResult(Page);
        }

        public Task<WhatsAppSession> CreateSessionAsync(string? sessionId, CancellationToken ct) =>
            Task.FromResult(new WhatsAppSession("sess1", "connected"));
        public Task<WhatsAppSessionStatus?> GetSessionAsync(string sessionId, CancellationToken ct) =>
            Task.FromResult<WhatsAppSessionStatus?>(null);
        public Task<IReadOnlyList<WhatsAppChat>> ListChatsAsync(string sessionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WhatsAppChat>>(Array.Empty<WhatsAppChat>());
        public Task SendAsync(string sessionId, string chatId, string text, CancellationToken ct) =>
            Task.CompletedTask;
        public Task DeleteSessionAsync(string sessionId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
