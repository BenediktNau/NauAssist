using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateLlmSettings;

public sealed class UpdateLlmSettingsHandler
    : IRequestHandler<UpdateLlmSettingsRequest, UpdateLlmSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public UpdateLlmSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<UpdateLlmSettingsResult> Handle(
        UpdateLlmSettingsRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OllamaModel))
        {
            return new UpdateLlmSettingsResult(false, "ollamaModel darf nicht leer sein.");
        }

        await _settings.SetLlmAsync(new LlmSettings(request.OllamaModel), ct);

        return new UpdateLlmSettingsResult(true, null);
    }
}
