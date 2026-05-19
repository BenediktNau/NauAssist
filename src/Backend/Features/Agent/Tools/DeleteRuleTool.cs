using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Rules.DeleteRule;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class DeleteRuleTool : ITool
{
    public string Name => "delete_rule";
    public string Description => "Löscht eine Regel anhand ihrer ID (vom list_rules-Tool erhältlich).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": { "rule_id": { "type": "integer" } },
          "required": ["rule_id"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public DeleteRuleTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.GetProperty("rule_id").GetInt64();
        var response = await _mediator.Send(new DeleteRuleRequest(id), ct);
        return JsonSerializer.SerializeToElement(new { deleted = response.Deleted });
    }
}
