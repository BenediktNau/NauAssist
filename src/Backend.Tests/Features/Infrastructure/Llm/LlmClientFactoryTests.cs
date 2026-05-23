using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Llm.Gemini;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Llm;

public sealed class LlmClientFactoryTests
{
    [Fact]
    public async Task Create_OllamaProvider_BuildsClient_WithoutAuth()
    {
        var factory = NewFactory(new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null));
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("http://localhost:11434/v1/");
        http.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Create_OllamaProvider_WithApiKey_BuildsClient_WithBearer()
    {
        var factory = NewFactory(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null),
            ollamaApiKey: "tok-abc");
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("http://localhost:11434/v1/");
        http.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        http.DefaultRequestHeaders.Authorization.Parameter.Should().Be("tok-abc");
    }

    [Fact]
    public async Task Create_GeminiProvider_WithKey_BuildsClient_WithBearer()
    {
        var factory = NewFactory(new LlmSettings("gemini", "gemma4:26b", "gemini-2.5-flash", "AIza-xyz"));
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("https://generativelanguage.googleapis.com/v1beta/openai/");
        http.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        http.DefaultRequestHeaders.Authorization.Parameter.Should().Be("AIza-xyz");
    }

    [Fact]
    public async Task Create_GeminiProvider_WithoutKey_Throws()
    {
        var factory = NewFactory(new LlmSettings("gemini", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: null));

        var act = async () => await factory.CreateAsync(CancellationToken.None);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Gemini*Key*");
    }

    [Fact]
    public async Task Create_UnknownProvider_Throws()
    {
        var factory = NewFactory(new LlmSettings("anthropic", "gemma4:26b", "gemini-2.5-flash", null));

        var act = async () => await factory.CreateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static LlmClientFactory NewFactory(LlmSettings settings, string? ollamaApiKey = null)
    {
        var ollama = Options.Create(new OllamaOptions
        {
            Host = "http://localhost:11434",
            ApiKey = ollamaApiKey,
            Model = "gemma4:26b",
            InitialTimeoutSeconds = 60,
            TokenTimeoutSeconds = 30,
            SystemPrompt = "sys",
            NumCtx = 16384,
            Temperature = 0.3,
        });
        var gemini = Options.Create(new GeminiOptions
        {
            BaseAddress = "https://generativelanguage.googleapis.com/v1beta/openai/",
            InitialTimeoutSeconds = 60,
            TokenTimeoutSeconds = 30,
            SystemPrompt = null,
            Temperature = 0.3,
        });

        var repo = new FakeSettingsRepo(settings);
        var httpFactory = new TestHttpClientFactory();
        var loggerFactory = NullLoggerFactory.Instance;

        return new LlmClientFactory(httpFactory, repo, ollama, gemini, loggerFactory);
    }

    private sealed class FakeSettingsRepo : IAppSettingsRepository
    {
        private LlmSettings _settings;
        public FakeSettingsRepo(LlmSettings s) => _settings = s;
        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) => Task.FromResult(_settings);
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct) { _settings = s; return Task.CompletedTask; }

        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            throw new NotImplementedException();
        public Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            throw new NotImplementedException();
        public Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            throw new NotImplementedException();
        public Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
