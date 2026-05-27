using System.Text;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.AutonomousAgent.Classification;

/// <summary>
/// On-Demand Draft-Verfeinerung: erzeugt einen Antwort-Text basierend auf der
/// Original-Anfrage + gewähltem Slot. Wird beim Slot-Pick aufgerufen.
/// </summary>
public sealed class DraftReplyGenerator
{
    private const int MaxLength = 500;

    private readonly ILlmClient _llm;
    private readonly IAppSettingsRepository _settings;
    private readonly ILogger<DraftReplyGenerator> _logger;

    public DraftReplyGenerator(
        ILlmClient llm,
        IAppSettingsRepository settings,
        ILogger<DraftReplyGenerator> logger)
    {
        _llm = llm;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string?> RefineAsync(
        string? quotedText,
        string? topic,
        string? requester,
        SuggestionSlot pickedSlot,
        string locale,
        CancellationToken ct)
    {
        var persona = await _settings.GetUserPersonaAsync(ct);
        var systemPrompt = BuildSystemPrompt(persona, locale);
        var userPrompt = BuildUserPrompt(quotedText, topic, requester, pickedSlot, locale);

        var messages = new List<LlmMessage>
        {
            new("system", systemPrompt),
            new("user", userPrompt),
        };

        var sb = new StringBuilder();
        await foreach (var chunk in _llm.ChatStreamAsync(messages, Array.Empty<ToolDefinition>(), ct).WithCancellation(ct))
        {
            if (chunk is TextDeltaChunk t) sb.Append(t.Text);
        }

        var raw = sb.ToString().Trim();
        if (string.IsNullOrEmpty(raw))
        {
            _logger.LogWarning("DraftReplyGenerator: LLM lieferte leeren Output.");
            return null;
        }

        // Strip evtl. vorhandene Code-Fences / führende Quotation Marks.
        var stripped = raw.Trim('`', '"', ' ', '\n', '\r', '\t');
        if (stripped.Length > MaxLength) stripped = stripped[..MaxLength];
        return stripped;
    }

    private static string BuildSystemPrompt(string persona, string locale)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Du formulierst eine kurze, persönliche Antwort aus Sicht des Users.");
        sb.AppendLine("Antworte AUSSCHLIESSLICH mit dem Antwort-Text — kein Vorspann, keine Anführungszeichen, kein Markdown, keine Erklärung.");
        sb.AppendLine();
        sb.AppendLine("Stil:");
        sb.AppendLine("- Du-Form, deutsch (außer wenn die Anfrage offensichtlich in anderer Sprache war).");
        sb.AppendLine("- Knapp: 1-2 Sätze.");
        sb.AppendLine("- Ohne Anrede wie 'Hallo' und ohne Schlussformel wie 'Viele Grüße'.");
        sb.AppendLine("- Bestätige den gewählten Slot konkret mit Wochentag und Uhrzeit.");
        sb.AppendLine($"- Locale: {locale}.");

        if (!string.IsNullOrWhiteSpace(persona))
        {
            sb.AppendLine();
            sb.AppendLine("[Was du über den User weißt — übernimm Stil und Wortwahl, wenn passend]");
            sb.AppendLine(persona);
        }

        return sb.ToString();
    }

    private static string BuildUserPrompt(
        string? quotedText, string? topic, string? requester, SuggestionSlot slot, string locale)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Original-Anfrage:");
        sb.AppendLine(string.IsNullOrWhiteSpace(quotedText) ? "(nicht verfügbar)" : quotedText);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(topic))
        {
            sb.AppendLine($"Thema: {topic}");
        }
        if (!string.IsNullOrWhiteSpace(requester))
        {
            sb.AppendLine($"Anfrager: {requester}");
        }
        sb.AppendLine();
        sb.AppendLine("Gewählter Slot:");
        sb.AppendLine(FormatSlot(slot, locale));
        sb.AppendLine();
        sb.AppendLine("Antworte jetzt nur mit dem Antwort-Text.");
        return sb.ToString();
    }

    private static string FormatSlot(SuggestionSlot slot, string locale)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
        var day = slot.Start.ToString("dddd, d. MMMM", culture);
        var time = $"{slot.Start:HH:mm}–{slot.End:HH:mm}";
        return $"{day}, {time}";
    }
}
