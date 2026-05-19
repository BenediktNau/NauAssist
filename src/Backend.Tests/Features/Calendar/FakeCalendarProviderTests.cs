using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class FakeCalendarProviderTests
{
    [Fact]
    public async Task GetEvents_ReturnsSeededEventsInRange()
    {
        var provider = new FakeCalendarProvider();
        var inRange = new CalendarEvent("e1", "A",
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-25T11:00:00+02:00"),
            null, null);
        var outOfRange = new CalendarEvent("e2", "B",
            DateTimeOffset.Parse("2026-06-25T10:00:00+02:00"),
            DateTimeOffset.Parse("2026-06-25T11:00:00+02:00"),
            null, null);
        provider.Seed(inRange, outOfRange);

        var events = await provider.GetEventsAsync(
            DateTimeOffset.Parse("2026-05-25T00:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-26T00:00:00+02:00"),
            CancellationToken.None);

        events.Should().ContainSingle(e => e.Id == "e1");
    }

    [Fact]
    public async Task CreateEvent_AppendsEventAndAssignsId()
    {
        var provider = new FakeCalendarProvider();

        var id = await provider.CreateEventAsync(new NewEvent(
            "Pierre",
            DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            null, null), CancellationToken.None);

        id.Should().NotBeNullOrEmpty();
        var events = await provider.GetEventsAsync(
            DateTimeOffset.Parse("2026-05-27T00:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-28T00:00:00+02:00"),
            CancellationToken.None);
        events.Should().ContainSingle(e => e.Id == id && e.Title == "Pierre");
    }

    [Fact]
    public async Task GetEvents_RangeOverlapMatching_IncludesPartialOverlap()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent("e1", "A",
            DateTimeOffset.Parse("2026-05-25T17:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-25T19:00:00+02:00"),
            null, null));

        var events = await provider.GetEventsAsync(
            DateTimeOffset.Parse("2026-05-25T18:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-25T20:00:00+02:00"),
            CancellationToken.None);

        events.Should().ContainSingle(e => e.Id == "e1");
    }
}
