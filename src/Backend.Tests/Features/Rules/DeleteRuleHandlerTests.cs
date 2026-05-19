using FluentAssertions;
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
        var handler = new DeleteRuleHandler(repo);

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
        var handler = new DeleteRuleHandler(repo);

        var response = await handler.Handle(new DeleteRuleRequest(99999), CancellationToken.None);

        response.Deleted.Should().BeFalse();
    }
}
