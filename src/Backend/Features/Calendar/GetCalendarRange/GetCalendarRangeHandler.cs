using Mediator;

namespace NauAssist.Backend.Features.Calendar.GetCalendarRange;

public sealed class GetCalendarRangeHandler : IRequestHandler<GetCalendarRangeRequest, GetCalendarRangeResponse>
{
    private readonly ICalendarProvider _calendar;

    public GetCalendarRangeHandler(ICalendarProvider calendar)
    {
        _calendar = calendar;
    }

    public async ValueTask<GetCalendarRangeResponse> Handle(GetCalendarRangeRequest request, CancellationToken cancellationToken)
    {
        if (request.To <= request.From)
        {
            throw new ArgumentException("To muss nach From liegen.", nameof(request));
        }

        var events = await _calendar.GetEventsAsync(request.From, request.To, cancellationToken);
        return new GetCalendarRangeResponse(events);
    }
}
