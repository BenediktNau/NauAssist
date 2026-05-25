using Mediator;

namespace NauAssist.Backend.Features.Calendar.UpdateEvent;

public sealed record UpdateEventRequest(string EventId, EventUpdate Update, EventScope Scope = EventScope.Instance) : IRequest<UpdateEventResponse>;

public sealed record UpdateEventResponse(string EventId, EventScope Scope);
