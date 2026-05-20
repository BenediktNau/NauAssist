using System.Globalization;

namespace NauAssist.Backend.Features.Infrastructure.Time;

public sealed class ClockContext
{
    private static readonly string[] WeekdaysDe =
    {
        "Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag",
    };

    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;

    public ClockContext(Func<DateTimeOffset> clock, TimeZoneInfo zone)
    {
        _clock = clock;
        _zone = zone;
    }

    public TimeSnapshot Build()
    {
        var nowUtc = _clock().ToUniversalTime();
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, _zone);

        var today = DateOnly.FromDateTime(nowLocal.DateTime);
        var tomorrow = today.AddDays(1);

        var weekday = WeekdaysDe[(int)today.DayOfWeek];

        var isoWeek = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));

        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var thisSunday = thisMonday.AddDays(6);
        var nextMonday = thisMonday.AddDays(7);
        var nextSunday = thisSunday.AddDays(7);

        var (thisWeSat, thisWeSun, nextWeSat, nextWeSun) = ComputeWeekends(today);

        return new TimeSnapshot(
            NowUtc: nowUtc,
            NowLocal: nowLocal,
            Timezone: _zone.Id,
            Today: today,
            Tomorrow: tomorrow,
            WeekdayDe: weekday,
            IsoWeek: isoWeek,
            ThisWeek: new DateRange(thisMonday, thisSunday),
            NextWeek: new DateRange(nextMonday, nextSunday),
            ThisWeekend: new DateRange(thisWeSat, thisWeSun),
            NextWeekend: new DateRange(nextWeSat, nextWeSun));
    }

    private static (DateOnly thisSat, DateOnly thisSun, DateOnly nextSat, DateOnly nextSun) ComputeWeekends(DateOnly today)
    {
        switch (today.DayOfWeek)
        {
            case DayOfWeek.Saturday:
            {
                var sat = today;
                var sun = today.AddDays(1);
                return (sat, sun, sat.AddDays(7), sun.AddDays(7));
            }
            case DayOfWeek.Sunday:
            {
                var sat = today.AddDays(-1);
                var sun = today;
                return (sat, sun, today.AddDays(6), today.AddDays(7));
            }
            default:
            {
                var daysToSat = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
                var sat = today.AddDays(daysToSat);
                var sun = sat.AddDays(1);
                return (sat, sun, sat.AddDays(7), sun.AddDays(7));
            }
        }
    }
}
