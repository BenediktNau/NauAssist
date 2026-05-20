using System.Text.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class GetCurrentTimeToolTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Execute_ReturnsAllSnapshotFields()
    {
        var local = new DateTime(2026, 5, 20, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        var tool = new GetCurrentTimeTool(clock);

        var result = await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, default);

        result.GetProperty("now").GetString().Should().Be("2026-05-20T14:32:00+02:00");
        result.GetProperty("timezone").GetString().Should().Be("Europe/Berlin");
        result.GetProperty("today").GetString().Should().Be("2026-05-20");
        result.GetProperty("tomorrow").GetString().Should().Be("2026-05-21");
        result.GetProperty("weekday").GetString().Should().Be("Mittwoch");
        result.GetProperty("iso_week").GetInt32().Should().Be(21);

        var thisWeek = result.GetProperty("this_week");
        thisWeek.GetProperty("start").GetString().Should().Be("2026-05-18");
        thisWeek.GetProperty("end").GetString().Should().Be("2026-05-24");

        var nextWeek = result.GetProperty("next_week");
        nextWeek.GetProperty("start").GetString().Should().Be("2026-05-25");
        nextWeek.GetProperty("end").GetString().Should().Be("2026-05-31");

        var thisWeekend = result.GetProperty("this_weekend");
        thisWeekend.GetProperty("start").GetString().Should().Be("2026-05-23");
        thisWeekend.GetProperty("end").GetString().Should().Be("2026-05-24");

        var nextWeekend = result.GetProperty("next_weekend");
        nextWeekend.GetProperty("start").GetString().Should().Be("2026-05-30");
        nextWeekend.GetProperty("end").GetString().Should().Be("2026-05-31");
    }

    [Fact]
    public void ToolMetadata_NameAndDescription()
    {
        var local = new DateTime(2026, 5, 20, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        var tool = new GetCurrentTimeTool(clock);

        tool.Name.Should().Be("get_current_time");
        tool.Description.Should().NotBeNullOrWhiteSpace();
        tool.ParameterSchema.GetProperty("type").GetString().Should().Be("object");
    }
}
