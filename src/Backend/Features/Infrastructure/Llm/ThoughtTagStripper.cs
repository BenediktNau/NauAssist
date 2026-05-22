using System.Text;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

/// <summary>
/// Entfernt Gemma-typische &lt;thought&gt;...&lt;/thought&gt;-Reasoning-Blöcke
/// aus einem Streaming-Text. Funktioniert auch, wenn Tags über mehrere Chunks
/// gesplittet sind.
/// </summary>
public sealed class ThoughtTagStripper
{
    private const string OpenTag = "<thought>";
    private const string CloseTag = "</thought>";

    private bool _insideThought;
    private string _pending = "";

    public string Process(string chunk)
    {
        var buffer = _pending + chunk;
        _pending = "";
        var output = new StringBuilder();

        while (buffer.Length > 0)
        {
            if (_insideThought)
            {
                var closeIdx = buffer.IndexOf(CloseTag, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    buffer = buffer[(closeIdx + CloseTag.Length)..];
                    _insideThought = false;
                }
                else
                {
                    _pending = LongestSuffixThatIsPrefixOf(buffer, CloseTag);
                    return output.ToString();
                }
            }
            else
            {
                var openIdx = buffer.IndexOf(OpenTag, StringComparison.Ordinal);
                if (openIdx >= 0)
                {
                    output.Append(buffer.AsSpan(0, openIdx));
                    buffer = buffer[(openIdx + OpenTag.Length)..];
                    _insideThought = true;
                }
                else
                {
                    var suffix = LongestSuffixThatIsPrefixOf(buffer, OpenTag);
                    var emitLen = buffer.Length - suffix.Length;
                    output.Append(buffer.AsSpan(0, emitLen));
                    _pending = suffix;
                    return output.ToString();
                }
            }
        }
        return output.ToString();
    }

    /// <summary>Gibt zurück, was noch im Buffer hängt. Bei Stream-Ende: unfertige Thoughts werden verworfen, sonstige Pending-Bytes (z.B. einsames "&lt;") werden als Text geflushed.</summary>
    public string Flush()
    {
        if (_insideThought)
        {
            _pending = "";
            return "";
        }
        var leftover = _pending;
        _pending = "";
        return leftover;
    }

    private static string LongestSuffixThatIsPrefixOf(string text, string tag)
    {
        var max = Math.Min(text.Length, tag.Length - 1);
        for (var i = max; i > 0; i--)
        {
            if (tag.AsSpan(0, i).SequenceEqual(text.AsSpan(text.Length - i)))
            {
                return text[^i..];
            }
        }
        return "";
    }
}
