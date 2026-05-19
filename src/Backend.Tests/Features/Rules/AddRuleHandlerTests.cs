using FluentAssertions;
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
        var handler = new AddRuleHandler(repo, () => DateTimeOffset.Parse("2026-05-19T12:00:00+02:00"));

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
        var handler = new AddRuleHandler(repo, () => DateTimeOffset.UtcNow);

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
        var handler = new AddRuleHandler(repo, () => DateTimeOffset.UtcNow);

        var request = new AddRuleRequest(
            Text: "Quatsch-Range",
            DaysOfWeek: DayOfWeekFlags.AllDays,
            TimeRangeStart: new TimeOnly(20, 0),
            TimeRangeEnd: new TimeOnly(18, 0),
            Hardness: RuleHardness.Soft);

        var act = async () => await handler.Handle(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*TimeRange*");
    }
}
