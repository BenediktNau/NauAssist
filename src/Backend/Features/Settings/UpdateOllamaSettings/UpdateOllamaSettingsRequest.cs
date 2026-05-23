using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

public sealed record UpdateOllamaSettingsRequest(
    string Host,
    string? ApiKey,
    int NumCtx,
    double Temperature) : IRequest<UpdateOllamaSettingsResult>;

public sealed record UpdateOllamaSettingsResult(bool Ok, string? Error);
