namespace NauAssist.Backend.Features.Infrastructure.Llm.Ollama;

public sealed class OllamaOptions
{
    public string Host { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b-instruct";
    public int InitialTimeoutSeconds { get; set; } = 60;
    public int TokenTimeoutSeconds { get; set; } = 30;
    public string? SystemPrompt { get; set; }
    public int? NumCtx { get; set; }
    public double? Temperature { get; set; }
}
