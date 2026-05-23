using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CalendarContextBuilderTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static TimeSnapshot SnapshotForMittwoch_21_5_2026_14h32()
    {
        var local = new DateTime(2026, 5, 21, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        return clock.Build();
    }

    private static CalendarContextBuilder BuildBuilder(FakeCalendarProvider provider) =>
        new(provider, new FakeSettingsRepo(searchHorizon: 14), Berlin);

    [Fact]
    public async Task BuildAsync_NoAllDayEvents_ReturnsEmptyString()
    {
        var provider = new FakeCalendarProvider();
        var builder = BuildBuilder(provider);

        var block = await builder.BuildAsync(SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_SingleDayAllDay_RendersOneDate()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e1", Title: "Urlaub",
            Start: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().Contain("[Längerfristiger Kontext");
        block.Should().Contain("- Mo 1.6.: Urlaub");
        block.Should().NotContain("–");
    }

    [Fact]
    public async Task BuildAsync_MultiDayAllDay_RendersRangeWithMinusOneDayConvention()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e1", Title: "Schulung Köln",
            Start: new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        // End=30.5. exklusiv → Anzeige-Ende 29.5.
        block.Should().Contain("- Mi 27.5.–Fr 29.5.: Schulung Köln");
    }

    [Fact]
    public async Task BuildAsync_PastAllDay_IsFilteredOut()
    {
        // Event ends at 10:00 today → passes the provider's range filter (End > from = 00:00),
        // but the builder filters it out because End <= now.NowLocal (14:32).
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e-past", Title: "Gestern-Urlaub",
            Start: new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 21, 10, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_RegularEvents_DoNotAppearInBlock()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e-meet", Title: "Meeting",
            Start: new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 27, 11, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: false));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_RendersAllDayEventsInChronologicalOrder()
    {
        // End-to-end coverage: FakeCalendarProvider already sorts by Start, so this verifies
        // that the rendered output block reflects chronological order throughout the pipeline.
        var provider = new FakeCalendarProvider();
        provider.Seed(
            new CalendarEvent("e2", "Urlaub",
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.FromHours(2)),
                null, null, IsAllDay: true),
            new CalendarEvent("e1", "Schulung",
                new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)),
                null, null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        var schulungIdx = block.IndexOf("Schulung", StringComparison.Ordinal);
        var urlaubIdx = block.IndexOf("Urlaub", StringComparison.Ordinal);
        schulungIdx.Should().BeGreaterThan(-1);
        urlaubIdx.Should().BeGreaterThan(-1);
        schulungIdx.Should().BeLessThan(urlaubIdx);
    }
}
