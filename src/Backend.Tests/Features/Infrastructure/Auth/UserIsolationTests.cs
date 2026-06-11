using FluentAssertions;
using NauAssist.Backend.Features.AutonomousAgent;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Auth;

/// <summary>
/// Multi-User-Isolation (Spec §10): zwei User-Kontexte dürfen gegenseitig
/// keine Daten sehen — weder Chats noch Suggestions.
/// </summary>
public sealed class UserIsolationTests
{
    private static IUserContext User(string id)
    {
        var holder = new UserContextHolder();
        holder.Set(id);
        return holder;
    }

    [Fact]
    public void UserContextHolder_Default_IstDefaultUser()
    {
        new UserContextHolder().UserId.Should().Be(DefaultUser.Id);
    }

    [Fact]
    public async Task Messages_SindProUserGetrennt()
    {
        using var temp = new TempSqliteDb();
        var alice = new MessageRepository(temp.AppDb, User("alice"));
        var bob = new MessageRepository(temp.AppDb, User("bob"));

        await alice.AddAsync(
            new Message(0, "default", MessageRole.User, "hallo von alice", null, false,
                DateTimeOffset.Parse("2026-06-11T10:00:00Z")),
            CancellationToken.None);

        var aliceMsgs = await alice.GetRecentAsync("default", take: 10, CancellationToken.None);
        var bobMsgs = await bob.GetRecentAsync("default", take: 10, CancellationToken.None);

        aliceMsgs.Should().ContainSingle(m => m.Content == "hallo von alice");
        bobMsgs.Should().BeEmpty();
    }

    [Fact]
    public async Task Suggestions_SindProUserGetrennt_AuchPerId()
    {
        using var temp = new TempSqliteDb();
        var alice = new SuggestionRepository(temp.AppDb, User("alice"));
        var bob = new SuggestionRepository(temp.AppDb, User("bob"));
        var now = DateTimeOffset.Parse("2026-06-11T10:00:00Z");

        var created = await alice.InsertAsync(
            source: "imap", sourceRef: "msg-1", intent: "schedule_request",
            topic: "Volleyball", requester: "Lukas", quotedText: null,
            slots: [], draftReply: "Passt!", replyMetadata: null,
            now, CancellationToken.None);

        (await bob.ListAsync(null, 10, CancellationToken.None)).Should().BeEmpty();
        (await bob.GetAsync(created.Id, CancellationToken.None)).Should().BeNull();
        (await bob.SetStatusAsync(created.Id, SuggestionStatus.Dismissed, now, CancellationToken.None))
            .Should().BeFalse();

        (await alice.GetAsync(created.Id, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task BestandsdatenOhneUserId_LaufenAlsDefaultUser()
    {
        using var temp = new TempSqliteDb();
        var defaultRepo = new MessageRepository(temp.AppDb, new UserContextHolder());

        await defaultRepo.AddAsync(
            new Message(0, "default", MessageRole.User, "bestand", null, false,
                DateTimeOffset.Parse("2026-06-11T10:00:00Z")),
            CancellationToken.None);

        var msgs = await defaultRepo.GetRecentAsync("default", take: 10, CancellationToken.None);
        msgs.Should().ContainSingle(m => m.Content == "bestand");
    }
}
