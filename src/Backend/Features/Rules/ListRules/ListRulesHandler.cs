using Mediator;

namespace NauAssist.Backend.Features.Rules.ListRules;

public sealed class ListRulesHandler : IRequestHandler<ListRulesRequest, ListRulesResponse>
{
    private readonly RuleRepository _repo;

    public ListRulesHandler(RuleRepository repo)
    {
        _repo = repo;
    }

    public async ValueTask<ListRulesResponse> Handle(ListRulesRequest request, CancellationToken cancellationToken)
    {
        var rules = await _repo.ListAllAsync(cancellationToken);
        return new ListRulesResponse(rules);
    }
}
