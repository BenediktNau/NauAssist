using AwesomeAssertions;
using NauAssist.Backend.Features.Events;

namespace NauAssist.Backend.Tests.Features.Events;

public sealed class ProactiveEventBrokerTests
{
    [Fact]
    public async Task Publish_ReachesSubscriberOfSameUser()
    {
        var broker = new ProactiveEventBroker();
        using var sub = broker.Subscribe("user-a");

        var delivered = broker.Publish("user-a", new ProactiveEvent("chat_message", """{"messageId":1}"""));

        delivered.Should().Be(1);
        var ev = await sub.Reader.ReadAsync(CancellationToken.None);
        ev.EventName.Should().Be("chat_message");
        ev.DataJson.Should().Contain("\"messageId\":1");
    }

    [Fact]
    public void Publish_DoesNotCrossUsers()
    {
        var broker = new ProactiveEventBroker();
        using var subB = broker.Subscribe("user-b");

        var delivered = broker.Publish("user-a", new ProactiveEvent("chat_message", "{}"));

        delivered.Should().Be(0);
        subB.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_WithoutSubscribers_ReturnsZero()
    {
        var broker = new ProactiveEventBroker();
        broker.Publish("niemand", new ProactiveEvent("x", "{}")).Should().Be(0);
    }

    [Fact]
    public void DisposedSubscription_NoLongerReceives()
    {
        var broker = new ProactiveEventBroker();
        var sub = broker.Subscribe("user-a");
        sub.Dispose();

        broker.Publish("user-a", new ProactiveEvent("x", "{}")).Should().Be(0);
    }
}
