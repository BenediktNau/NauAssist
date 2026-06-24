using AwesomeAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class FreeSlotCalculatorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static FreeSlotCalculator Calc() => new(
        Berlin,
        new TimeOnly(9, 0),
        new TimeOnly(18, 0),
        DayOfWeekFlags.WeekdaysOnly);

    [Fact]
    public void Calculate_OneEmptyWeekday_FullDayMinusLunchIfNoEvents()
    {
        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: Array.Empty<CalendarEvent>(),
            durationMinutes: 60);

        slots.Should().NotBeEmpty();
        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 9, 0));
        slots.Last().End.Should().BeOnOrBefore(BerlinTime(2026, 5, 27, 18, 0));
    }

    [Fact]
    public void Calculate_EventInMiddle_SplitsAroundIt()
    {
        var ev = new CalendarEvent("e1", "Mittagstermin",
            BerlinTime(2026, 5, 27, 12, 0),
            BerlinTime(2026, 5, 27, 13, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.Should().Contain(s => s.End <= BerlinTime(2026, 5, 27, 12, 0));
        slots.Should().Contain(s => s.Start >= BerlinTime(2026, 5, 27, 13, 0));
        slots.Should().NotContain(s => s.Start < BerlinTime(2026, 5, 27, 13, 0) && s.End > BerlinTime(2026, 5, 27, 12, 0));
    }

    [Fact]
    public void Calculate_DurationLargerThanGap_GapDoesNotAppear()
    {
        var events = new[]
        {
            new CalendarEvent("e1", "A", BerlinTime(2026, 5, 27, 10, 0), BerlinTime(2026, 5, 27, 11, 0), null, null),
            new CalendarEvent("e2", "B", BerlinTime(2026, 5, 27, 12, 0), BerlinTime(2026, 5, 27, 13, 0), null, null),
        };

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: events,
            durationMinutes: 90);

        slots.Should().NotContain(s => s.Start >= BerlinTime(2026, 5, 27, 11, 0) && s.End <= BerlinTime(2026, 5, 27, 12, 0));
    }

    [Fact]
    public void Calculate_OutsideWorkingHours_IsIgnored()
    {
        var ev = new CalendarEvent("e1", "Frueh",
            BerlinTime(2026, 5, 27, 6, 0),
            BerlinTime(2026, 5, 27, 8, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 9, 0));
    }

    [Fact]
    public void Calculate_SaturdayAndSunday_AreSkipped()
    {
        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 30, 0, 0),
            to: BerlinTime(2026, 6, 1, 0, 0),
            events: Array.Empty<CalendarEvent>(),
            durationMinutes: 60);

        slots.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_MultipleDays_SpansAcrossAllWeekdays()
    {
        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 25, 0, 0),
            to: BerlinTime(2026, 5, 30, 0, 0),
            events: Array.Empty<CalendarEvent>(),
            durationMinutes: 60);

        var distinctDays = slots.Select(s => s.Start.LocalDateTime.Date).Distinct().Count();
        distinctDays.Should().Be(5);
    }

    [Fact]
    public void Calculate_EventPartiallyOverlappingMorning_ShrinksMorningWindow()
    {
        var ev = new CalendarEvent("e1", "Frueh-rein",
            BerlinTime(2026, 5, 27, 8, 30),
            BerlinTime(2026, 5, 27, 10, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 10, 0));
    }

    [Fact]
    public void Calculate_FullyBookedDay_ReturnsNoSlots()
    {
        var ev = new CalendarEvent("e1", "Ganztag",
            BerlinTime(2026, 5, 27, 9, 0),
            BerlinTime(2026, 5, 27, 18, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_AllDayEvent_DoesNotBlockSlots()
    {
        var schulung = new CalendarEvent(
            Id: "e-allday",
            Title: "Schulung",
            Start: BerlinTime(2026, 5, 27, 0, 0),
            End:   BerlinTime(2026, 5, 28, 0, 0),
            Description: null,
            Location: null,
            IsAllDay: true);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to:   BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { schulung },
            durationMinutes: 60);

        slots.Should().NotBeEmpty();
        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 9, 0));
        slots.Last().End.Should().BeOnOrBefore(BerlinTime(2026, 5, 27, 18, 0));
    }

    [Fact]
    public void Calculate_AllDayPlusRegularSameDay_OnlyRegularBlocks()
    {
        var schulung = new CalendarEvent(
            Id: "e-allday",
            Title: "Schulung",
            Start: BerlinTime(2026, 5, 27, 0, 0),
            End:   BerlinTime(2026, 5, 28, 0, 0),
            Description: null, Location: null, IsAllDay: true);

        var mittag = new CalendarEvent(
            Id: "e-mittag",
            Title: "Mittagstermin",
            Start: BerlinTime(2026, 5, 27, 12, 0),
            End:   BerlinTime(2026, 5, 27, 13, 0),
            Description: null, Location: null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to:   BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { schulung, mittag },
            durationMinutes: 60);

        slots.Should().Contain(s => s.End <= BerlinTime(2026, 5, 27, 12, 0));
        slots.Should().Contain(s => s.Start >= BerlinTime(2026, 5, 27, 13, 0));
        slots.Should().NotContain(s => s.Start < BerlinTime(2026, 5, 27, 13, 0) && s.End > BerlinTime(2026, 5, 27, 12, 0));
    }

    private static DateTimeOffset BerlinTime(int y, int m, int d, int h, int min) =>
        new DateTimeOffset(y, m, d, h, min, 0,
            Berlin.GetUtcOffset(new DateTime(y, m, d, h, min, 0)));
}
