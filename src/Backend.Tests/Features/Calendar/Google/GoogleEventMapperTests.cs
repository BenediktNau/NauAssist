using FluentAssertions;
using Google.Apis.Calendar.v3.Data;
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Tests.Features.Calendar.Google;

public sealed class GoogleEventMapperTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void Map_DateTimeEvent_NotAllDay()
    {
        var e = new Event
        {
            Id = "e1",
            Summary = "Meeting",
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-05-27T10:00:00+02:00") },
            End   = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-05-27T11:00:00+02:00") },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().NotBeNull();
        result!.IsAllDay.Should().BeFalse();
        result.Start.Should().Be(DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"));
        result.End.Should().Be(DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"));
    }

    [Fact]
    public void Map_DateOnlyEvent_SingleDay_IsAllDay_LocalMidnight()
    {
        var e = new Event
        {
            Id = "e-urlaub",
            Summary = "Urlaub",
            Start = new EventDateTime { Date = "2026-06-01" },
            End   = new EventDateTime { Date = "2026-06-02" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().NotBeNull();
        result!.IsAllDay.Should().BeTrue();
        result.Start.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        result.End.Should().Be(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Map_DateOnlyEvent_MultiDay_EndIsExclusiveNextDay()
    {
        // Google-Konvention: Schulung 27.5.–29.5. → Date-End = 30.5. (exklusiv).
        var e = new Event
        {
            Id = "e-schulung",
            Summary = "Schulung Köln",
            Start = new EventDateTime { Date = "2026-05-27" },
            End   = new EventDateTime { Date = "2026-05-30" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().NotBeNull();
        result!.IsAllDay.Should().BeTrue();
        result.Start.Should().Be(new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)));
        result.End.Should().Be(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Map_DateOnly_DST_SpringForward_UsesCorrectOffset()
    {
        // 2026: DST-Start 29.03. Wir testen ein All-Day genau am Übergangstag.
        var e = new Event
        {
            Id = "e-dst",
            Summary = "DST-Tag",
            Start = new EventDateTime { Date = "2026-03-29" },
            End   = new EventDateTime { Date = "2026-03-30" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result!.IsAllDay.Should().BeTrue();
        // 29.3.2026 00:00 Berlin liegt noch in Winterzeit (UTC+1).
        result.Start.Offset.Should().Be(TimeSpan.FromHours(1));
        // 30.3.2026 00:00 Berlin liegt nach DST-Start (UTC+2).
        result.End.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Map_DateOnly_DST_FallBack_UsesCorrectOffset()
    {
        // 2026: DST-Ende 25.10.
        var e = new Event
        {
            Id = "e-dst-fall",
            Summary = "DST-Tag",
            Start = new EventDateTime { Date = "2026-10-25" },
            End   = new EventDateTime { Date = "2026-10-26" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result!.IsAllDay.Should().BeTrue();
        // 25.10.2026 00:00 Berlin: noch Sommerzeit (UTC+2).
        result.Start.Offset.Should().Be(TimeSpan.FromHours(2));
        // 26.10.2026 00:00 Berlin: Winterzeit (UTC+1).
        result.End.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Map_BothFieldsMissing_ReturnsNull()
    {
        var e = new Event { Id = "e-leer", Start = new EventDateTime(), End = new EventDateTime() };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().BeNull();
    }
}
