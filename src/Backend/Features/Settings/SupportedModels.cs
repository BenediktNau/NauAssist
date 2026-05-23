namespace NauAssist.Backend.Features.Settings;

public static class SupportedModels
{
    public static readonly IReadOnlyList<string> Ollama = new[]
    {
        "gemma4:26b",
        "gemma4:e2b",
        "qwen3.5:4b",
        "qwen2.5:7b-instruct",
        "llama3.2:3b",
    };

    public static readonly IReadOnlyList<string> Gemini = new[]
    {
        "gemini-2.5-flash",
        "gemini-2.5-pro",
        "gemma-4-31b-it",
    };
}
