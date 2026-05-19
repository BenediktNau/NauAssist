using FluentAssertions;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class MessageRepositoryTests
{
    [Fact]
    public async Task AddAsync_PersistsRow_ReturnsAssignedId()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        var saved = await repo.AddAsync(
            new Message(0, "default", MessageRole.User, "hallo", null, false,
                DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        saved.Id.Should().BeGreaterThan(0);
        saved.Content.Should().Be("hallo");
        saved.Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirstUpToTake()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        for (var i = 0; i < 5; i++)
        {
            await repo.AddAsync(
                new Message(0, "default", MessageRole.User, $"msg {i}", null, false,
                    DateTimeOffset.Parse("2026-05-19T10:00:00Z").AddMinutes(i)),
                CancellationToken.None);
        }

        var recent = await repo.GetRecentAsync("default", take: 3, CancellationToken.None);

        recent.Should().HaveCount(3);
        recent.Select(m => m.Content).Should().Equal("msg 4", "msg 3", "msg 2");
    }

    [Fact]
    public async Task GetRecentAsync_FiltersBySessionId()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        await repo.AddAsync(new Message(0, "a", MessageRole.User, "a1", null, false,
            DateTimeOffset.Parse("2026-05-19T10:00:00Z")), CancellationToken.None);
        await repo.AddAsync(new Message(0, "b", MessageRole.User, "b1", null, false,
            DateTimeOffset.Parse("2026-05-19T10:01:00Z")), CancellationToken.None);

        var aMessages = await repo.GetRecentAsync("a", take: 10, CancellationToken.None);

        aMessages.Should().HaveCount(1);
        aMessages[0].Content.Should().Be("a1");
    }

    [Fact]
    public async Task AddAsync_RoundTripsProposalsJsonAndAssistantRole()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        var saved = await repo.AddAsync(
            new Message(0, "default", MessageRole.Assistant, "Vorschläge:", """[{"start":"2026-05-20T09:00:00Z"}]""",
                false, DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        var recent = await repo.GetRecentAsync("default", 1, CancellationToken.None);
        recent[0].Role.Should().Be(MessageRole.Assistant);
        recent[0].ProposalsJson.Should().Contain("2026-05-20");
        recent[0].Id.Should().Be(saved.Id);
    }

    [Fact]
    public async Task MarkIncompleteAsync_FlipsFlag()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        var saved = await repo.AddAsync(
            new Message(0, "default", MessageRole.Assistant, "halb", null, false,
                DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        await repo.MarkIncompleteAsync(saved.Id, CancellationToken.None);

        var recent = await repo.GetRecentAsync("default", take: 1, CancellationToken.None);
        recent[0].Incomplete.Should().BeTrue();
    }
}
