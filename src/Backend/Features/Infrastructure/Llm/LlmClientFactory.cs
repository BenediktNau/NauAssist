using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm.Gemini;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

public interface ILlmClientFactory
{
    Task<ILlmClient> CreateAsync(CancellationToken ct);
}

public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IAppSettingsRepository _settings;
    private readonly OllamaOptions _ollamaDefaults;
    private readonly GeminiOptions _geminiDefaults;
    private readonly ILoggerFactory _loggerFactory;

    public LlmClientFactory(
        IHttpClientFactory httpFactory,
        IAppSettingsRepository settings,
        IOptions<OllamaOptions> ollamaDefaults,
        IOptions<GeminiOptions> geminiDefaults,
        ILoggerFactory loggerFactory)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _ollamaDefaults = ollamaDefaults.Value;
        _geminiDefaults = geminiDefaults.Value;
        _loggerFactory = loggerFactory;
    }

    public async Task<ILlmClient> CreateAsync(CancellationToken ct)
    {
        var (client, _) = await CreateInternalAsync(ct);
        return client;
    }

    internal async Task<(ILlmClient Client, HttpClient Http)> CreateInternalForTestAsync()
    {
        return await CreateInternalAsync(CancellationToken.None);
    }

    private async Task<(ILlmClient Client, HttpClient Http)> CreateInternalAsync(CancellationToken ct)
    {
        var s = await _settings.GetLlmAsync(ct);
        return s.Provider switch
        {
            LlmProviders.Ollama => await BuildOllamaAsync(s, ct),
            LlmProviders.Gemini => BuildGemini(s),
            _ => throw new InvalidOperationException($"Unbekannter LLM-Provider: '{s.Provider}'."),
        };
    }

    private async Task<(ILlmClient, HttpClient)> BuildOllamaAsync(LlmSettings s, CancellationToken ct)
    {
        var ollamaUser = await _settings.GetOllamaAsync(ct);

        var http = _httpFactory.CreateClient("Ollama");
        http.BaseAddress = new Uri(ollamaUser.Host.TrimEnd('/') + "/v1/");

        if (!string.IsNullOrWhiteSpace(ollamaUser.ApiKey))
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ollamaUser.ApiKey);
        }

        var options = new OpenAICompatibleLlmOptions(
            Model: s.OllamaModel,
            InitialTimeoutSeconds: _ollamaDefaults.InitialTimeoutSeconds,
            TokenTimeoutSeconds: _ollamaDefaults.TokenTimeoutSeconds,
            SystemPrompt: _ollamaDefaults.SystemPrompt,
            Temperature: ollamaUser.Temperature,
            NumCtx: ollamaUser.NumCtx);

        var logger = _loggerFactory.CreateLogger<OpenAICompatibleLlmClient>();
        return (new OpenAICompatibleLlmClient(http, options, logger), http);
    }

    private (ILlmClient, HttpClient) BuildGemini(LlmSettings s)
    {
        if (string.IsNullOrEmpty(s.GeminiApiKey))
        {
            throw new InvalidOperationException(
                "Gemini-Provider aktiviert, aber kein API-Key konfiguriert.");
        }

        var http = _httpFactory.CreateClient("Gemini");
        http.BaseAddress = new Uri(_geminiDefaults.BaseAddress);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.GeminiApiKey);

        var systemPrompt = _geminiDefaults.SystemPrompt ?? _ollamaDefaults.SystemPrompt;

        var options = new OpenAICompatibleLlmOptions(
            Model: s.GeminiModel,
            InitialTimeoutSeconds: _geminiDefaults.InitialTimeoutSeconds,
            TokenTimeoutSeconds: _geminiDefaults.TokenTimeoutSeconds,
            SystemPrompt: systemPrompt,
            Temperature: _geminiDefaults.Temperature,
            NumCtx: null);

        var logger = _loggerFactory.CreateLogger<OpenAICompatibleLlmClient>();
        return (new OpenAICompatibleLlmClient(http, options, logger), http);
    }
}
