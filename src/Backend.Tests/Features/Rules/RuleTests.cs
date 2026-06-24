using AwesomeAssertions;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Tests.Features.Rules;

public sealed class RuleTests
{
    [Fact]
    public void DayOfWeekFlags_WeekdaysOnly_HasMondayThruFriday()
    {
        var weekdays = DayOfWeekFlags.WeekdaysOnly;

        weekdays.HasFlag(DayOfWeekFlags.Monday).Should().BeTrue();
        weekdays.HasFlag(DayOfWeekFlags.Friday).Should().BeTrue();
        weekdays.HasFlag(DayOfWeekFlags.Saturday).Should().BeFalse();
        weekdays.HasFlag(DayOfWeekFlags.Sunday).Should().BeFalse();
    }

    [Fact]
    public void DayOfWeekFlags_AllDays_HasAllSevenDays()
    {
        var all = DayOfWeekFlags.AllDays;
        ((int)all).Should().Be(127); // 2^7 - 1
    }

    [Fact]
    public void Rule_CanBeConstructed_WithValidProperties()
    {
        var rule = new Rule(
            Id: 1,
            Text: "Mo–Fr nach 18 Uhr lieber frei",
            DaysOfWeek: DayOfWeekFlags.WeekdaysOnly,
            TimeRangeStart: new TimeOnly(18, 0),
            TimeRangeEnd: new TimeOnly(23, 59),
            Hardness: RuleHardness.Soft,
            CreatedAt: DateTimeOffset.UtcNow);

        rule.Text.Should().Be("Mo–Fr nach 18 Uhr lieber frei");
        rule.Hardness.Should().Be(RuleHardness.Soft);
    }
}
