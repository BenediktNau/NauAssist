using Mediator;

namespace NauAssist.Backend.Features.Settings.GetOllamaSettings;

public sealed record GetOllamaSettingsRequest : IRequest<GetOllamaSettingsResult>;

public sealed record GetOllamaSettingsResult(
    string Host,
    bool HasApiKey,
    int NumCtx,
    double Temperature);
