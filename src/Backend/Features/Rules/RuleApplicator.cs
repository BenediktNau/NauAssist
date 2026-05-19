namespace NauAssist.Backend.Features.Rules;

public sealed class RuleApplicator
{
    private readonly TimeZoneInfo _localZone;

    public RuleApplicator(TimeZoneInfo localZone)
    {
        _localZone = localZone;
    }

    public IReadOnlyList<SlotAnnotation> Annotate(
        IEnumerable<SlotCandidate> slots,
        IEnumerable<Rule> rules)
    {
        var ruleList = rules.ToList();
        var result = new List<SlotAnnotation>();

        foreach (var slot in slots)
        {
            var violations = ruleList
                .Where(r => Matches(slot, r))
                .ToList();

            if (violations.Count == 0)
            {
                result.Add(new SlotAnnotation(slot, AnnotationStatus.Passes, null));
                continue;
            }

            // Harte Verstöße haben Vorrang vor weichen
            var hard = violations.FirstOrDefault(r => r.Hardness == RuleHardness.Hard);
            if (hard is not null)
            {
                result.Add(new SlotAnnotation(slot, AnnotationStatus.HardViolation, hard));
            }
            else
            {
                result.Add(new SlotAnnotation(slot, AnnotationStatus.SoftViolation, violations[0]));
            }
        }

        return result;
    }

    private bool Matches(SlotCandidate slot, Rule rule)
    {
        var localStart = TimeZoneInfo.ConvertTime(slot.Start, _localZone);
        var localEnd = TimeZoneInfo.ConvertTime(slot.End, _localZone);

        // Wochentag prüfen (anhand des Slot-Starts in Lokalzeit)
        var dayFlag = DayFlagOf(localStart.DayOfWeek);
        if (!rule.DaysOfWeek.HasFlag(dayFlag))
        {
            return false;
        }

        // Ohne Zeit-Range: gilt für den ganzen Tag
        if (rule.TimeRangeStart is null && rule.TimeRangeEnd is null)
        {
            return true;
        }

        var ruleStart = rule.TimeRangeStart ?? TimeOnly.MinValue;
        var ruleEnd = rule.TimeRangeEnd ?? new TimeOnly(23, 59, 59);

        var slotStartTime = TimeOnly.FromDateTime(localStart.DateTime);
        var slotEndTime = TimeOnly.FromDateTime(localEnd.DateTime);

        // Überschneidung: Slot[Start..End] vs. Rule[ruleStart..ruleEnd]
        return slotStartTime < ruleEnd && slotEndTime > ruleStart;
    }

    private static DayOfWeekFlags DayFlagOf(DayOfWeek day) => day switch
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
