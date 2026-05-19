using Mediator;

namespace NauAssist.Backend.Features.Calendar.CreateEvent;

public sealed record CreateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location) : IRequest<CreateEventResponse>;

public sealed record CreateEventResponse(string EventId);
