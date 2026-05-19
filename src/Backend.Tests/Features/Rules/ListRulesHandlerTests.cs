using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.ListRules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class ListRulesHandlerTests
{
    [Fact]
    public async Task Handle_OnEmptyDb_ReturnsEmptyList()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new ListRulesHandler(repo);

        var response = await handler.Handle(new ListRulesRequest(), CancellationToken.None);

        response.Rules.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsAllSavedRules()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);
        var handler = new ListRulesHandler(repo);

        await repo.AddAsync(new Rule(0, "A", DayOfWeekFlags.AllDays, null, null, RuleHardness.Soft, DateTimeOffset.UtcNow.AddMinutes(-5)), CancellationToken.None);
        await repo.AddAsync(new Rule(0, "B", DayOfWeekFlags.AllDays, null, null, RuleHardness.Hard, DateTimeOffset.UtcNow), CancellationToken.None);

        var response = await handler.Handle(new ListRulesRequest(), CancellationToken.None);

        response.Rules.Should().HaveCount(2);
        response.Rules.Select(r => r.Text).Should().ContainInOrder("A", "B");
    }
}
