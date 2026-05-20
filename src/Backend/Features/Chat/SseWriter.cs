using System.Text;
using System.Text.Json;

namespace NauAssist.Backend.Features.Chat;

public sealed class SseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Stream _stream;

    public SseWriter(Stream stream)
    {
        _stream = stream;
    }

    public async Task WriteAsync(SseEvent ev, CancellationToken ct)
    {
        var dataJson = ev switch
        {
            SseToken t => JsonSerializer.Serialize(new { text = t.Text }, JsonOptions),
            SseToolStarted ts => JsonSerializer.Serialize(new { name = ts.Name }, JsonOptions),
            SseToolFinished tf => JsonSerializer.Serialize(new { name = tf.Name, ok = tf.Ok }, JsonOptions),
            SseProposals p => JsonSerializer.Serialize(p.Slots, JsonOptions),
            SseDone d => JsonSerializer.Serialize(new { messageId = d.MessageId }, JsonOptions),
            SseError e => JsonSerializer.Serialize(new { message = e.Message, correlationId = e.CorrelationId }, JsonOptions),
            _ => throw new InvalidOperationException($"Unbekannter SseEvent-Typ: {ev.GetType().Name}"),
        };

        var frame = $"event: {ev.EventName}\ndata: {dataJson}\n\n";
        var bytes = Encoding.UTF8.GetBytes(frame);
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }
}
