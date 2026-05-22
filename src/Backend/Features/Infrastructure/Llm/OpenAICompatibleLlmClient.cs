using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

public sealed class OpenAICompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OpenAICompatibleLlmOptions _options;
    private readonly ILogger<OpenAICompatibleLlmClient> _logger;

    public OpenAICompatibleLlmClient(
        HttpClient http,
        OpenAICompatibleLlmOptions options,
        ILogger<OpenAICompatibleLlmClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var payload = BuildPayload(messages, tools);
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload),
        };

        using var initialCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        initialCts.CancelAfter(TimeSpan.FromSeconds(_options.InitialTimeoutSeconds));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, initialCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var toolCallBuffer = new Dictionary<int, ToolCallBuilder>();
        var thoughtStripper = new ThoughtTagStripper();

        while (true)
        {
            using var tokenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tokenCts.CancelAfter(TimeSpan.FromSeconds(_options.TokenTimeoutSeconds));

            var line = await reader.ReadLineAsync(tokenCts.Token);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    var stripped = thoughtStripper.Process(text);
                    if (stripped.Length > 0)
                    {
                        yield return new TextDeltaChunk(stripped);
                    }
                }
            }

            if (delta.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tcEl in toolCallsEl.EnumerateArray())
                {
                    var index = tcEl.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                    if (!toolCallBuffer.TryGetValue(index, out var builder))
                    {
                        builder = new ToolCallBuilder();
                        toolCallBuffer[index] = builder;
                    }

                    if (tcEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        builder.Id ??= idEl.GetString();

                    if (tcEl.TryGetProperty("function", out var fnEl))
                    {
                        if (fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            builder.Name ??= nameEl.GetString();
                        if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            builder.ArgumentsBuffer.Append(argsEl.GetString());
                    }
                }
            }

            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String && fr.GetString() == "tool_calls")
            {
                foreach (var b in toolCallBuffer.Values)
                {
                    if (b.Name is null) continue;
                    JsonElement args;
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(b.ArgumentsBuffer.Length > 0 ? b.ArgumentsBuffer.ToString() : "{}");
                        args = argsDoc.RootElement.Clone();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Tool-Call-Args sind kein gültiges JSON: '{Raw}'", b.ArgumentsBuffer);
                        continue;
                    }
                    yield return new ToolCallChunk(new LlmToolCall(b.Id ?? Guid.NewGuid().ToString(), b.Name, args));
                }
                toolCallBuffer.Clear();
            }
        }

        var leftover = thoughtStripper.Flush();
        if (leftover.Length > 0)
        {
            yield return new TextDeltaChunk(leftover);
        }
    }

    private object BuildPayload(IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition> tools)
    {
        var serializedMessages = new List<object>();

        if (!string.IsNullOrEmpty(_options.SystemPrompt))
        {
            serializedMessages.Add(new { role = "system", content = _options.SystemPrompt });
        }

        foreach (var m in messages)
        {
            var dict = new Dictionary<string, object?> { ["role"] = m.Role };
            if (m.Content is not null) dict["content"] = m.Content;
            if (m.ToolCallId is not null) dict["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is not null)
            {
                dict["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments.GetRawText() },
                }).ToArray();
            }
            serializedMessages.Add(dict);
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = serializedMessages,
            ["stream"] = true,
        };

        if (_options.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        if (tools.Count > 0)
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonDocument.Parse(t.ParameterSchema.GetRawText()).RootElement,
                },
            }).ToArray();
        }

        if (_options.NumCtx is { } numCtx)
        {
            payload["options"] = new Dictionary<string, object?> { ["num_ctx"] = numCtx };
        }

        return payload;
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder ArgumentsBuffer { get; } = new();
    }
}
