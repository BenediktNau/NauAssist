using System.Text.Json;
using AwesomeAssertions;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class FakeLlmClientTests
{
    [Fact]
    public async Task ChatStream_ReturnsScriptedChunks_InOrder()
    {
        var fake = new FakeLlmClient();
        fake.QueueResponse(new TextDeltaChunk("Hallo"), new TextDeltaChunk(" Welt"));

        var chunks = new List<LlmStreamChunk>();
        await foreach (var c in fake.ChatStreamAsync(
            new List<LlmMessage> { new("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None))
        {
            chunks.Add(c);
        }

        chunks.Should().HaveCount(2);
        chunks[0].Should().BeOfType<TextDeltaChunk>().Which.Text.Should().Be("Hallo");
        chunks[1].Should().BeOfType<TextDeltaChunk>().Which.Text.Should().Be(" Welt");
    }

    [Fact]
    public async Task ChatStream_ConsecutiveCalls_UseConsecutiveQueuedResponses()
    {
        var fake = new FakeLlmClient();
        fake.QueueResponse(new TextDeltaChunk("erste"));
        fake.QueueResponse(new TextDeltaChunk("zweite"));

        var first = await Collect(fake);
        var second = await Collect(fake);

        ((TextDeltaChunk)first[0]).Text.Should().Be("erste");
        ((TextDeltaChunk)second[0]).Text.Should().Be("zweite");
    }

    [Fact]
    public async Task ChatStream_WhenQueueEmpty_Throws()
    {
        var fake = new FakeLlmClient();

        var act = async () => await Collect(fake);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChatStream_CapturesMessagesAndToolsForInspection()
    {
        var fake = new FakeLlmClient();
        fake.QueueResponse(new TextDeltaChunk("ok"));
        var tools = new[] { new ToolDefinition("foo", "Foo desc", JsonDocument.Parse("{}").RootElement) };

        await Collect(fake, new List<LlmMessage> { new("user", "Test") }, tools);

        fake.CapturedCalls.Should().HaveCount(1);
        fake.CapturedCalls[0].Messages.Should().ContainSingle(m => m.Role == "user");
        fake.CapturedCalls[0].Tools.Should().ContainSingle(t => t.Name == "foo");
    }

    private static async Task<List<LlmStreamChunk>> Collect(
        FakeLlmClient fake,
        IReadOnlyList<LlmMessage>? msgs = null,
        IReadOnlyList<ToolDefinition>? tools = null)
    {
        var list = new List<LlmStreamChunk>();
        await foreach (var c in fake.ChatStreamAsync(
            msgs ?? Array.Empty<LlmMessage>(),
            tools ?? Array.Empty<ToolDefinition>(),
            CancellationToken.None))
        {
            list.Add(c);
        }
        return list;
    }
}
