using Mediator;

namespace NauAssist.Backend.Features.Calendar.CreateEvent;

public sealed class CreateEventHandler : IRequestHandler<CreateEventRequest, CreateEventResponse>
{
    private readonly ICalendarProvider _calendar;

    public CreateEventHandler(ICalendarProvider calendar)
    {
        _calendar = calendar;
    }

    public async ValueTask<CreateEventResponse> Handle(CreateEventRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Title darf nicht leer sein.", nameof(request));
        }

        if (request.End <= request.Start)
        {
            throw new ArgumentException("End muss nach Start liegen.", nameof(request));
        }

        var newEvent = new NewEvent(
            Title: request.Title.Trim(),
            Start: request.Start,
            End: request.End,
            Description: request.Description,
            Location: request.Location);

        var id = await _calendar.CreateEventAsync(newEvent, cancellationToken);
        return new CreateEventResponse(id);
    }
}
