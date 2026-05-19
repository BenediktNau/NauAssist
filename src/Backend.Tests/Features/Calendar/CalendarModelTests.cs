using FluentAssertions;
using NauAssist.Backend.Features.Calendar;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CalendarModelTests
{
    [Fact]
    public void CalendarEvent_CanBeConstructed()
    {
        var ev = new CalendarEvent(
            Id: "google-event-123",
            Title: "Sprint Planning",
            Start: DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-25T11:00:00+02:00"),
            Description: "Quartalsplanung",
            Location: "MS Teams");

        ev.Id.Should().Be("google-event-123");
        ev.End.Should().BeAfter(ev.Start);
    }

    [Fact]
    public void NewEvent_CanBeConstructed_WithMinimalFields()
    {
        var ev = new NewEvent(
            Title: "Pierre",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null);

        ev.Title.Should().Be("Pierre");
        ev.Description.Should().BeNull();
    }

    [Fact]
    public void CalendarOptions_HasReasonableDefaults()
    {
        var opts = new CalendarOptions();

        opts.WorkingHoursStart.Should().Be("09:00");
        opts.WorkingHoursEnd.Should().Be("18:00");
        opts.DefaultDurationMinutes.Should().Be(60);
        opts.SearchHorizonDays.Should().Be(14);
    }
}
