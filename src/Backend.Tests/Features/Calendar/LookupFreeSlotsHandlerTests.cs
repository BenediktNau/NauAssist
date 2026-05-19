using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class LookupFreeSlotsHandlerTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Handle_NoEventsNoRules_ReturnsPassingSlots()
    {
        using var db = new TempSqliteDb();
        var ruleRepo = new RuleRepository(db.AppDb);
        var provider = new FakeCalendarProvider();
        var calc = MakeCalculator();
        var applicator = new RuleApplicator(Berlin);

        var handler = new LookupFreeSlotsHandler(ruleRepo, provider, calc, applicator);

        var response = await handler.Handle(new LookupFreeSlotsRequest(
            From: BerlinTime(2026, 5, 27, 0, 0),
            To: BerlinTime(2026, 5, 28, 0, 0),
            DurationMinutes: 60), CancellationToken.None);

        response.Annotations.Should().NotBeEmpty();
        response.Annotations.Should().OnlyContain(a => a.Status == AnnotationStatus.Passes);
    }

    [Fact]
    public async Task Handle_RuleBlocksEvening_EveningSlotsAreHardViolations()
    {
        using var db = new TempSqliteDb();
        var ruleRepo = new RuleRepository(db.AppDb);
        await ruleRepo.AddAsync(new Rule(
            Id: 0,
            Text: "Mo-Fr nach 17 nicht",
            DaysOfWeek: DayOfWeekFlags.WeekdaysOnly,
            TimeRangeStart: new TimeOnly(17, 0),
            TimeRangeEnd: new TimeOnly(23, 59),
            Hardness: RuleHardness.Hard,
            CreatedAt: DateTimeOffset.UtcNow), CancellationToken.None);

        var provider = new FakeCalendarProvider();
        var calc = MakeCalculator();
        var applicator = new RuleApplicator(Berlin);

        var handler = new LookupFreeSlotsHandler(ruleRepo, provider, calc, applicator);

        var response = await handler.Handle(new LookupFreeSlotsRequest(
            From: BerlinTime(2026, 5, 27, 0, 0),
            To: BerlinTime(2026, 5, 28, 0, 0),
            DurationMinutes: 60), CancellationToken.None);

        response.Annotations.Should().Contain(a =>
            a.Status == AnnotationStatus.HardViolation
            && a.Slot.Start.Hour >= 17);
    }

    [Fact]
    public async Task Handle_BlockedByExistingEvent_GapDisappearsCompletely()
    {
        using var db = new TempSqliteDb();
        var ruleRepo = new RuleRepository(db.AppDb);
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent("blocker", "Sprint",
            BerlinTime(2026, 5, 27, 9, 0),
            BerlinTime(2026, 5, 27, 18, 0),
            null, null));
        var calc = MakeCalculator();
        var applicator = new RuleApplicator(Berlin);

        var handler = new LookupFreeSlotsHandler(ruleRepo, provider, calc, applicator);

        var response = await handler.Handle(new LookupFreeSlotsRequest(
            From: BerlinTime(2026, 5, 27, 0, 0),
            To: BerlinTime(2026, 5, 28, 0, 0),
            DurationMinutes: 60), CancellationToken.None);

        response.Annotations.Should().BeEmpty();
    }

    private static FreeSlotCalculator MakeCalculator() => new(
        Berlin,
        new TimeOnly(9, 0),
        new TimeOnly(18, 0),
        DayOfWeekFlags.WeekdaysOnly);

    private static DateTimeOffset BerlinTime(int y, int m, int d, int h, int min) =>
        new DateTimeOffset(y, m, d, h, min, 0,
            Berlin.GetUtcOffset(new DateTime(y, m, d, h, min, 0)));
}
