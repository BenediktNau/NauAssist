using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateLlmSettings;

public sealed class UpdateLlmSettingsHandler
    : IRequestHandler<UpdateLlmSettingsRequest, UpdateLlmSettingsResult>
{
    private const int MaxSystemPromptLength = 4000;

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

        var trimmedPrompt = request.SystemPrompt?.Trim();
        if (trimmedPrompt is { Length: > MaxSystemPromptLength })
        {
            return new UpdateLlmSettingsResult(
                false,
                $"systemPrompt überschreitet Maximallänge ({MaxSystemPromptLength} Zeichen).");
        }

        var normalizedPrompt = string.IsNullOrEmpty(trimmedPrompt) ? null : trimmedPrompt;

        await _settings.SetLlmAsync(
            new LlmSettings(request.OllamaModel, normalizedPrompt),
            ct);

        return new UpdateLlmSettingsResult(true, null);
    }
}
