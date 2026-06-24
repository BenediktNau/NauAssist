using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Llm;

public sealed class OpenAICompatibleLlmClientTests
{
    [Fact]
    public async Task Payload_IncludesModelMessagesAndStreamTrue()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions(
                Model: "gemma4:26b",
                InitialTimeoutSeconds: 60,
                TokenTimeoutSeconds: 30,
                SystemPrompt: "You are a test.",
                Temperature: null,
                NumCtx: null),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        var payload = handler.LastBodyJson!.Value;
        payload.GetProperty("model").GetString().Should().Be("gemma4:26b");
        payload.GetProperty("stream").GetBoolean().Should().BeTrue();
        var messages = payload.GetProperty("messages").EnumerateArray().ToList();
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("You are a test.");
        messages[1].GetProperty("role").GetString().Should().Be("user");
        messages[1].GetProperty("content").GetString().Should().Be("Hi");
    }

    [Fact]
    public async Task Payload_OmitsOptions_WhenNumCtxAndTemperatureNull()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions("model", 60, 30, null, null, null),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        handler.LastBodyJson!.Value.TryGetProperty("options", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Payload_IncludesOptionsNumCtx_WhenSet()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions("model", 60, 30, null, null, NumCtx: 16384),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        handler.LastBodyJson!.Value.GetProperty("options").GetProperty("num_ctx").GetInt32().Should().Be(16384);
    }

    [Fact]
    public async Task Payload_IncludesTopLevelTemperature_WhenSet()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };
        var client = new OpenAICompatibleLlmClient(
            http,
            new OpenAICompatibleLlmOptions("model", 60, 30, null, Temperature: 0.3, NumCtx: null),
            NullLogger<OpenAICompatibleLlmClient>.Instance);

        await foreach (var _ in client.ChatStreamAsync(
            new[] { new LlmMessage("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None)) { }

        var payload = handler.LastBodyJson!.Value;
        payload.GetProperty("temperature").GetDouble().Should().BeApproximately(0.3, 0.0001);
        payload.TryGetProperty("options", out _).Should().BeFalse();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public JsonElement? LastBodyJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var raw = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            LastBodyJson = doc.RootElement.Clone();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
            };
            return response;
        }
    }
}
