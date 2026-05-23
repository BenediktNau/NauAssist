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
    public void CalendarEvent_IsAllDay_DefaultsToFalse()
    {
        var ev = new CalendarEvent(
            Id: "e",
            Title: "Sprint",
            Start: DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"),
            Description: null,
            Location: null);

        ev.IsAllDay.Should().BeFalse();
    }

    [Fact]
    public void NewEvent_IsAllDay_DefaultsToFalse_AndCanBeTrue()
    {
        var regular = new NewEvent(
            Title: "Lunch",
            Start: DateTimeOffset.Parse("2026-05-27T12:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T13:00:00+02:00"),
            Description: null,
            Location: null);

        regular.IsAllDay.Should().BeFalse();

        var allDay = new NewEvent(
            Title: "Urlaub",
            Start: DateTimeOffset.Parse("2026-06-01T00:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-06-02T00:00:00+02:00"),
            Description: null,
            Location: null,
            IsAllDay: true);

        allDay.IsAllDay.Should().BeTrue();
    }
}
