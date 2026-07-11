using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Features.Events;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class EventsEndpointsTests
{
    [Fact]
    public async Task EventsStream_DeliversPublishedEventForOwnUser()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await client.GetAsync(
            "/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        // Default-User ermitteln (anonyme Requests laufen als dieser User).
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            userId = scope.ServiceProvider.GetRequiredService<IUserContext>().UserId;
        }

        // Bis der Endpoint subscribed hat, kann Publish ins Leere gehen — kurz nachliefern.
        var broker = factory.Services.GetRequiredService<ProactiveEventBroker>();
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 100 && !cts.IsCancellationRequested; i++)
            {
                if (broker.Publish(userId, new ProactiveEvent("chat_message", """{"messageId":42}""")) > 0) return;
                await Task.Delay(50, cts.Token);
            }
        }, cts.Token);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var received = new StringBuilder();
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            received.AppendLine(line);
            if (received.ToString().Contains("\"messageId\":42")) break;
        }

        received.ToString().Should().Contain("event: chat_message").And.Contain("\"messageId\":42");
    }
}
