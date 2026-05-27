using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Features.AutonomousAgent.Classification;

/// <summary>
/// Single-Call: klassifiziert eine Nachricht und liefert (falls Termin-Anfrage)
/// strukturierten Slot-Hint + Draft-Reply + optionales Persona-Update.
/// Verwendet das gleiche LLM wie der Chat-Agent.
/// </summary>
public sealed class IntentClassifier
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILlmClient _llm;
    private readonly ClockContext _clock;
    private readonly ILogger<IntentClassifier> _logger;

    public IntentClassifier(
        ILlmClient llm,
        ClockContext clock,
        ILogger<IntentClassifier> logger)
    {
        _llm = llm;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ClassificationResult?> ClassifyAsync(
        RawSignal signal,
        string userPersona,
        CancellationToken ct)
    {
        var snap = _clock.Build();
        var systemPrompt = BuildSystemPrompt(snap, userPersona);
        var userPrompt = BuildUserPrompt(signal);

        var messages = new List<LlmMessage>
        {
            new("system", systemPrompt),
            new("user", userPrompt),
        };

        var sb = new StringBuilder();
        await foreach (var chunk in _llm.ChatStreamAsync(messages, Array.Empty<ToolDefinition>(), ct).WithCancellation(ct))
        {
            if (chunk is TextDeltaChunk text)
            {
                sb.Append(text.Text);
            }
        }

        var raw = sb.ToString();
        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            _logger.LogWarning("IntentClassifier: keine JSON-Antwort im LLM-Output ({Length} Zeichen).", raw.Length);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var intent = root.TryGetProperty("intent", out var iEl) ? iEl.GetString() ?? "none" : "none";
            return new ClassificationResult(
                Intent: intent,
                Topic: GetString(root, "topic"),
                Requester: GetString(root, "requester"),
                DateHint: GetString(root, "date_hint"),
                DurationMinutes: GetInt(root, "duration_minutes"),
                DraftReply: GetString(root, "draft_reply"),
                Confidence: GetDouble(root, "confidence") ?? 0.0,
                PersonaUpdate: GetString(root, "persona_update"));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "IntentClassifier: JSON-Parse-Fehler. Output: {Output}", Truncate(raw, 400));
            return null;
        }
    }

    private static string BuildSystemPrompt(TimeSnapshot s, string persona)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Du bist ein autonomer Agent, der eingehende Chat-/Mail-Nachrichten klassifiziert.");
        sb.AppendLine("Deine Aufgabe: erkenne, ob die Nachricht eine Terminanfrage an den User ist, und schlage einen Slot-Suchbereich vor.");
        sb.AppendLine();
        sb.AppendLine("[Zeit-Kontext]");
        sb.AppendLine($"Jetzt: {s.NowLocal:yyyy-MM-ddTHH:mm:sszzz} ({s.WeekdayDe}, KW {s.IsoWeek})");
        sb.AppendLine($"Heute: {s.Today:yyyy-MM-dd}");
        sb.AppendLine($"Morgen: {s.Tomorrow:yyyy-MM-dd}");
        sb.AppendLine($"Diese Woche: {s.ThisWeek.Start:yyyy-MM-dd} bis {s.ThisWeek.End:yyyy-MM-dd}");
        sb.AppendLine($"Nächste Woche: {s.NextWeek.Start:yyyy-MM-dd} bis {s.NextWeek.End:yyyy-MM-dd}");
        sb.AppendLine($"Dieses WE: {s.ThisWeekend.Start:yyyy-MM-dd} bis {s.ThisWeekend.End:yyyy-MM-dd}");
        sb.AppendLine($"Nächstes WE: {s.NextWeekend.Start:yyyy-MM-dd} bis {s.NextWeekend.End:yyyy-MM-dd}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(persona))
        {
            sb.AppendLine("[Was du über den User weißt]");
            sb.AppendLine(persona);
            sb.AppendLine();
        }

        sb.AppendLine("Antworte AUSSCHLIESSLICH mit einem einzigen JSON-Objekt — kein Vorspann, kein Markdown-Code-Fence, keine Erklärung.");
        sb.AppendLine();
        sb.AppendLine("Schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"intent\": \"schedule_request\" | \"none\",");
        sb.AppendLine("  \"topic\": string | null,                      // kurzer Titel, z.B. \"Volleyball\"");
        sb.AppendLine("  \"requester\": string | null,                  // wer fragt (Vorname reicht)");
        sb.AppendLine("  \"date_hint\": string | null,                  // einer von: today, tomorrow, this_week, next_week, this_weekend, next_weekend, oder ISO-Datum \"YYYY-MM-DD\", oder ISO-Range \"YYYY-MM-DD/YYYY-MM-DD\"");
        sb.AppendLine("  \"duration_minutes\": number | null,           // Standard 60 wenn unklar");
        sb.AppendLine("  \"draft_reply\": string | null,                // kurzer Antwort-Entwurf aus Sicht des Users, max 280 Zeichen");
        sb.AppendLine("  \"confidence\": number,                        // 0..1, wie sicher ist die Klassifikation");
        sb.AppendLine("  \"persona_update\": string | null              // optional: ergänze, was du Neues über den User gelernt hast (max 400 Zeichen)");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Regeln:");
        sb.AppendLine("- Wenn intent=none, dürfen alle anderen Felder null oder leer sein.");
        sb.AppendLine("- Setze confidence niedrig, wenn die Nachricht mehrdeutig ist — lieber none.");
        sb.AppendLine("- date_hint ist die zeitliche Bandbreite, nicht ein einzelner Slot. Der User wählt später aus konkreten Vorschlägen.");
        sb.AppendLine("- draft_reply: aus Sicht des Users formuliert, du-Form, knapp, ohne Anrede/Schlusszeile, lass den konkreten Slot als Platzhalter [SLOT].");
        sb.AppendLine("- persona_update nur setzen, wenn die Nachricht wirklich etwas Neues über die Aktivitäten/Interessen/Stil des Users zeigt. Sonst null.");
        return sb.ToString();
    }

    private static string BuildUserPrompt(RawSignal signal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Quelle: {signal.Source}");
        if (!string.IsNullOrEmpty(signal.Sender))
        {
            sb.AppendLine($"Absender: {signal.Sender}");
        }
        sb.AppendLine($"Empfangen: {signal.ReceivedAt:yyyy-MM-ddTHH:mm:sszzz}");
        sb.AppendLine();
        sb.AppendLine("Nachricht:");
        sb.AppendLine(signal.Text);
        return sb.ToString();
    }

    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Strip ```json fences if present
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        // Find first '{' and last '}' to be tolerant against minor prefixes/suffixes.
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return trimmed.Substring(start, end - start + 1);
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var i) => i,
            _ => null,
        };
    }

    private static double? GetDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
