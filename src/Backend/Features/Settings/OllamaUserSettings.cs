namespace NauAssist.Backend.Features.Settings;

public sealed record OllamaUserSettings(
    string Host,
    string? ApiKey,
    int NumCtx,
    double Temperature);
