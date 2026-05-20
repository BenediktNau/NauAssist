using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class AddRuleHandlerTests
{
    [Fact]
    public async Task Handle_PersistsRule_AndReturnsIt()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(repo, audit, () => DateTimeOffset.Parse("2026-05-19T12:00:00+02:00"));

        var request = new AddRuleRequest(
            Text: "Mi 19–20 Uhr Sport",
            DaysOfWeek: DayOfWeekFlags.Wednesday,
            TimeRangeStart: new TimeOnly(19, 0),
            TimeRangeEnd: new TimeOnly(20, 0),
            Hardness: RuleHardness.Hard);

        var response = await handler.Handle(request, CancellationToken.None);

        response.Rule.Id.Should().BeGreaterThan(0);
        response.Rule.Text.Should().Be("Mi 19–20 Uhr Sport");
        response.Rule.DaysOfWeek.Should().Be(DayOfWeekFlags.Wednesday);
        response.Rule.Hardness.Should().Be(RuleHardness.Hard);
        response.Rule.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-05-19T12:00:00+02:00"));
    }

    [Fact]
    public async Task Handle_RejectsEmptyText()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(repo, audit);

        var request = new AddRuleRequest(
            Text: "",
            DaysOfWeek: DayOfWeekFlags.AllDays,
            TimeRangeStart: null,
            TimeRangeEnd: null,
            Hardness: RuleHardness.Soft);

        var act = async () => await handler.Handle(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Text*");
    }

    [Fact]
    public async Task Handle_RejectsEndBeforeStart()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(repo, audit);

        var request = new AddRuleRequest(
            Text: "Quatsch-Range",
            DaysOfWeek: DayOfWeekFlags.AllDays,
            TimeRangeStart: new TimeOnly(20, 0),
            TimeRangeEnd: new TimeOnly(18, 0),
            Hardness: RuleHardness.Soft);

        var act = async () => await handler.Handle(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*TimeRange*");
    }

    [Fact]
    public async Task Handle_AfterAdd_WritesAuditEntry()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(repo, audit);

        await handler.Handle(new AddRuleRequest(
            Text: "Mi Sport",
            DaysOfWeek: DayOfWeekFlags.Wednesday,
            TimeRangeStart: null,
            TimeRangeEnd: null,
            Hardness: RuleHardness.Soft), CancellationToken.None);

        (await audit.CountAsync(CancellationToken.None)).Should().Be(1);
        using var conn = db.AppDb.OpenConnection();
        var toolName = await Dapper.SqlMapper.ExecuteScalarAsync<string>(conn,
            "SELECT tool_name FROM audit_log LIMIT 1");
        toolName.Should().Be("add_rule");
    }

    private static AddRuleHandler BuildHandler(
        RuleRepository repo,
        AuditLogRepository audit,
        Func<DateTimeOffset>? clock = null) =>
        new(
            repo,
            audit,
            clock ?? (() => DateTimeOffset.UtcNow),
            NullLogger<AddRuleHandler>.Instance);
}
