using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class RuleRepositoryTests
{
    [Fact]
    public async Task Add_PersistsRule_AndAssignsId()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var draft = new Rule(
            Id: 0,
            Text: "Mo–Fr nach 18 Uhr nicht",
            DaysOfWeek: DayOfWeekFlags.WeekdaysOnly,
            TimeRangeStart: new TimeOnly(18, 0),
            TimeRangeEnd: new TimeOnly(23, 59),
            Hardness: RuleHardness.Hard,
            CreatedAt: DateTimeOffset.UtcNow);

        var saved = await repo.AddAsync(draft, CancellationToken.None);

        saved.Id.Should().BeGreaterThan(0);
        saved.Text.Should().Be("Mo–Fr nach 18 Uhr nicht");
    }

    [Fact]
    public async Task ListAll_ReturnsAddedRules_OrderedByCreatedAt()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var first = await repo.AddAsync(MakeRule("Regel A", DateTimeOffset.UtcNow.AddMinutes(-10)), CancellationToken.None);
        var second = await repo.AddAsync(MakeRule("Regel B", DateTimeOffset.UtcNow), CancellationToken.None);

        var all = await repo.ListAllAsync(CancellationToken.None);

        all.Should().HaveCount(2);
        all[0].Id.Should().Be(first.Id);
        all[1].Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task Delete_RemovesRule_AndReturnsTrue()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var saved = await repo.AddAsync(MakeRule("zum Löschen", DateTimeOffset.UtcNow), CancellationToken.None);

        var deleted = await repo.DeleteAsync(saved.Id, CancellationToken.None);

        deleted.Should().BeTrue();
        var all = await repo.ListAllAsync(CancellationToken.None);
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NonexistentId_ReturnsFalse()
    {
        using var db = new TempSqliteDb();
        var repo = new RuleRepository(db.AppDb);

        var deleted = await repo.DeleteAsync(99999, CancellationToken.None);

        deleted.Should().BeFalse();
    }

    private static Rule MakeRule(string text, DateTimeOffset createdAt) => new(
        Id: 0,
        Text: text,
        DaysOfWeek: DayOfWeekFlags.AllDays,
        TimeRangeStart: null,
        TimeRangeEnd: null,
        Hardness: RuleHardness.Soft,
        CreatedAt: createdAt);
}
