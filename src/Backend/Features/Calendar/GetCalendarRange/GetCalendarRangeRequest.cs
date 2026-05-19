using Mediator;

namespace NauAssist.Backend.Features.Calendar.GetCalendarRange;

public sealed record GetCalendarRangeRequest(
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<GetCalendarRangeResponse>;

public sealed record GetCalendarRangeResponse(IReadOnlyList<CalendarEvent> Events);
