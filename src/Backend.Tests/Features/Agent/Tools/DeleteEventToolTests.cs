using System.Text.Json;
using AwesomeAssertions;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.DeleteEvent;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class DeleteEventToolTests
{
    [Fact]
    public async Task Execute_NoScope_DefaultsToInstance()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<DeleteEventRequest, DeleteEventResponse>(
            new DeleteEventResponse("ev-1", EventScope.Instance));

        var tool = new DeleteEventTool(mediator);
        var args = JsonDocument.Parse("""{ "event_id": "ev-1" }""").RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        var sent = mediator.SentRequests.OfType<DeleteEventRequest>().Last();
        sent.Scope.Should().Be(EventScope.Instance);
    }

    [Fact]
    public async Task Execute_ScopeSeries_ForwardsSeries()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<DeleteEventRequest, DeleteEventResponse>(
            new DeleteEventResponse("ev-1", EventScope.Series));

        var tool = new DeleteEventTool(mediator);
        var args = JsonDocument.Parse("""{ "event_id": "ev-1", "scope": "series" }""").RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        var sent = mediator.SentRequests.OfType<DeleteEventRequest>().Last();
        sent.Scope.Should().Be(EventScope.Series);
    }

    [Fact]
    public async Task Execute_UnknownScope_Throws()
    {
        var mediator = new FakeMediator();
        var tool = new DeleteEventTool(mediator);
        var args = JsonDocument.Parse("""{ "event_id": "ev-1", "scope": "all" }""").RootElement;

        var act = async () => await tool.ExecuteAsync(args, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
