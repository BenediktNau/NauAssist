using System.Text.Json;

namespace NauAssist.Backend.Features.Agent.Tools;

/// <summary>
/// Side-Effect-Tool. Wird vom AgentRunner abgefangen — ExecuteAsync wirft, weil es niemals
/// regulär aufgerufen werden sollte.
/// Existiert nur, damit das LLM eine Tool-Definition mit JSON-Schema sieht.
/// </summary>
public sealed class PresentProposalsTool : ITool
{
    public const string ToolName = "present_proposals";

    public string Name => ToolName;
    public string Description =>
        "Veröffentlicht die finale Auswahl von 2–3 Slot-Vorschlägen an die Benutzeroberfläche. " +
        "Nach diesem Aufruf formuliert der Agent den begleitenden Antwort-Text in natürlicher Sprache.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "slots": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "properties": {
                  "start": { "type": "string", "format": "date-time" },
                  "end":   { "type": "string", "format": "date-time" },
                  "note":  { "type": ["string","null"], "description": "optionaler Kurz-Hinweis, z. B. 'Mi vormittag'" }
                },
                "required": ["start","end"]
              }
            }
          },
          "required": ["slots"]
        }
        """).RootElement;

    public Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        throw new InvalidOperationException(
            "PresentProposalsTool.ExecuteAsync darf nicht aufgerufen werden — der AgentRunner fängt diesen Tool-Call vorher ab.");
}
