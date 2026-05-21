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
                Location: e.Location);
        }

        return null;
    }
}
