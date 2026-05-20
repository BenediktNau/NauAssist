using FluentAssertions;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Chat.ChatHistory;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class GetChatHistoryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsMessagesOldestFirst()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        for (var i = 0; i < 3; i++)
        {
            await repo.AddAsync(
                new Message(0, "default", MessageRole.User, $"m{i}", null, false,
                    DateTimeOffset.Parse("2026-05-19T10:00:00Z").AddMinutes(i)),
                CancellationToken.None);
        }

        var handler = new GetChatHistoryHandler(repo);
        var response = await handler.Handle(new GetChatHistoryRequest("default"), CancellationToken.None);

        response.Messages.Select(m => m.Content).Should().Equal("m0", "m1", "m2");
    }

    [Fact]
    public async Task Handle_HonorsTakeParameter()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        for (var i = 0; i < 5; i++)
        {
            await repo.AddAsync(
                new Message(0, "default", MessageRole.User, $"m{i}", null, false,
                    DateTimeOffset.Parse("2026-05-19T10:00:00Z").AddMinutes(i)),
                CancellationToken.None);
        }

        var handler = new GetChatHistoryHandler(repo);
        var response = await handler.Handle(new GetChatHistoryRequest("default", Take: 2), CancellationToken.None);

        // GetRecent take=2 → newest two (m4, m3); reversed → m3, m4
        response.Messages.Select(m => m.Content).Should().Equal("m3", "m4");
    }

    [Fact]
    public async Task Handle_FiltersBySession()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        await repo.AddAsync(new Message(0, "a", MessageRole.User, "alpha", null, false,
            DateTimeOffset.Parse("2026-05-19T10:00:00Z")), CancellationToken.None);
        await repo.AddAsync(new Message(0, "b", MessageRole.User, "beta", null, false,
            DateTimeOffset.Parse("2026-05-19T10:01:00Z")), CancellationToken.None);

        var handler = new GetChatHistoryHandler(repo);
        var response = await handler.Handle(new GetChatHistoryRequest("a"), CancellationToken.None);

        response.Messages.Should().ContainSingle().Which.Content.Should().Be("alpha");
    }
}
