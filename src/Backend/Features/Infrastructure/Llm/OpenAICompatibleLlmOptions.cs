namespace NauAssist.Backend.Features.Infrastructure.Llm;

/// <summary>
/// Pro Request konstruierter Options-Datensatz für <see cref="OpenAICompatibleLlmClient"/>.
/// Modell-spezifische und Provider-spezifische Werte werden hier vereinheitlicht.
/// </summary>
public sealed record OpenAICompatibleLlmOptions(
    string Model,
    int InitialTimeoutSeconds,
    int TokenTimeoutSeconds,
    string? SystemPrompt,
    double? Temperature,
    int? NumCtx);
