using Mediator;

namespace NauAssist.Backend.Features.Settings.GetLlmSettings;

public sealed class GetLlmSettingsHandler : IRequestHandler<GetLlmSettingsRequest, GetLlmSettingsResponse>
{
    private readonly IAppSettingsRepository _settings;

    public GetLlmSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<GetLlmSettingsResponse> Handle(GetLlmSettingsRequest request, CancellationToken ct)
    {
        var s = await _settings.GetLlmAsync(ct);
        return new GetLlmSettingsResponse(s.OllamaModel);
    }
}
