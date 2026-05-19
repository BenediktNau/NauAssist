using Mediator;

namespace NauAssist.Backend.Features.Rules.AddRule;

public sealed class AddRuleHandler : IRequestHandler<AddRuleRequest, AddRuleResponse>
{
    private readonly RuleRepository _repo;
    private readonly Func<DateTimeOffset> _clock;

    public AddRuleHandler(RuleRepository repo, Func<DateTimeOffset> clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public async ValueTask<AddRuleResponse> Handle(AddRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Text darf nicht leer sein.", nameof(request));
        }

        if (request.TimeRangeStart.HasValue && request.TimeRangeEnd.HasValue
            && request.TimeRangeEnd.Value <= request.TimeRangeStart.Value)
        {
            throw new ArgumentException("TimeRangeEnd muss nach TimeRangeStart liegen.", nameof(request));
        }

        if (request.DaysOfWeek == DayOfWeekFlags.None)
        {
            throw new ArgumentException("DaysOfWeek muss mindestens einen Tag enthalten.", nameof(request));
        }

        var draft = new Rule(
            Id: 0,
            Text: request.Text.Trim(),
            DaysOfWeek: request.DaysOfWeek,
            TimeRangeStart: request.TimeRangeStart,
            TimeRangeEnd: request.TimeRangeEnd,
            Hardness: request.Hardness,
            CreatedAt: _clock());

        var saved = await _repo.AddAsync(draft, cancellationToken);
        return new AddRuleResponse(saved);
    }
}
