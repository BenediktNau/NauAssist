using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CreateEventHandlerTests
{
    [Fact]
    public async Task Handle_CreatesEvent_AndReturnsProviderId()
    {
        using var db = new TempSqliteDb();
        var provider = new FakeCalendarProvider();
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(provider, audit);

        var response = await handler.Handle(new CreateEventRequest(
            Title: "Pierre",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        response.EventId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_RejectsEmptyTitle()
    {
        using var db = new TempSqliteDb();
        var provider = new FakeCalendarProvider();
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(provider, audit);

        var act = async () => await handler.Handle(new CreateEventRequest(
            Title: "",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Title*");
    }

    [Fact]
    public async Task Handle_RejectsEndBeforeStart()
    {
        using var db = new TempSqliteDb();
        var provider = new FakeCalendarProvider();
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(provider, audit);

        var act = async () => await handler.Handle(new CreateEventRequest(
            Title: "X",
            Start: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*End*");
    }

    [Fact]
    public async Task Handle_AfterCreate_WritesAuditEntry()
    {
        using var db = new TempSqliteDb();
        var provider = new FakeCalendarProvider();
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(provider, audit);

        var response = await handler.Handle(new CreateEventRequest(
            Title: "Pierre",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        (await audit.CountAsync(CancellationToken.None)).Should().Be(1);

        var entries = await audit.GetByMessageIdAsync(0, CancellationToken.None); // FK = null → keine Treffer
        entries.Should().BeEmpty(); // weil TriggeringMessageId = null, nicht 0
        // Tool-Name + Provider-ID prüfen wir via SQL direkt
        using var conn = db.AppDb.OpenConnection();
        var toolName = await Dapper.SqlMapper.ExecuteScalarAsync<string>(conn,
            "SELECT tool_name FROM audit_log LIMIT 1");
        toolName.Should().Be("create_event");
        var providerEventId = await Dapper.SqlMapper.ExecuteScalarAsync<string>(conn,
            "SELECT provider_event_id FROM audit_log LIMIT 1");
        providerEventId.Should().Be(response.EventId);
    }

    [Fact]
    public async Task Handle_AllDayRequest_ForwardsIsAllDayToProvider()
    {
        using var db = new TempSqliteDb();
        var provider = new FakeCalendarProvider();
        var audit = new AuditLogRepository(db.AppDb);
        var handler = BuildHandler(provider, audit);

        await handler.Handle(new CreateEventRequest(
            Title: "Urlaub",
            Start: DateTimeOffset.Parse("2026-06-01T00:00:00+02:00"),
            End:   DateTimeOffset.Parse("2026-06-08T00:00:00+02:00"),
            Description: null,
            Location: null,
            IsAllDay: true), CancellationToken.None);

        provider.CreatedEvents.Should().HaveCount(1);
        provider.CreatedEvents[0].IsAllDay.Should().BeTrue();
        provider.CreatedEvents[0].Title.Should().Be("Urlaub");
    }

    private static CreateEventHandler BuildHandler(ICalendarProvider provider, AuditLogRepository audit) =>
        new(
            provider,
            audit,
            clock: () => DateTimeOffset.Parse("2026-05-19T10:00:00Z"),
            logger: NullLogger<CreateEventHandler>.Instance);
}
