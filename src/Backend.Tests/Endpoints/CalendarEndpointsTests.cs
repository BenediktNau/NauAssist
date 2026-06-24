using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class CalendarEndpointsTests : IDisposable
{
    private readonly TestAppFactory _factory = new();
    private readonly FakeCalendarProvider _fakeCal = new();

    private WebApplicationFactory<Program> Build() =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(svc =>
        {
            svc.AddSingleton<ICalendarProvider>(_fakeCal);
        }));

    private WebApplicationFactory<Program> BuildNotConnected() =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(svc =>
        {
            svc.AddSingleton<ICalendarProvider, ThrowingCalendarProvider>();
        }));

    [Fact]
    public async Task GetRange_ReturnsSeededEvents()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(7);
        _fakeCal.Seed(
            new CalendarEvent("ev-1", "Standup", from.AddHours(9), from.AddHours(9.5),
                null, null, false),
            new CalendarEvent("ev-2", "Urlaub", from.AddDays(3), from.AddDays(5),
                null, null, true));

        using var client = Build().CreateClient();
        using var resp = await client.GetAsync(
            $"/api/calendar/range?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<RangeDto>();
        body!.Events.Should().HaveCount(2);
        body.Events[0].Title.Should().Be("Standup");
        body.Events[1].IsAllDay.Should().BeTrue();
    }

    [Fact]
    public async Task GetRange_InvertedRange_Returns400()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.GetAsync(
            $"/api/calendar/range?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(from.AddHours(-1).ToString("O"))}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRange_NotConnected_Returns409()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        using var client = BuildNotConnected().CreateClient();
        using var resp = await client.GetAsync(
            $"/api/calendar/range?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(from.AddDays(1).ToString("O"))}");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostFreeSlots_ReturnsSlotsWithStatus()
    {
        var monday = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.PostAsJsonAsync("/api/calendar/free-slots", new
        {
            from = monday.AddHours(9),
            to = monday.AddHours(12),
            durationMinutes = 30,
        });

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<SlotsDto>();
        body!.Slots.Should().NotBeEmpty();
        body.Slots[0].Status.Should().BeOneOf("passes", "soft", "hard");
    }

    [Fact]
    public async Task PostFreeSlots_BadDuration_Returns400()
    {
        var from = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.PostAsJsonAsync("/api/calendar/free-slots", new
        {
            from,
            to = from.AddHours(2),
            durationMinutes = 0,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostEvent_CreatesEventAndReturns201()
    {
        var start = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "Standup",
            start,
            end = start.AddMinutes(30),
            description = (string?)null,
            location = (string?)null,
            isAllDay = false,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CreatedEventDto>();
        body!.Id.Should().StartWith("fake-");
        _fakeCal.CreatedEvents.Should().ContainSingle()
            .Which.Title.Should().Be("Standup");
    }

    [Fact]
    public async Task PostEvent_EmptyTitle_Returns400()
    {
        var start = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "   ",
            start,
            end = start.AddMinutes(30),
            isAllDay = false,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostEvent_EndBeforeStart_Returns400()
    {
        var start = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "Kaputt",
            start,
            end = start.AddMinutes(-30),
            isAllDay = false,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostEvent_NotConnected_Returns409()
    {
        var start = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        using var client = BuildNotConnected().CreateClient();
        using var resp = await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "Standup",
            start,
            end = start.AddMinutes(30),
            isAllDay = false,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteEvent_RemovesEvent()
    {
        _fakeCal.Seed(new CalendarEvent("ev-1", "Standup",
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            null, null, false));

        using var client = Build().CreateClient();
        using var resp = await client.DeleteAsync("/api/calendar/events/ev-1");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fakeCal.Deletions.Should().ContainSingle()
            .Which.Scope.Should().Be(EventScope.Instance);
    }

    [Fact]
    public async Task DeleteEvent_ScopeSeries_Forwards()
    {
        _fakeCal.Seed(new CalendarEvent("ev-1", "Standup",
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            null, null, false, SeriesId: "master"));

        using var client = Build().CreateClient();
        using var resp = await client.DeleteAsync("/api/calendar/events/ev-1?scope=series");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fakeCal.Deletions.Should().ContainSingle()
            .Which.Scope.Should().Be(EventScope.Series);
    }

    [Fact]
    public async Task DeleteEvent_BadScope_Returns400()
    {
        using var client = Build().CreateClient();
        using var resp = await client.DeleteAsync("/api/calendar/events/ev-1?scope=alle");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PatchEvent_UpdatesFields()
    {
        _fakeCal.Seed(new CalendarEvent("ev-1", "Alt",
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            null, null, false));

        using var client = Build().CreateClient();
        using var resp = await client.PatchAsJsonAsync("/api/calendar/events/ev-1", new
        {
            title = "Neu",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fakeCal.Updates.Should().ContainSingle()
            .Which.Update.Title.Should().Be("Neu");
    }

    [Fact]
    public async Task PatchEvent_EndBeforeStart_Returns400()
    {
        _fakeCal.Seed(new CalendarEvent("ev-1", "X",
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            null, null, false));

        var start = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        using var client = Build().CreateClient();
        using var resp = await client.PatchAsJsonAsync("/api/calendar/events/ev-1", new
        {
            start,
            end = start.AddMinutes(-30),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose() => _factory.Dispose();

    private sealed record RangeDto(IReadOnlyList<EventDto> Events);
    private sealed record EventDto(
        string Id, string Title, DateTimeOffset Start, DateTimeOffset End,
        string? Description, string? Location, bool IsAllDay, bool IsSeriesInstance);

    private sealed record CreatedEventDto(string Id);

    private sealed record SlotsDto(IReadOnlyList<SlotDto> Slots);
    private sealed record SlotDto(
        DateTimeOffset Start, DateTimeOffset End, string Status, string? ViolatedBy);

    private sealed class ThrowingCalendarProvider : ICalendarProvider
    {
        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            throw new NotAuthenticatedException("Nicht verbunden.");

        public Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct) =>
            throw new NotAuthenticatedException("Nicht verbunden.");

        public Task DeleteEventAsync(string eventId, EventScope scope, CancellationToken ct) =>
            throw new NotAuthenticatedException("Nicht verbunden.");

        public Task UpdateEventAsync(string eventId, EventUpdate update, EventScope scope, CancellationToken ct) =>
            throw new NotAuthenticatedException("Nicht verbunden.");
    }
}
