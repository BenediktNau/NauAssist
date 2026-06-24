using NauAssist.Backend.Features.Infrastructure.Auth;
using AwesomeAssertions;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Audit;

public sealed class AuditLogRepositoryTests
{
    [Fact]
    public async Task AppendAsync_PersistsRow_ReturnsId()
    {
        using var temp = new TempSqliteDb();
        var repo = new AuditLogRepository(temp.AppDb, new UserContextHolder());

        var saved = await repo.AppendAsync(new AuditEntry(
            Id: 0,
            TriggeringMessageId: null,
            ToolName: "create_event",
            ToolArgsJson: """{"title":"X"}""",
            ResultJson: """{"id":"evt1"}""",
            ProviderEventId: "evt1",
            CreatedAt: DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        saved.Id.Should().BeGreaterThan(0);
        saved.ToolName.Should().Be("create_event");
        saved.ProviderEventId.Should().Be("evt1");
    }

    [Fact]
    public async Task GetByMessageIdAsync_ReturnsOnlyMatchingEntries_OrderedByIdAsc()
    {
        using var temp = new TempSqliteDb();
        var messages = new MessageRepository(temp.AppDb, new UserContextHolder());
        var repo = new AuditLogRepository(temp.AppDb, new UserContextHolder());

        var msgA = await messages.AddAsync(new Message(0, "default", MessageRole.User, "a", null, false,
            DateTimeOffset.Parse("2026-05-19T09:00:00Z")), CancellationToken.None);
        var msgB = await messages.AddAsync(new Message(0, "default", MessageRole.User, "b", null, false,
            DateTimeOffset.Parse("2026-05-19T09:01:00Z")), CancellationToken.None);

        // 2 Einträge für msgA, 1 für msgB
        await repo.AppendAsync(new AuditEntry(0, msgA.Id, "create_event", "{}", "{}", "p1",
            DateTimeOffset.Parse("2026-05-19T10:00:00Z")), CancellationToken.None);
        await repo.AppendAsync(new AuditEntry(0, msgB.Id, "add_rule", "{}", "{}", null,
            DateTimeOffset.Parse("2026-05-19T10:01:00Z")), CancellationToken.None);
        await repo.AppendAsync(new AuditEntry(0, msgA.Id, "delete_rule", "{}", "{}", null,
            DateTimeOffset.Parse("2026-05-19T10:02:00Z")), CancellationToken.None);

        var entries = await repo.GetByMessageIdAsync(msgA.Id, CancellationToken.None);

        entries.Should().HaveCount(2);
        entries.Select(e => e.ToolName).Should().Equal("create_event", "delete_rule");
    }

    [Fact]
    public async Task AppendAsync_AllowsNullTriggeringMessageAndNullProviderEventId()
    {
        using var temp = new TempSqliteDb();
        var repo = new AuditLogRepository(temp.AppDb, new UserContextHolder());

        var saved = await repo.AppendAsync(new AuditEntry(0, null, "add_rule",
            """{"text":"abends frei"}""", """{"id":1}""", null,
            DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        saved.Id.Should().BeGreaterThan(0);
        saved.TriggeringMessageId.Should().BeNull();
        saved.ProviderEventId.Should().BeNull();
    }
}
