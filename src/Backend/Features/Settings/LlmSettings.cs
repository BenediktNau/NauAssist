namespace NauAssist.Backend.Features.Settings;

public sealed record LlmSettings(string OllamaModel, string? SystemPrompt = null);
