using System.Text;
using NauAssist.Backend.Features.Events;
using NauAssist.Backend.Features.Infrastructure.Auth;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Schlanker SSE-Stream für server-initiierte Nachrichten an die offene PWA
/// (z.B. "Watch-Job gefeuert" ⇒ Chat-History live nachladen). Pro Verbindung eine
/// Broker-Subscription; ein Heartbeat-Kommentar alle 25 s hält Proxies die Verbindung offen.
/// </summary>
public static class EventsEndpoints
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (
            HttpContext ctx,
            ProactiveEventBroker broker,
            IUserContext user,
            CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";

            using var subscription = broker.Subscribe(user.UserId);
            Task<ProactiveEvent>? pending = null;
            try
            {
                await ctx.Response.Body.FlushAsync(ct);

                while (!ct.IsCancellationRequested)
                {
                    pending ??= subscription.Reader.ReadAsync(ct).AsTask();
                    var winner = await Task.WhenAny(pending, Task.Delay(Heartbeat, ct));

                    string frame;
                    if (winner == pending)
                    {
                        var ev = await pending;
                        pending = null;
                        frame = $"event: {ev.EventName}\ndata: {ev.DataJson}\n\n";
                    }
                    else
                    {
                        frame = ": ping\n\n";
                    }

                    await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client hat getrennt — normales Ende.
            }
            catch (IOException)
            {
                // Abrupter Verbindungsabbruch kann beim Schreiben als IOException auftreten,
                // bevor der CancellationToken feuert — ebenfalls normales Ende, kein Fehler.
            }
        });

        return app;
    }
}
