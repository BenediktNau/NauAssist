using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CreateEventHandlerTests
{
    [Fact]
    public async Task Handle_CreatesEvent_AndReturnsProviderId()
    {
        var provider = new FakeCalendarProvider();
        var handler = new CreateEventHandler(provider);

        var response = await handler.Handle(new CreateEventRequest(
            Title: "Pierre",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        response.EventId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_RejectsEmptyTitle()
    {
        var provider = new FakeCalendarProvider();
        var handler = new CreateEventHandler(provider);

        var act = async () => await handler.Handle(new CreateEventRequest(
            Title: "",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Title*");
    }

    [Fact]
    public async Task Handle_RejectsEndBeforeStart()
    {
        var provider = new FakeCalendarProvider();
        var handler = new CreateEventHandler(provider);

        var act = async () => await handler.Handle(new CreateEventRequest(
            Title: "X",
            Start: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*End*");
    }
}
