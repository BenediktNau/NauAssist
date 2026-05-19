using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Features.Calendar;

public sealed class FreeSlotCalculator
{
    private readonly TimeZoneInfo _localZone;
    private readonly TimeOnly _workStart;
    private readonly TimeOnly _workEnd;
    private readonly DayOfWeekFlags _workingDays;

    public FreeSlotCalculator(
        TimeZoneInfo localZone,
        TimeOnly workStart,
        TimeOnly workEnd,
        DayOfWeekFlags workingDays)
    {
        _localZone = localZone;
        _workStart = workStart;
        _workEnd = workEnd;
        _workingDays = workingDays;
    }

    public IReadOnlyList<SlotCandidate> Calculate(
        DateTimeOffset from,
        DateTimeOffset to,
        IEnumerable<CalendarEvent> events,
        int durationMinutes)
    {
        if (durationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        if (to <= from) return Array.Empty<SlotCandidate>();

        var duration = TimeSpan.FromMinutes(durationMinutes);
        var eventList = events
            .Select(e => (Start: e.Start, End: e.End))
            .OrderBy(e => e.Start)
            .ToList();

        var result = new List<SlotCandidate>();

        var localFrom = TimeZoneInfo.ConvertTime(from, _localZone);
        var localTo = TimeZoneInfo.ConvertTime(to, _localZone);
        var day = localFrom.Date;

        while (day < localTo.Date.AddDays(1))
        {
            var dayFlag = DayFlagOf(day.DayOfWeek);
            if (_workingDays.HasFlag(dayFlag))
            {
                var dayStartLocal = day.Add(_workStart.ToTimeSpan());
                var dayEndLocal = day.Add(_workEnd.ToTimeSpan());

                var dayStartUtc = new DateTimeOffset(dayStartLocal, _localZone.GetUtcOffset(dayStartLocal));
                var dayEndUtc = new DateTimeOffset(dayEndLocal, _localZone.GetUtcOffset(dayEndLocal));

                var windowStart = dayStartUtc < from ? from : dayStartUtc;
                var windowEnd = dayEndUtc > to ? to : dayEndUtc;
                if (windowStart >= windowEnd)
                {
                    day = day.AddDays(1);
                    continue;
                }

                var occupants = eventList
                    .Where(e => e.Start < windowEnd && e.End > windowStart)
                    .ToList();

                var cursor = windowStart;
                foreach (var occ in occupants)
                {
                    if (occ.Start > cursor)
                    {
                        EmitSlots(result, cursor, occ.Start, duration);
                    }
                    if (occ.End > cursor)
                    {
                        cursor = occ.End;
                    }
                }

                if (cursor < windowEnd)
                {
                    EmitSlots(result, cursor, windowEnd, duration);
                }
            }

            day = day.AddDays(1);
        }

        return result;
    }

    private static void EmitSlots(List<SlotCandidate> sink, DateTimeOffset start, DateTimeOffset end, TimeSpan duration)
    {
        var cursor = start;
        while (cursor + duration <= end)
        {
            sink.Add(new SlotCandidate(cursor, cursor + duration));
            cursor += duration;
        }
    }

    private static DayOfWeekFlags DayFlagOf(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => DayOfWeekFlags.Monday,
        DayOfWeek.Tuesday   => DayOfWeekFlags.Tuesday,
        DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday,
        DayOfWeek.Thursday  => DayOfWeekFlags.Thursday,
        DayOfWeek.Friday    => DayOfWeekFlags.Friday,
        DayOfWeek.Saturday  => DayOfWeekFlags.Saturday,
        DayOfWeek.Sunday    => DayOfWeekFlags.Sunday,
        _ => DayOfWeekFlags.None,
    };
}
