using Mediator;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Endpoints;

public static class CalendarEndpoints
{
    private const int MaxRangeDays = 400;
    private const int MaxSlotRangeDays = 90;
    private const int MaxDurationMinutes = 24 * 60;

    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/calendar/range", async (
            DateTimeOffset from,
            DateTimeOffset to,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (to <= from)
            {
                return Results.BadRequest(new { error = "to muss nach from liegen." });
            }
            if ((to - from).TotalDays > MaxRangeDays)
            {
                return Results.BadRequest(new { error = $"Range > {MaxRangeDays} Tagen nicht unterstützt." });
            }

            try
            {
                var response = await mediator.Send(new GetCalendarRangeRequest(from, to), ct);
                var dtos = response.Events.Select(MapEvent).ToArray();
                return Results.Ok(new CalendarRangeDto(dtos));
            }
            catch (NotAuthenticatedException ex)
            {
                return Results.Json(
                    new { error = ex.Message, code = "not_connected" },
                    statusCode: StatusCodes.Status409Conflict);
            }
        });

        app.MapPost("/api/calendar/free-slots", async (
            FindFreeSlotsPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (payload.To <= payload.From)
            {
                return Results.BadRequest(new { error = "to muss nach from liegen." });
            }
            if ((payload.To - payload.From).TotalDays > MaxSlotRangeDays)
            {
                return Results.BadRequest(new { error = $"Range > {MaxSlotRangeDays} Tagen nicht unterstützt." });
            }
            if (payload.DurationMinutes <= 0 || payload.DurationMinutes > MaxDurationMinutes)
            {
                return Results.BadRequest(new { error = "durationMinutes muss zwischen 1 und 1440 liegen." });
            }

            try
            {
                var response = await mediator.Send(
                    new LookupFreeSlotsRequest(payload.From, payload.To, payload.DurationMinutes),
                    ct);
                var dtos = response.Annotations.Select(MapAnnotation).ToArray();
                return Results.Ok(new FreeSlotsDto(dtos));
            }
            catch (NotAuthenticatedException ex)
            {
                return Results.Json(
                    new { error = ex.Message, code = "not_connected" },
                    statusCode: StatusCodes.Status409Conflict);
            }
        });

        return app;
    }

    private static CalendarEventDto MapEvent(CalendarEvent ev) => new(
        Id: ev.Id,
        Title: ev.Title,
        Start: ev.Start,
        End: ev.End,
        Description: ev.Description,
        Location: ev.Location,
        IsAllDay: ev.IsAllDay,
        IsSeriesInstance: ev.IsSeriesInstance);

    private static FreeSlotDto MapAnnotation(SlotAnnotation a) => new(
        Start: a.Slot.Start,
        End: a.Slot.End,
        Status: a.Status switch
        {
            AnnotationStatus.Passes => "passes",
            AnnotationStatus.SoftViolation => "soft",
            AnnotationStatus.HardViolation => "hard",
            _ => "passes",
        },
        ViolatedBy: a.ViolatedBy?.Text);

    public sealed record FindFreeSlotsPayload(
        DateTimeOffset From,
        DateTimeOffset To,
        int DurationMinutes);

    private sealed record CalendarRangeDto(IReadOnlyList<CalendarEventDto> Events);

    private sealed record CalendarEventDto(
        string Id,
        string Title,
        DateTimeOffset Start,
        DateTimeOffset End,
        string? Description,
        string? Location,
        bool IsAllDay,
        bool IsSeriesInstance);

    private sealed record FreeSlotsDto(IReadOnlyList<FreeSlotDto> Slots);

    private sealed record FreeSlotDto(
        DateTimeOffset Start,
        DateTimeOffset End,
        string Status,
        string? ViolatedBy);
}
