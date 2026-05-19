using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class AddRuleTool : ITool
{
    public string Name => "add_rule";
    public string Description =>
        "Speichert eine vom User formulierte Regel (z. B. 'keine Termine nach 18 Uhr'). " +
        "Args sind strukturiert — das LLM wandelt die natürliche Eingabe vorher in dieses Schema.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "text": { "type": "string", "description": "Original-Klartext der Regel" },
            "days_of_week": {
              "type": "array",
              "items": { "type": "string", "enum": ["monday","tuesday","wednesday","thursday","friday","saturday","sunday"] }
            },
            "time_start": { "type": ["string","null"], "description": "HH:mm — Beginn der Sperrzeit, null = ganzer Tag" },
            "time_end":   { "type": ["string","null"], "description": "HH:mm — Ende der Sperrzeit" },
            "hardness":   { "type": "string", "enum": ["hard","soft"] }
          },
          "required": ["text", "days_of_week", "hardness"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public AddRuleTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var days = DayOfWeekFlags.None;
        foreach (var dayEl in args.GetProperty("days_of_week").EnumerateArray())
        {
            days |= dayEl.GetString() switch
            {
                "monday" => DayOfWeekFlags.Monday,
                "tuesday" => DayOfWeekFlags.Tuesday,
                "wednesday" => DayOfWeekFlags.Wednesday,
                "thursday" => DayOfWeekFlags.Thursday,
                "friday" => DayOfWeekFlags.Friday,
                "saturday" => DayOfWeekFlags.Saturday,
                "sunday" => DayOfWeekFlags.Sunday,
                _ => DayOfWeekFlags.None,
            };
        }

        var request = new AddRuleRequest(
            Text: args.GetProperty("text").GetString()!,
            DaysOfWeek: days,
            TimeRangeStart: ParseTime(args, "time_start"),
            TimeRangeEnd: ParseTime(args, "time_end"),
            Hardness: Enum.Parse<RuleHardness>(args.GetProperty("hardness").GetString()!, ignoreCase: true));

        var response = await _mediator.Send(request, ct);
        return JsonSerializer.SerializeToElement(new
        {
            rule_id = response.Rule.Id,
            interpreted = new
            {
                text = response.Rule.Text,
                days_of_week = (int)response.Rule.DaysOfWeek,
                time_start = response.Rule.TimeRangeStart?.ToString("HH:mm"),
                time_end = response.Rule.TimeRangeEnd?.ToString("HH:mm"),
                hardness = response.Rule.Hardness.ToString().ToLowerInvariant(),
            },
        });
    }

    private static TimeOnly? ParseTime(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : TimeOnly.Parse(s);
    }
}
