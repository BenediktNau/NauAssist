using FluentAssertions;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Chat.ChatHistory;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class GetChatHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

    private static GetChatHistoryHandler BuildHandler(TempSqliteDb temp)
    {
        var messages = new MessageRepository(temp.AppDb);
        var markers = new ChatClearMarkerRepository(temp.AppDb);
        var cutoff = new ChatContextCutoff(markers, () => Now, TimeZoneInfo.Utc);
        return new GetChatHistoryHandler(messages, markers, cutoff);
    }

    [Fact]
    public async Task Handle_ReturnsMessagesOldestFirst()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        for (var i = 0; i < 3; i++)
        {
            await repo.AddAsync(
                new Message(0, "default", MessageRole.User, $"m{i}", null, false,
                    Now.AddMinutes(-30 + i)),
                CancellationToken.None);
        }

        var response = await BuildHandler(temp).Handle(new GetChatHistoryRequest("default"), CancellationToken.None);

        response.Messages.Select(m => m.Content).Should().Equal("m0", "m1", "m2");
    }

    [Fact]
    public async Task Handle_ExcludesMessagesBeforeDayStart()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        // Day start = 2026-05-19T05:00Z. Die erste Message liegt davor.
        await repo.AddAsync(
            new Message(0, "default", MessageRole.User, "gestern-spät", null, false,
                DateTimeOffset.Parse("2026-05-19T03:00:00Z")),
            CancellationToken.None);
        await repo.AddAsync(
            new Message(0, "default", MessageRole.User, "heute-früh", null, false,
                DateTimeOffset.Parse("2026-05-19T06:00:00Z")),
            CancellationToken.None);

        var response = await BuildHandler(temp).Handle(new GetChatHistoryRequest("default"), CancellationToken.None);

        response.Messages.Select(m => m.Content).Should().Equal("heute-früh");
    }

    [Fact]
    public async Task Handle_FiltersBySession()
    {
        using var temp = new TempSqliteDb();
        var repo = new MessageRepository(temp.AppDb);

        await repo.AddAsync(new Message(0, "a", MessageRole.User, "alpha", null, false, Now), CancellationToken.None);
        await repo.AddAsync(new Message(0, "b", MessageRole.User, "beta", null, false, Now.AddMinutes(1)), CancellationToken.None);

        var response = await BuildHandler(temp).Handle(new GetChatHistoryRequest("a"), CancellationToken.None);

        response.Messages.Should().ContainSingle().Which.Content.Should().Be("alpha");
    }

    [Fact]
    public async Task Handle_ReturnsMarkersWithinDayStart()
    {
        using var temp = new TempSqliteDb();
        var markerRepo = new ChatClearMarkerRepository(temp.AppDb);

        // Marker vor day-start (04:00Z) wird ignoriert; späterer Marker erscheint.
        await markerRepo.AddAsync("default", DateTimeOffset.Parse("2026-05-19T04:00:00Z"), CancellationToken.None);
        await markerRepo.AddAsync("default", DateTimeOffset.Parse("2026-05-19T08:00:00Z"), CancellationToken.None);

        var response = await BuildHandler(temp).Handle(new GetChatHistoryRequest("default"), CancellationToken.None);

        response.Markers.Should().ContainSingle().Which.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-05-19T08:00:00Z"));
    }
}
