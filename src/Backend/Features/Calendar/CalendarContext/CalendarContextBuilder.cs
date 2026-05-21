using System.Text;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Features.Calendar.CalendarContext;

public sealed class CalendarContextBuilder
{
    private readonly ICalendarProvider _provider;
    private readonly CalendarOptions _options;
    private readonly TimeZoneInfo _zone;

    public CalendarContextBuilder(
        ICalendarProvider provider,
        IOptions<CalendarOptions> options,
        TimeZoneInfo zone)
    {
        _provider = provider;
        _options = options.Value;
        _zone = zone;
    }

    public async Task<string> BuildAsync(TimeSnapshot now, CancellationToken ct)
    {
        var startLocal = now.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(startLocal, _zone.GetUtcOffset(startLocal));
        var to = from.AddDays(_options.SearchHorizonDays);

        var events = await _provider.GetEventsAsync(from, to, ct);

        var allDay = events
            .Where(e => e.IsAllDay && e.End > now.NowLocal)
            .OrderBy(e => e.Start)
            .ToList();

        if (allDay.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"[Längerfristiger Kontext — All-Day-Termine im {_options.SearchHorizonDays}-Tage-Horizont]");
        foreach (var e in allDay)
        {
            sb.AppendLine($"- {FormatRange(e.Start, e.End)}: {e.Title}");
        }
        sb.AppendLine();
        sb.Append(
            "Diese Termine sind ganztägig und blockieren keinen Slot. Bevor du Vorschläge machst, " +
            "prüfe, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — und frage bei " +
            "Kollision nach.");
        return sb.ToString();
    }

    private static string FormatRange(DateTimeOffset start, DateTimeOffset endExclusive)
    {
        var startDate = DateOnly.FromDateTime(start.DateTime);
        var lastDate = DateOnly.FromDateTime(endExclusive.AddDays(-1).DateTime);

        if (startDate == lastDate)
        {
            return $"{ShortDay(startDate.DayOfWeek)} {startDate.Day}.{startDate.Month}.";
        }

        return $"{ShortDay(startDate.DayOfWeek)} {startDate.Day}.{startDate.Month}." +
               $"–{ShortDay(lastDate.DayOfWeek)} {lastDate.Day}.{lastDate.Month}.";
    }

    private static string ShortDay(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Di",
        DayOfWeek.Wednesday => "Mi",
        DayOfWeek.Thursday => "Do",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        DayOfWeek.Sunday => "So",
        _ => "?",
    };
}
