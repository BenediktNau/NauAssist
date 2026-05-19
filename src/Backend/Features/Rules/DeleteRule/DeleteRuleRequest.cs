using Mediator;

namespace NauAssist.Backend.Features.Rules.DeleteRule;

public sealed record DeleteRuleRequest(long Id) : IRequest<DeleteRuleResponse>;

public sealed record DeleteRuleResponse(bool Deleted);
