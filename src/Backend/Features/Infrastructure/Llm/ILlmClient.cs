using System.Text.Json;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

/// <summary>
/// Schmaler Wrapper um einen OpenAI-kompatiblen Chat-Endpoint mit Streaming und Tool-Calls.
/// </summary>
public interface ILlmClient
{
    IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}

public sealed record LlmMessage(
    string Role,
    string? Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    string? ToolCallId = null);

public sealed record LlmToolCall(
    string Id,
    string Name,
    JsonElement Arguments);

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParameterSchema);

public abstract record LlmStreamChunk;
public sealed record TextDeltaChunk(string Text) : LlmStreamChunk;
public sealed record ToolCallChunk(LlmToolCall Call) : LlmStreamChunk;
