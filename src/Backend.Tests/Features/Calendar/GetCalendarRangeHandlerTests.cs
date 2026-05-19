using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class GetCalendarRangeHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsEventsFromProvider()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent("e1", "A",
            DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"),
            null, null));
        var handler = new GetCalendarRangeHandler(provider);

        var response = await handler.Handle(new GetCalendarRangeRequest(
            From: DateTimeOffset.Parse("2026-05-27T00:00:00+02:00"),
            To: DateTimeOffset.Parse("2026-05-28T00:00:00+02:00")), CancellationToken.None);

        response.Events.Should().ContainSingle(e => e.Id == "e1");
    }

    [Fact]
    public async Task Handle_RejectsInvalidRange()
    {
        var provider = new FakeCalendarProvider();
        var handler = new GetCalendarRangeHandler(provider);

        var act = async () => await handler.Handle(new GetCalendarRangeRequest(
            From: DateTimeOffset.Parse("2026-05-28T00:00:00+02:00"),
            To: DateTimeOffset.Parse("2026-05-27T00:00:00+02:00")), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
