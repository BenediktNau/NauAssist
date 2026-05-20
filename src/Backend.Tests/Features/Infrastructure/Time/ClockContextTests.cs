using FluentAssertions;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Time;

public sealed class ClockContextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static ClockContext BuildClockAt(int year, int month, int day, int hour, int minute, TimeZoneInfo zone)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        var nowLocal = new DateTimeOffset(local, offset);
        return new ClockContext(() => nowLocal.ToUniversalTime(), zone);
    }

    [Fact]
    public void Build_Mittwoch_14h32_StandardSnapshot()
    {
        // 2026-05-20 ist ein Mittwoch, KW 21.
        var clock = BuildClockAt(2026, 5, 20, 14, 32, Berlin);

        var snap = clock.Build();

        snap.Timezone.Should().Be("Europe/Berlin");
        snap.Today.Should().Be(new DateOnly(2026, 5, 20));
        snap.Tomorrow.Should().Be(new DateOnly(2026, 5, 21));
        snap.WeekdayDe.Should().Be("Mittwoch");
        snap.IsoWeek.Should().Be(21);
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 24)));
        snap.NextWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 31)));
        snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24)));
        snap.NextWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31)));
    }
}
