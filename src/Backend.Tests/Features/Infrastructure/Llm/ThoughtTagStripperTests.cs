using AwesomeAssertions;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Llm;

public sealed class ThoughtTagStripperTests
{
    [Fact]
    public void PlainText_PassesThrough()
    {
        var s = new ThoughtTagStripper();
        s.Process("Hallo Benedikt!").Should().Be("Hallo Benedikt!");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void SingleChunk_WithThought_StripsTagAndContent()
    {
        var s = new ThoughtTagStripper();
        var output = s.Process("<thought>plane Antwort</thought>Hallo!");
        output.Should().Be("Hallo!");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void OpenTag_SplitAcrossChunks_StripsCorrectly()
    {
        var s = new ThoughtTagStripper();
        var a = s.Process("Vor<thou");
        var b = s.Process("ght>geheim</thought>nach");
        (a + b).Should().Be("Vornach");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void CloseTag_SplitAcrossChunks_StripsCorrectly()
    {
        var s = new ThoughtTagStripper();
        var a = s.Process("<thought>noch denkend</thou");
        var b = s.Process("ght>Antwort!");
        (a + b).Should().Be("Antwort!");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void OpenTag_SplitAtEveryChar_StripsCorrectly()
    {
        var s = new ThoughtTagStripper();
        var input = "vor<thought>x</thought>nach";
        var output = string.Concat(input.Select(c => s.Process(c.ToString())));
        output.Should().Be("vornach");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void MultipleThoughts_AllStripped()
    {
        var s = new ThoughtTagStripper();
        var output = s.Process("<thought>a</thought>X<thought>b</thought>Y");
        output.Should().Be("XY");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void Ampersand_OrLooseLt_DoesNotBufferForever()
    {
        var s = new ThoughtTagStripper();
        var a = s.Process("a < b");
        a.Should().Be("a < b");
        var b = s.Process(" und c");
        b.Should().Be(" und c");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void ForeignTag_PassesThrough()
    {
        var s = new ThoughtTagStripper();
        s.Process("<b>fett</b>").Should().Be("<b>fett</b>");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void PartialOpenTagAtEnd_BuffersThenFlushesIfNotMatching()
    {
        var s = new ThoughtTagStripper();
        var a = s.Process("text<thou");
        a.Should().Be("text");
        // Stream endet, ohne dass `<thought>` vollständig wird.
        s.Flush().Should().Be("<thou");
    }

    [Fact]
    public void PartialOpenTagAtEnd_BecomesPlainText_WhenContinuationDiffers()
    {
        var s = new ThoughtTagStripper();
        var a = s.Process("text<thou");
        var b = s.Process("sand");
        (a + b).Should().Be("text<thousand");
        s.Flush().Should().BeEmpty();
    }

    [Fact]
    public void UnterminatedThought_AtStreamEnd_IsDiscarded()
    {
        var s = new ThoughtTagStripper();
        var a = s.Process("vor<thought>endlos");
        a.Should().Be("vor");
        s.Flush().Should().BeEmpty();
    }
}
