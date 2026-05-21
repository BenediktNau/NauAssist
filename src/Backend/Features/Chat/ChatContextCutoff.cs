namespace NauAssist.Backend.Features.Chat;

/// <summary>
/// Berechnet den effektiven Cutoff für den LLM-Konversationskontext:
/// das Maximum aus jüngster 5-Uhr-Tagesmarke (Berlin-Zeit) und jüngstem /clear-Marker.
/// </summary>
public sealed class ChatContextCutoff
{
    public const int DayStartHour = 5;

    private readonly IChatClearMarkerSource _markers;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;

    public ChatContextCutoff(IChatClearMarkerSource markers, Func<DateTimeOffset> clock, TimeZoneInfo zone)
    {
        _markers = markers;
        _clock = clock;
        _zone = zone;
    }

    public async Task<DateTimeOffset> ComputeAsync(string sessionId, CancellationToken ct)
    {
        var dayStart = ComputeDayStart();
        var marker = await _markers.GetLatestCreatedAtSinceAsync(sessionId, dayStart, ct);
        return marker is DateTimeOffset m && m > dayStart ? m : dayStart;
    }

    public DateTimeOffset ComputeDayStart()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_clock(), _zone);
        var todayCutoff = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, DayStartHour, 0, 0, DateTimeKind.Unspecified);
        if (nowLocal.DateTime < todayCutoff)
        {
            todayCutoff = todayCutoff.AddDays(-1);
        }
        var offset = _zone.GetUtcOffset(todayCutoff);
        return new DateTimeOffset(todayCutoff, offset);
    }
}
