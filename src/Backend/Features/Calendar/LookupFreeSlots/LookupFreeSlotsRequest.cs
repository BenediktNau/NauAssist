using Mediator;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Features.Calendar.LookupFreeSlots;

public sealed record LookupFreeSlotsRequest(
    DateTimeOffset From,
    DateTimeOffset To,
    int DurationMinutes) : IRequest<LookupFreeSlotsResponse>;

public sealed record LookupFreeSlotsResponse(IReadOnlyList<SlotAnnotation> Annotations);
