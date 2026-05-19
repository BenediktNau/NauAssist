using Mediator;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Features.Calendar.LookupFreeSlots;

public sealed class LookupFreeSlotsHandler : IRequestHandler<LookupFreeSlotsRequest, LookupFreeSlotsResponse>
{
    private readonly RuleRepository _ruleRepo;
    private readonly ICalendarProvider _calendar;
    private readonly FreeSlotCalculator _calculator;
    private readonly RuleApplicator _applicator;

    public LookupFreeSlotsHandler(
        RuleRepository ruleRepo,
        ICalendarProvider calendar,
        FreeSlotCalculator calculator,
        RuleApplicator applicator)
    {
        _ruleRepo = ruleRepo;
        _calendar = calendar;
        _calculator = calculator;
        _applicator = applicator;
    }

    public async ValueTask<LookupFreeSlotsResponse> Handle(LookupFreeSlotsRequest request, CancellationToken cancellationToken)
    {
        if (request.To <= request.From)
        {
            throw new ArgumentException("To muss nach From liegen.", nameof(request));
        }

        if (request.DurationMinutes <= 0)
        {
            throw new ArgumentException("DurationMinutes muss > 0 sein.", nameof(request));
        }

        var rules = await _ruleRepo.ListAllAsync(cancellationToken);
        var events = await _calendar.GetEventsAsync(request.From, request.To, cancellationToken);
        var candidates = _calculator.Calculate(request.From, request.To, events, request.DurationMinutes);
        var annotations = _applicator.Annotate(candidates, rules);

        return new LookupFreeSlotsResponse(annotations);
    }
}
