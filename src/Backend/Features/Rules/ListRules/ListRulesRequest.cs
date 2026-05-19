using Mediator;

namespace NauAssist.Backend.Features.Rules.ListRules;

public sealed record ListRulesRequest() : IRequest<ListRulesResponse>;

public sealed record ListRulesResponse(IReadOnlyList<Rule> Rules);
