using System.ComponentModel.DataAnnotations;

namespace NauAssist.Common.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    [Required(AllowEmptyStrings = false)]
    public string Model { get; set; } = "gemma4:e4b";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string Endpoint { get; set; } = "http://host.docker.internal:11434";

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.7;

    [Range(1, 32_768)]
    public int MaxTokens { get; set; } = 2048;

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 60;

    [Range(0, 10)]
    public int Retries { get; set; } = 3;
}
