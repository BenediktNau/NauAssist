using System.Text;
using AwesomeAssertions;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Chat;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class SseWriterTests
{
    [Fact]
    public async Task WriteAsync_TokenEvent_ProducesExactWireFormat()
    {
        using var memory = new MemoryStream();
        var writer = new SseWriter(memory);

        await writer.WriteAsync(new SseToken("hallo"), CancellationToken.None);

        var s = Encoding.UTF8.GetString(memory.ToArray());
        s.Should().Be("event: token\ndata: {\"text\":\"hallo\"}\n\n");
    }

    [Fact]
    public async Task WriteAsync_DoneEvent_IncludesMessageId()
    {
        using var memory = new MemoryStream();
        var writer = new SseWriter(memory);

        await writer.WriteAsync(new SseDone(42), CancellationToken.None);

        var s = Encoding.UTF8.GetString(memory.ToArray());
        s.Should().Contain("event: done\n");
        s.Should().Contain("\"messageId\":42");
    }

    [Fact]
    public async Task WriteAsync_ToolStartedAndFinished_SerializeNameAndOk()
    {
        using var memory = new MemoryStream();
        var writer = new SseWriter(memory);

        await writer.WriteAsync(new SseToolStarted("create_event"), CancellationToken.None);
        await writer.WriteAsync(new SseToolFinished("create_event", true), CancellationToken.None);

        var s = Encoding.UTF8.GetString(memory.ToArray());
        s.Should().Contain("event: tool_started\ndata: {\"name\":\"create_event\"}");
        s.Should().Contain("event: tool_finished\ndata: {\"name\":\"create_event\",\"ok\":true}");
    }

    [Fact]
    public async Task WriteAsync_ProposalsEvent_SerializesSlots()
    {
        using var memory = new MemoryStream();
        var writer = new SseWriter(memory);
        var slots = new[]
        {
            new SlotInfo(
                DateTimeOffset.Parse("2026-05-20T09:00:00Z"),
                DateTimeOffset.Parse("2026-05-20T10:00:00Z"),
                null),
        };

        await writer.WriteAsync(new SseProposals(slots), CancellationToken.None);

        var s = Encoding.UTF8.GetString(memory.ToArray());
        s.Should().StartWith("event: proposals\ndata: [");
        s.Should().Contain("2026-05-20T09:00:00");
    }

    [Fact]
    public async Task WriteAsync_ErrorEvent_IncludesMessageAndCorrelationId()
    {
        using var memory = new MemoryStream();
        var writer = new SseWriter(memory);

        await writer.WriteAsync(new SseError("Boom", "corr-123"), CancellationToken.None);

        var s = Encoding.UTF8.GetString(memory.ToArray());
        s.Should().Contain("event: error\n");
        s.Should().Contain("\"message\":\"Boom\"");
        s.Should().Contain("\"correlationId\":\"corr-123\"");
    }
}
