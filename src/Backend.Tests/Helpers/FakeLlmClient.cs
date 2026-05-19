using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// Skriptbarer LLM-Fake für deterministische Tests.
/// Pro Aufruf wird die nächste gequeuede Response konsumiert.
/// </summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<IReadOnlyList<LlmStreamChunk>> _scripted = new();
    private readonly List<CapturedCall> _captured = new();

    public IReadOnlyList<CapturedCall> CapturedCalls => _captured;

    public void QueueResponse(params LlmStreamChunk[] chunks)
    {
        _scripted.Enqueue(chunks);
    }

    public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _captured.Add(new CapturedCall(messages.ToList(), tools.ToList()));

        if (_scripted.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeLlmClient hat keine gequeuede Response — vor dem Aufruf QueueResponse aufrufen.");
        }

        var response = _scripted.Dequeue();
        foreach (var chunk in response)
        {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }

    public sealed record CapturedCall(
        IReadOnlyList<LlmMessage> Messages,
        IReadOnlyList<ToolDefinition> Tools);
}
