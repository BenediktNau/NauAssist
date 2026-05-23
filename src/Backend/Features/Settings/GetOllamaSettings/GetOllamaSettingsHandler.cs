using Mediator;

namespace NauAssist.Backend.Features.Settings.GetOllamaSettings;

public sealed class GetOllamaSettingsHandler
    : IRequestHandler<GetOllamaSettingsRequest, GetOllamaSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public GetOllamaSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<GetOllamaSettingsResult> Handle(
        GetOllamaSettingsRequest request, CancellationToken ct)
    {
        var s = await _settings.GetOllamaAsync(ct);
        return new GetOllamaSettingsResult(
            Host: s.Host,
            HasApiKey: !string.IsNullOrEmpty(s.ApiKey),
            NumCtx: s.NumCtx,
            Temperature: s.Temperature);
    }
}
