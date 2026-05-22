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
        if (request.Provider != LlmProviders.Ollama && request.Provider != LlmProviders.Gemini)
        {
            return new UpdateLlmSettingsResult(false, $"Ungültiger provider: '{request.Provider}'.");
        }

        if (!SupportedModels.Ollama.Contains(request.OllamaModel))
        {
            return new UpdateLlmSettingsResult(false, $"Ungültiges ollamaModel: '{request.OllamaModel}'.");
        }

        if (!SupportedModels.Gemini.Contains(request.GeminiModel))
        {
            return new UpdateLlmSettingsResult(false, $"Ungültiges geminiModel: '{request.GeminiModel}'.");
        }

        var existing = await _settings.GetLlmAsync(ct);

        string? newKey = request.GeminiApiKey switch
        {
            null => existing.GeminiApiKey,
            ""   => null,
            _    => request.GeminiApiKey,
        };

        if (request.Provider == LlmProviders.Gemini && string.IsNullOrEmpty(newKey))
        {
            return new UpdateLlmSettingsResult(false,
                "Gemini benötigt einen API-Key — bitte eintragen, bevor du wechselst.");
        }

        await _settings.SetLlmAsync(
            new LlmSettings(request.Provider, request.OllamaModel, request.GeminiModel, newKey),
            ct);

        return new UpdateLlmSettingsResult(true, null);
    }
}
