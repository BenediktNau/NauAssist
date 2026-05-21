using System.Globalization;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

namespace NauAssist.Backend.Features.Calendar.Google;

internal static class GoogleEventMapper
{
    public static CalendarEvent? Map(GoogleEvent e, TimeZoneInfo zone)
    {
        if (e.Start is null || e.End is null) return null;

        if (e.Start.DateTimeDateTimeOffset is { } startDt && e.End.DateTimeDateTimeOffset is { } endDt)
        {
            return new CalendarEvent(
                Id: e.Id,
                Title: e.Summary ?? "(ohne Titel)",
                Start: startDt,
                End: endDt,
                Description: e.Description,
                Location: e.Location,
                IsAllDay: false);
        }

        if (!string.IsNullOrEmpty(e.Start.Date) && !string.IsNullOrEmpty(e.End.Date))
        {
            var startDate = DateOnly.ParseExact(e.Start.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var endDate = DateOnly.ParseExact(e.End.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new CalendarEvent(
                Id: e.Id,
                Title: e.Summary ?? "(ohne Titel)",
                Start: ToLocalMidnight(startDate, zone),
                End: ToLocalMidnight(endDate, zone),
                Description: e.Description,
                Location: e.Location,
                IsAllDay: true);
        }

        return null;
    }

    private static DateTimeOffset ToLocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
