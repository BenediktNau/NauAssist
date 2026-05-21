using FluentAssertions;
using NauAssist.Backend.Features.Chat;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class ChatContextCutoffTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private const string Sid = "default";

    private static DateTimeOffset BerlinLocal(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = Berlin.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    [Fact]
    public async Task Compute_NoMarker_Afternoon_ReturnsTodayFiveAm()
    {
        // Mi 2026-05-20 14:32 Berlin
        var now = BerlinLocal(2026, 5, 20, 14, 32);
        var sut = new ChatContextCutoff(
            new InMemoryMarkers(),
            () => now,
            Berlin);

        var cutoff = await sut.ComputeAsync(Sid, default);

        cutoff.Should().Be(BerlinLocal(2026, 5, 20, 5, 0));
    }

    [Fact]
    public async Task Compute_NoMarker_BeforeFiveAm_ReturnsYesterdayFiveAm()
    {
        // Mi 2026-05-20 02:30 Berlin → vor 5 Uhr → gestern 5 Uhr
        var now = BerlinLocal(2026, 5, 20, 2, 30);
        var sut = new ChatContextCutoff(
            new InMemoryMarkers(),
            () => now,
            Berlin);

        var cutoff = await sut.ComputeAsync(Sid, default);

        cutoff.Should().Be(BerlinLocal(2026, 5, 19, 5, 0));
    }

    [Fact]
    public async Task Compute_MarkerAfterDayStart_ReturnsMarker()
    {
        var now = BerlinLocal(2026, 5, 20, 14, 32);
        var markerAt = BerlinLocal(2026, 5, 20, 12, 0);
        var sut = new ChatContextCutoff(
            new InMemoryMarkers(markerAt),
            () => now,
            Berlin);

        var cutoff = await sut.ComputeAsync(Sid, default);

        cutoff.Should().Be(markerAt);
    }

    [Fact]
    public async Task Compute_MarkerBeforeDayStart_ReturnsDayStart()
    {
        // jüngster Marker liegt gestern 23:00 — vor heute 05:00 → Cutoff = heute 05:00
        var now = BerlinLocal(2026, 5, 20, 6, 0);
        var markerAt = BerlinLocal(2026, 5, 19, 23, 0);
        var sut = new ChatContextCutoff(
            new InMemoryMarkers(markerAt),
            () => now,
            Berlin);

        var cutoff = await sut.ComputeAsync(Sid, default);

        cutoff.Should().Be(BerlinLocal(2026, 5, 20, 5, 0));
    }

    private sealed class InMemoryMarkers : IChatClearMarkerSource
    {
        private readonly List<DateTimeOffset> _markers;

        public InMemoryMarkers(params DateTimeOffset[] markers)
        {
            _markers = markers.ToList();
        }

        public Task<DateTimeOffset?> GetLatestCreatedAtSinceAsync(string sessionId, DateTimeOffset since, CancellationToken ct)
        {
            DateTimeOffset? latest = _markers
                .Where(m => m > since)
                .OrderByDescending(m => m)
                .Select(m => (DateTimeOffset?)m)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }
    }
}
