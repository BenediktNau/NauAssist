using Mediator;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;

namespace NauAssist.Backend.Features.Settings.GetLlmSettings;

public sealed class GetLlmSettingsHandler : IRequestHandler<GetLlmSettingsRequest, GetLlmSettingsResponse>
{
    private readonly IAppSettingsRepository _settings;
    private readonly OllamaOptions _ollamaDefaults;

    public GetLlmSettingsHandler(
        IAppSettingsRepository settings,
        IOptions<OllamaOptions> ollamaDefaults)
    {
        _settings = settings;
        _ollamaDefaults = ollamaDefaults.Value;
    }

    public async ValueTask<GetLlmSettingsResponse> Handle(GetLlmSettingsRequest request, CancellationToken ct)
    {
        var s = await _settings.GetLlmAsync(ct);
        return new GetLlmSettingsResponse(
            OllamaModel: s.OllamaModel,
            SystemPrompt: s.SystemPrompt,
            DefaultSystemPrompt: _ollamaDefaults.SystemPrompt ?? "");
    }
}
