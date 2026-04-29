using System.ComponentModel.DataAnnotations;

namespace NauAssist.Common.Configuration;

public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    /// <summary>Whisper-Modelldatei für STT, relativ zu <see cref="IPathResolver.ModelsRoot"/>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string WhisperModelFile { get; set; } = "ggml-base.bin";

    /// <summary>Piper-Stimme für TTS (deutscher Default).</summary>
    [Required(AllowEmptyStrings = false)]
    public string PiperVoice { get; set; } = "de_DE-thorsten-medium";

    /// <summary>Schwellwert für Voice-Activity-Detection (0.0–1.0).</summary>
    [Range(0.0, 1.0)]
    public double VadThreshold { get; set; } = 0.5;
}
