namespace NauAssist.Backend.Features.AutonomousAgent.Classification;

/// <summary>
/// Schnelle Vorfilterung: hat die Nachricht überhaupt das Potential, eine Termin-Anfrage zu sein?
/// Ziel: >90% der "boring messages" rauswerfen, bevor das LLM angefasst wird.
/// </summary>
public static class CheapPreFilter
{
    // Lowercase-Substrings, die in Termin-Anfragen typischerweise vorkommen.
    private static readonly string[] Hints =
    [
        "termin", "treffen", "treffe ", "treffe?", "verabred", "vereinbar",
        "spielen", "essen", "kaffee", "mittag", "abend", "frühstück", "brunch",
        "wann", "uhr", "zeit ", "zeit?", "frei?", "passt dir", "passt es",
        "schaffst", "schaffen wir", "hast du", "kannst du",
        "morgen", "übermorgen", "wochenende", "diese woche", "nächste woche",
        "meeting", "call", "anruf",
        "?",
    ];

    public static bool LooksLikeSchedulingIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        foreach (var h in Hints)
        {
            if (lower.Contains(h, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
