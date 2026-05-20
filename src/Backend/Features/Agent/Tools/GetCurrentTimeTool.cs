using System.Text.Json;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class GetCurrentTimeTool : ITool
{
    public string Name => "get_current_time";
    public string Description =>
        "Liefert die aktuelle Zeit in Europe/Berlin plus die exakten Daten für heute, morgen, " +
        "diese/nächste Woche (Mo–So) und dieses/nächstes Wochenende (Sa–So). Aufrufen, wenn der " +
        "Zeit-Kontext-Block für die Anfrage nicht reicht (z. B. 'in drei Wochen am Donnerstag').";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    private readonly ClockContext _clock;

    public GetCurrentTimeTool(ClockContext clock)
    {
        _clock = clock;
    }

    public Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var s = _clock.Build();

        var resultObj = new
        {
            now = s.NowLocal.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            timezone = s.Timezone,
            today = s.Today.ToString("yyyy-MM-dd"),
            tomorrow = s.Tomorrow.ToString("yyyy-MM-dd"),
            weekday = s.WeekdayDe,
            iso_week = s.IsoWeek,
            this_week = new { start = s.ThisWeek.Start.ToString("yyyy-MM-dd"), end = s.ThisWeek.End.ToString("yyyy-MM-dd") },
            next_week = new { start = s.NextWeek.Start.ToString("yyyy-MM-dd"), end = s.NextWeek.End.ToString("yyyy-MM-dd") },
            this_weekend = new { start = s.ThisWeekend.Start.ToString("yyyy-MM-dd"), end = s.ThisWeekend.End.ToString("yyyy-MM-dd") },
            next_weekend = new { start = s.NextWeekend.Start.ToString("yyyy-MM-dd"), end = s.NextWeekend.End.ToString("yyyy-MM-dd") },
        };

        return Task.FromResult(JsonSerializer.SerializeToElement(resultObj));
    }
}
