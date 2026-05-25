using Mediator;

namespace NauAssist.Backend.Features.Calendar.DeleteEvent;

public sealed record DeleteEventRequest(string EventId, EventScope Scope = EventScope.Instance) : IRequest<DeleteEventResponse>;

public sealed record DeleteEventResponse(string EventId, EventScope Scope);
