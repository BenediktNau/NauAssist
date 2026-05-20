using System.Text;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>Liest einen SSE-Response-Body bis Stream-Ende und gibt (event, data)-Paare zurück.</summary>
public static class SseTestConsumer
{
    public static async Task<List<(string Event, string Data)>> ConsumeAsync(
        Stream body, CancellationToken ct)
    {
        var events = new List<(string, string)>();
        using var reader = new StreamReader(body, Encoding.UTF8);
        string? eventName = null;
        string? data = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (eventName is not null && data is not null)
                {
                    events.Add((eventName, data));
                }
                eventName = null;
                data = null;
                continue;
            }
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line[6..];
            }
        }
        return events;
    }
}
