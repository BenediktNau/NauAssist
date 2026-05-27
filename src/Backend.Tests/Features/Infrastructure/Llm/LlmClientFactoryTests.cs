using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Llm;

public sealed class LlmClientFactoryTests
{
    [Fact]
    public async Task Create_BuildsOllamaClient_WithoutAuth()
    {
        var factory = NewFactory(new LlmSettings("gemma4:26b"));
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("http://localhost:11434/v1/");
        http.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Create_WithApiKey_BuildsOllamaClient_WithBearer()
    {
        var factory = NewFactory(new LlmSettings("gemma4:26b"), ollamaApiKey: "tok-abc");
        var (client, http) = await factory.CreateInternalForTestAsync();

        client.Should().BeOfType<OpenAICompatibleLlmClient>();
        http.BaseAddress!.AbsoluteUri.Should().Be("http://localhost:11434/v1/");
        http.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        http.DefaultRequestHeaders.Authorization.Parameter.Should().Be("tok-abc");
    }

    [Fact]
    public async Task Create_NoSystemPromptInDb_UsesOptionsDefault()
    {
        var factory = NewFactory(
            new LlmSettings("gemma4:26b", null),
            optionsSystemPrompt: "from-options");

        var options = await factory.GetOptionsForTestAsync();

        options.SystemPrompt.Should().Be("from-options");
    }

    [Fact]
    public async Task Create_SystemPromptInDb_OverridesOptionsDefault()
    {
        var factory = NewFactory(
            new LlmSettings("gemma4:26b", "from-db"),
            optionsSystemPrompt: "from-options");

        var options = await factory.GetOptionsForTestAsync();

        options.SystemPrompt.Should().Be("from-db");
    }

    [Fact]
    public async Task Create_WhitespaceSystemPromptInDb_FallsBackToOptions()
    {
        var factory = NewFactory(
            new LlmSettings("gemma4:26b", "   "),
            optionsSystemPrompt: "from-options");

        var options = await factory.GetOptionsForTestAsync();

        options.SystemPrompt.Should().Be("from-options");
    }

    private static LlmClientFactory NewFactory(
        LlmSettings settings,
        string? ollamaApiKey = null,
        string? optionsSystemPrompt = "sys")
    {
        var ollama = Options.Create(new OllamaOptions
        {
            Model = "gemma4:26b",
            InitialTimeoutSeconds = 60,
            TokenTimeoutSeconds = 30,
            SystemPrompt = optionsSystemPrompt,
        });

        var repo = new FakeSettingsRepo(
            settings,
            new OllamaUserSettings("http://localhost:11434", ollamaApiKey, 16384, 0.3));
        var httpFactory = new TestHttpClientFactory();
        var loggerFactory = NullLoggerFactory.Instance;

        return new LlmClientFactory(httpFactory, repo, ollama, loggerFactory);
    }

    private sealed class FakeSettingsRepo : IAppSettingsRepository
    {
        private LlmSettings _settings;
        private readonly OllamaUserSettings _ollama;
        public FakeSettingsRepo(LlmSettings s, OllamaUserSettings ollama)
        {
            _settings = s;
            _ollama = ollama;
        }

        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) => Task.FromResult(_settings);
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct)
        {
            _settings = s; return Task.CompletedTask;
        }

        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            Task.FromResult(_ollama);
        public Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            throw new NotImplementedException();
        public Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            throw new NotImplementedException();
        public Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<string> GetUserPersonaAsync(CancellationToken ct) => Task.FromResult(string.Empty);
        public Task SetUserPersonaAsync(string text, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
