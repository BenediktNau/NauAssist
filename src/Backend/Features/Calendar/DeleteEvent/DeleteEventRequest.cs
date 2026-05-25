using Mediator;

namespace NauAssist.Backend.Features.Calendar.DeleteEvent;

public sealed record DeleteEventRequest(string EventId) : IRequest<DeleteEventResponse>;

public sealed record DeleteEventResponse(string EventId);
