using NauAssist.Backend.Features.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.DeleteRule;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class DeleteRuleHandlerTests
{
    [Fact]
    public async Task Handle_DeletesExistingRule_ReturnsTrue()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb, new UserContextHolder());
        var handler = BuildHandler(repo, audit);

        var saved = await repo.AddAsync(new Rule(0, "weg", DayOfWeekFlags.AllDays, null, null, RuleHardness.Soft, DateTimeOffset.UtcNow), CancellationToken.None);

        var response = await handler.Handle(new DeleteRuleRequest(saved.Id), CancellationToken.None);

        response.Deleted.Should().BeTrue();
        (await repo.ListAllAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NonexistentId_ReturnsFalse()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb, new UserContextHolder());
        var handler = BuildHandler(repo, audit);

        var response = await handler.Handle(new DeleteRuleRequest(99999), CancellationToken.None);

        response.Deleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AfterDelete_WritesAuditEntry()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb, new UserContextHolder());
        var handler = BuildHandler(repo, audit);

        var saved = await repo.AddAsync(new Rule(0, "weg", DayOfWeekFlags.AllDays, null, null, RuleHardness.Soft, DateTimeOffset.UtcNow), CancellationToken.None);
        await handler.Handle(new DeleteRuleRequest(saved.Id), CancellationToken.None);

        (await audit.CountAsync(CancellationToken.None)).Should().Be(1);
        using var conn = db.AppDb.OpenConnection();
        var toolName = await Dapper.SqlMapper.ExecuteScalarAsync<string>(conn,
            "SELECT tool_name FROM audit_log LIMIT 1");
        toolName.Should().Be("delete_rule");
    }

    private static DeleteRuleHandler BuildHandler(RuleRepository repo, AuditLogRepository audit) =>
        new(
            repo,
            audit,
            clock: () => DateTimeOffset.Parse("2026-05-19T10:00:00Z"),
            logger: NullLogger<DeleteRuleHandler>.Instance);
}
