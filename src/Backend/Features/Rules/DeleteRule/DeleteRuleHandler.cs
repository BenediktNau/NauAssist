using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;

namespace NauAssist.Backend.Features.Rules.DeleteRule;

public sealed class DeleteRuleHandler : IRequestHandler<DeleteRuleRequest, DeleteRuleResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RuleRepository _repo;
    private readonly AuditLogRepository _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<DeleteRuleHandler> _logger;

    public DeleteRuleHandler(
        RuleRepository repo,
        AuditLogRepository audit,
        Func<DateTimeOffset> clock,
        ILogger<DeleteRuleHandler> logger)
    {
        _repo = repo;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<DeleteRuleResponse> Handle(DeleteRuleRequest request, CancellationToken cancellationToken)
    {
        var deleted = await _repo.DeleteAsync(request.Id, cancellationToken);

        await TryWriteAuditAsync(
            toolName: "delete_rule",
            argsJson: JsonSerializer.Serialize(new { id = request.Id }, JsonOptions),
            resultJson: JsonSerializer.Serialize(new { deleted }, JsonOptions),
            cancellationToken);

        return new DeleteRuleResponse(deleted);
    }

    private async Task TryWriteAuditAsync(
        string toolName, string argsJson, string resultJson, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new AuditEntry(
                Id: 0,
                TriggeringMessageId: null,
                ToolName: toolName,
                ToolArgsJson: argsJson,
                ResultJson: resultJson,
                ProviderEventId: null,
                CreatedAt: _clock()),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit-Eintrag für {Tool} fehlgeschlagen.", toolName);
        }
    }
}
