using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;

namespace NauAssist.Backend.Features.Rules.AddRule;

public sealed class AddRuleHandler : IRequestHandler<AddRuleRequest, AddRuleResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RuleRepository _repo;
    private readonly AuditLogRepository _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<AddRuleHandler> _logger;

    public AddRuleHandler(
        RuleRepository repo,
        AuditLogRepository audit,
        Func<DateTimeOffset> clock,
        ILogger<AddRuleHandler> logger)
    {
        _repo = repo;
        _audit = audit;
        _clock = clock;
        _logger = logger;
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

        await TryWriteAuditAsync(
            toolName: "add_rule",
            argsJson: JsonSerializer.Serialize(request, JsonOptions),
            resultJson: JsonSerializer.Serialize(new { ruleId = saved.Id }, JsonOptions),
            cancellationToken);

        return new AddRuleResponse(saved);
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
