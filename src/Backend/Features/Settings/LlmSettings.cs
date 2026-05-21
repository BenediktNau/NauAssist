namespace NauAssist.Backend.Features.Settings;

public static class LlmProviders
{
    public const string Ollama = "ollama";
    public const string Gemini = "gemini";
}

public sealed record LlmSettings(
    string Provider,
    string OllamaModel,
    string GeminiModel,
    string? GeminiApiKey);
