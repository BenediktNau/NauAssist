using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateLlmSettings;

/// <summary>
/// GeminiApiKey-Konvention:
///  - null  → Bestand bleibt
///  - ""    → Key löschen
///  - sonst → überschreiben
/// </summary>
public sealed record UpdateLlmSettingsRequest(
    string Provider,
    string OllamaModel,
    string GeminiModel,
    string? GeminiApiKey) : IRequest<UpdateLlmSettingsResult>;

public sealed record UpdateLlmSettingsResult(bool Ok, string? Error);
