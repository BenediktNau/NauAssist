using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ILoggerFactory _loggerFactory;

    public LlmClientFactory(
        IHttpClientFactory httpFactory,
        IAppSettingsRepository settings,
        IOptions<OllamaOptions> ollamaDefaults,
        ILoggerFactory loggerFactory)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _ollamaDefaults = ollamaDefaults.Value;
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
}
