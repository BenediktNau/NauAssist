using AwesomeAssertions;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class RuleApplicatorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void Annotate_NoRules_AllSlotsPass()
    {
        var slot = SlotAt(2026, 5, 27, 14, 0, 15, 0);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, Array.Empty<Rule>());

        result.Should().HaveCount(1);
        result[0].Status.Should().Be(AnnotationStatus.Passes);
        result[0].ViolatedBy.Should().BeNull();
    }

    [Fact]
    public void Annotate_SlotOutsideRuleHours_Passes()
    {
        var slot = SlotAt(2026, 5, 27, 10, 0, 11, 0); // Mi 10:00–11:00
        var rule = HardRuleAfter18Mondays();

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.Passes);
    }

    [Fact]
    public void Annotate_SlotMatchesHardRule_ReturnsHardViolation()
    {
        // Mo 18:30–19:30 — fällt in 18-23:59
        var slot = SlotAt(2026, 5, 25, 18, 30, 19, 30);
        var rule = HardRuleAfter18Mondays();

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
        result[0].ViolatedBy.Should().Be(rule);
    }

    [Fact]
    public void Annotate_SlotMatchesSoftRule_ReturnsSoftViolation()
    {
        var slot = SlotAt(2026, 5, 25, 18, 30, 19, 30);
        var rule = HardRuleAfter18Mondays() with { Hardness = RuleHardness.Soft };

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.SoftViolation);
        result[0].ViolatedBy.Should().Be(rule);
    }

    [Fact]
    public void Annotate_SlotDayMismatch_DoesNotApplyRule()
    {
        // Rule gilt Mo–Fr, Slot ist Samstag
        var slot = SlotAt(2026, 5, 30, 18, 30, 19, 30);
        var rule = new Rule(1, "Mo–Fr nach 18 nicht", DayOfWeekFlags.WeekdaysOnly,
            new TimeOnly(18, 0), new TimeOnly(23, 59), RuleHardness.Hard, DateTimeOffset.UtcNow);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.Passes);
    }

    [Fact]
    public void Annotate_HardRuleBeatsSoftWhenBothMatch()
    {
        var slot = SlotAt(2026, 5, 25, 18, 30, 19, 30);

        var softRule = new Rule(1, "Soft Abend", DayOfWeekFlags.AllDays,
            new TimeOnly(18, 0), new TimeOnly(23, 59), RuleHardness.Soft, DateTimeOffset.UtcNow);
        var hardRule = new Rule(2, "Hard Mo Abend", DayOfWeekFlags.Monday,
            new TimeOnly(18, 0), new TimeOnly(23, 59), RuleHardness.Hard, DateTimeOffset.UtcNow);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { softRule, hardRule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
        result[0].ViolatedBy.Should().Be(hardRule);
    }

    [Fact]
    public void Annotate_RuleWithoutTimeRange_AppliesToEntireDay()
    {
        // Rule "Sonntag nie" — ohne TimeRange
        var slot = SlotAt(2026, 5, 31, 10, 0, 11, 0); // Sonntag
        var rule = new Rule(1, "Sonntag nie", DayOfWeekFlags.Sunday,
            TimeRangeStart: null, TimeRangeEnd: null, RuleHardness.Hard, DateTimeOffset.UtcNow);

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
    }

    [Fact]
    public void Annotate_SlotPartiallyOverlapsRule_IsAViolation()
    {
        // Slot 17:30–18:30, Rule ab 18:00 — Overlap mindestens ein Teil
        var slot = SlotAt(2026, 5, 25, 17, 30, 18, 30);
        var rule = HardRuleAfter18Mondays();

        var result = new RuleApplicator(Berlin).Annotate(new[] { slot }, new[] { rule });

        result[0].Status.Should().Be(AnnotationStatus.HardViolation);
    }

    /// <summary>Slot konstruiert in Europe/Berlin-Lokalzeit.</summary>
    private static SlotCandidate SlotAt(int year, int month, int day, int startH, int startM, int endH, int endM)
    {
        var start = new DateTimeOffset(year, month, day, startH, startM, 0,
            Berlin.GetUtcOffset(new DateTime(year, month, day, startH, startM, 0)));
        var end = new DateTimeOffset(year, month, day, endH, endM, 0,
            Berlin.GetUtcOffset(new DateTime(year, month, day, endH, endM, 0)));
        return new SlotCandidate(start, end);
    }

    /// <summary>Hilfs-Factory: "Mo nach 18 nicht" als harte Regel.</summary>
    private static Rule HardRuleAfter18Mondays() => new(
        Id: 1,
        Text: "Mo nach 18 Uhr nicht",
        DaysOfWeek: DayOfWeekFlags.Monday,
        TimeRangeStart: new TimeOnly(18, 0),
        TimeRangeEnd: new TimeOnly(23, 59),
        Hardness: RuleHardness.Hard,
        CreatedAt: DateTimeOffset.UtcNow);
}
