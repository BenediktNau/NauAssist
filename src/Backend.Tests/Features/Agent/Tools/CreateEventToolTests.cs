using System.Text.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class CreateEventToolTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Execute_RegularEvent_PassesDateTimeOffsetThrough()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<CreateEventRequest, CreateEventResponse>(
            new CreateEventResponse("fake-1"));

        var tool = new CreateEventTool(mediator, Berlin);

        var args = JsonDocument.Parse("""
            {
              "title": "Meeting",
              "start": "2026-05-27T10:00:00+02:00",
              "end":   "2026-05-27T11:00:00+02:00"
            }
            """).RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        var sent = mediator.SentRequests.OfType<CreateEventRequest>().Last();
        sent.IsAllDay.Should().BeFalse();
        sent.Start.Should().Be(DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"));
        sent.End.Should().Be(DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"));
    }

    [Fact]
    public async Task Execute_AllDay_ParsesDateAndUsesLocalMidnight()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<CreateEventRequest, CreateEventResponse>(
            new CreateEventResponse("fake-1"));

        var tool = new CreateEventTool(mediator, Berlin);

        var args = JsonDocument.Parse("""
            {
              "title": "Urlaub",
              "start": "2026-06-01",
              "end":   "2026-06-08",
              "is_all_day": true
            }
            """).RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        var sent = mediator.SentRequests.OfType<CreateEventRequest>().Last();
        sent.IsAllDay.Should().BeTrue();
        sent.Start.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        sent.End.Should().Be(new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.FromHours(2)));
    }
}
