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

    [Fact]
    public void Build_Samstag_thisWeekendIstHeuteUndMorgen()
    {
        // 2026-05-23 ist ein Samstag.
        var clock = BuildClockAt(2026, 5, 23, 10, 0, Berlin);

        var snap = clock.Build();

        snap.WeekdayDe.Should().Be("Samstag");
        snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24)));
        snap.NextWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31)));
        // Sa gehört noch zur laufenden Mo–So-KW.
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 24)));
    }

    [Fact]
    public void Build_Sonntag_thisWeekEndetHeute_thisWeekendIstGesternUndHeute()
    {
        // 2026-05-24 ist ein Sonntag.
        var clock = BuildClockAt(2026, 5, 24, 10, 0, Berlin);

        var snap = clock.Build();

        snap.WeekdayDe.Should().Be("Sonntag");
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 24)));
        snap.NextWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 31)));
        snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24)));
        snap.NextWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void Build_Sonntag_23h30_UTC_Sommerzeit_LokalIstMontag_TodayIstMontag()
    {
        // Sommerzeit Europe/Berlin = UTC+2. Sonntag 23:30 UTC → Montag 01:30 Berlin.
        var nowUtc = new DateTimeOffset(2026, 5, 24, 23, 30, 0, TimeSpan.Zero);
        var clock = new ClockContext(() => nowUtc, Berlin);

        var snap = clock.Build();

        snap.NowLocal.Year.Should().Be(2026);
        snap.NowLocal.Month.Should().Be(5);
        snap.NowLocal.Day.Should().Be(25);
        snap.NowLocal.Hour.Should().Be(1);
        snap.Today.Should().Be(new DateOnly(2026, 5, 25));
        snap.WeekdayDe.Should().Be("Montag");
        // Lokal Mo 25.05. — diese Woche beginnt heute.
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void Build_DST_Fruehjahrsuebergang_KeinOffByOne()
    {
        // 2026: letzter Sonntag im März = 29.03.2026 (DST start).
        // Wir testen Montag 30.03., damit die DST-Stunde sicher hinter uns liegt.
        var clock = BuildClockAt(2026, 3, 30, 10, 0, Berlin);

        var snap = clock.Build();

        snap.Today.Should().Be(new DateOnly(2026, 3, 30));
        snap.WeekdayDe.Should().Be("Montag");
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 5)));
        snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 4, 4), new DateOnly(2026, 4, 5)));
    }

    [Fact]
    public void Build_DST_Herbstuebergang_KeinOffByOne()
    {
        // 2026: letzter Sonntag im Oktober = 25.10.2026 (DST end).
        var clock = BuildClockAt(2026, 10, 26, 10, 0, Berlin);

        var snap = clock.Build();

        snap.Today.Should().Be(new DateOnly(2026, 10, 26));
        snap.WeekdayDe.Should().Be("Montag");
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1)));
    }
}
