namespace NauAssist.Backend.Features.Rules;

[Flags]
public enum DayOfWeekFlags
{
    None      = 0,
    Monday    = 1 << 0,
    Tuesday   = 1 << 1,
    Wednesday = 1 << 2,
    Thursday  = 1 << 3,
    Friday    = 1 << 4,
    Saturday  = 1 << 5,
    Sunday    = 1 << 6,

    WeekdaysOnly = Monday | Tuesday | Wednesday | Thursday | Friday,
    WeekendOnly  = Saturday | Sunday,
    AllDays      = WeekdaysOnly | WeekendOnly,
}

public enum RuleHardness
{
    Hard,
    Soft,
}

public sealed record Rule(
    long Id,
    string Text,
    DayOfWeekFlags DaysOfWeek,
    TimeOnly? TimeRangeStart,
    TimeOnly? TimeRangeEnd,
    RuleHardness Hardness,
    DateTimeOffset CreatedAt);
