using System.Text.Json;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Agent;

/// <summary>
/// Ein Tool ist ein Adapter zwischen LLM-Tool-Call und einer fachlichen Aktion.
/// Die meisten Tools rufen intern Mediator.Send. Das spezielle present_proposals-Tool
/// wird vom AgentRunner direkt abgefangen.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParameterSchema { get; }
    Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct);

    ToolDefinition ToDefinition() => new(Name, Description, ParameterSchema);
}
