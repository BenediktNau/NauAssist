namespace NauAssist.Backend.Features.Infrastructure.Llm.Gemini;

public sealed class GeminiOptions
{
    public string BaseAddress { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public int InitialTimeoutSeconds { get; set; } = 60;
    public int TokenTimeoutSeconds { get; set; } = 30;
    public string? SystemPrompt { get; set; }
    public double? Temperature { get; set; } = 0.3;
}
