using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NauAssist.Backend.Features.Events;

/// <summary>Ein server-initiiertes Ereignis für die offene PWA (SSE-Frame: EventName + fertiges Daten-JSON).</summary>
public sealed record ProactiveEvent(string EventName, string DataJson);

/// <summary>
/// Verteilt server-initiierte Ereignisse an offene <c>/api/events</c>-Streams, strikt pro User.
/// Singleton; Publisher (z.B. WatchJobNotifier) und Subscriber (SSE-Endpoint) sind entkoppelt.
/// Bounded Channel mit DropOldest: ein hängender Client staut keinen Speicher auf —
/// die UI lädt ohnehin per Query-Invalidierung nach, verlorene Events sind verschmerzbar.
/// </summary>
public sealed class ProactiveEventBroker
{
    private const int BufferPerSubscriber = 16;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ProactiveEvent>>> _subscribers = new();

    public Subscription Subscribe(string userId)
    {
        var channel = Channel.CreateBounded<ProactiveEvent>(new BoundedChannelOptions(BufferPerSubscriber)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        _subscribers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<ProactiveEvent>>())[id] = channel;
        return new Subscription(this, userId, id, channel.Reader);
    }

    /// <summary>Liefert die Zahl der Subscriber, die das Ereignis angenommen haben.</summary>
    public int Publish(string userId, ProactiveEvent ev)
    {
        if (!_subscribers.TryGetValue(userId, out var perUser)) return 0;
        var delivered = 0;
        foreach (var channel in perUser.Values)
        {
            if (channel.Writer.TryWrite(ev)) delivered++;
        }

        return delivered;
    }

    private void Unsubscribe(string userId, Guid id)
    {
        if (_subscribers.TryGetValue(userId, out var perUser) && perUser.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly ProactiveEventBroker _broker;
        private readonly string _userId;
        private readonly Guid _id;

        internal Subscription(ProactiveEventBroker broker, string userId, Guid id, ChannelReader<ProactiveEvent> reader)
        {
            _broker = broker;
            _userId = userId;
            _id = id;
            Reader = reader;
        }

        public ChannelReader<ProactiveEvent> Reader { get; }

        public void Dispose() => _broker.Unsubscribe(_userId, _id);
    }
}
