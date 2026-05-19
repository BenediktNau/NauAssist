using Mediator;

namespace NauAssist.Backend.Features.Rules.DeleteRule;

public sealed class DeleteRuleHandler : IRequestHandler<DeleteRuleRequest, DeleteRuleResponse>
{
    private readonly RuleRepository _repo;

    public DeleteRuleHandler(RuleRepository repo)
    {
        _repo = repo;
    }

    public async ValueTask<DeleteRuleResponse> Handle(DeleteRuleRequest request, CancellationToken cancellationToken)
    {
        var deleted = await _repo.DeleteAsync(request.Id, cancellationToken);
        return new DeleteRuleResponse(deleted);
    }
}
