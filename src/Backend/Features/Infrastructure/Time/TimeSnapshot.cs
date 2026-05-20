namespace NauAssist.Backend.Features.Infrastructure.Time;

public sealed record TimeSnapshot(
    DateTimeOffset NowUtc,
    DateTimeOffset NowLocal,
    string Timezone,
    DateOnly Today,
    DateOnly Tomorrow,
    string WeekdayDe,
    int IsoWeek,
    DateRange ThisWeek,
    DateRange NextWeek,
    DateRange ThisWeekend,
    DateRange NextWeekend);
