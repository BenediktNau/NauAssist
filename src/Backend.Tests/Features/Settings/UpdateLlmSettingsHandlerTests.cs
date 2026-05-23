using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateLlmSettings;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateLlmSettingsHandlerTests
{
    [Fact]
    public async Task Handle_UnknownProvider_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("anthropic", "gemma4:26b", "gemini-2.5-flash", null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("provider");
    }

    [Fact]
    public async Task Handle_EmptyOllamaModel_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "", "gemini-2.5-flash", null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("ollamaModel");
    }

    [Fact]
    public async Task Handle_CustomOllamaModel_Succeeds()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "mistral:7b", "gemini-2.5-flash", null),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.OllamaModel.Should().Be("mistral:7b");
    }

    [Fact]
    public async Task Handle_InvalidGeminiModel_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "gemma4:26b", "gpt-4", null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("geminiModel");
    }

    [Fact]
    public async Task Handle_SwitchToGeminiWithoutKey_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("gemini", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("API-Key");
    }

    [Fact]
    public async Task Handle_SwitchToGeminiWithKey_Succeeds()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("gemini", "gemma4:26b", "gemini-2.5-flash", "AIza-xyz"),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
        repo.Current.Provider.Should().Be("gemini");
        repo.Current.GeminiApiKey.Should().Be("AIza-xyz");
    }

    [Fact]
    public async Task Handle_NullKey_DoesNotOverwriteExistingKey()
    {
        var repo = new InMemorySettingsRepo();
        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", "AIza-original"),
            CancellationToken.None);
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("gemini", "gemma4:26b", "gemini-2.5-pro", GeminiApiKey: null),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.GeminiApiKey.Should().Be("AIza-original");
        repo.Current.GeminiModel.Should().Be("gemini-2.5-pro");
    }

    [Fact]
    public async Task Handle_EmptyStringKey_DeletesKey()
    {
        var repo = new InMemorySettingsRepo();
        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", "AIza-original"),
            CancellationToken.None);
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("ollama", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: ""),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.GeminiApiKey.Should().BeNull();
    }

    private sealed class InMemorySettingsRepo : IAppSettingsRepository
    {
        public LlmSettings Current { get; private set; } =
            new("ollama", "gemma4:26b", "gemini-2.5-flash", null);

        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) => Task.FromResult(Current);

        public Task SetLlmAsync(LlmSettings settings, CancellationToken ct)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            Task.FromResult(new OllamaUserSettings("http://localhost:11434", null, 16384, 0.3));

        public Task SetOllamaAsync(OllamaUserSettings settings, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            Task.FromResult(new CalendarUserSettings(
                "primary", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 14));

        public Task SetCalendarAsync(CalendarUserSettings settings, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            Task.FromResult<GoogleCredentials?>(null);

        public Task SetGoogleCredentialsAsync(GoogleCredentials credentials, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
