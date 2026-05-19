using Mediator;

namespace NauAssist.Backend.Features.Rules.AddRule;

public sealed record AddRuleRequest(
    string Text,
    DayOfWeekFlags DaysOfWeek,
    TimeOnly? TimeRangeStart,
    TimeOnly? TimeRangeEnd,
    RuleHardness Hardness) : IRequest<AddRuleResponse>;

public sealed record AddRuleResponse(Rule Rule);
