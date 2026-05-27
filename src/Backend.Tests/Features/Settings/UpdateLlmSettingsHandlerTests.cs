using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateLlmSettings;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateLlmSettingsHandlerTests
{
    [Fact]
    public async Task Handle_EmptyOllamaModel_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("", null),
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
            new UpdateLlmSettingsRequest("mistral:7b", null),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.OllamaModel.Should().Be("mistral:7b");
        repo.Current.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CustomSystemPrompt_IsTrimmedAndStored()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("mistral:7b", "   you are nau   "),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.SystemPrompt.Should().Be("you are nau");
    }

    [Fact]
    public async Task Handle_EmptySystemPrompt_StoresNullForFallback()
    {
        var repo = new InMemorySettingsRepo();
        repo.Current = new LlmSettings("mistral:7b", "previous");
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("mistral:7b", "   "),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TooLongSystemPrompt_ReturnsError()
    {
        var repo = new InMemorySettingsRepo();
        var handler = new UpdateLlmSettingsHandler(repo);

        var result = await handler.Handle(
            new UpdateLlmSettingsRequest("mistral:7b", new string('x', 4001)),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("systemPrompt");
    }

    private sealed class InMemorySettingsRepo : IAppSettingsRepository
    {
        public LlmSettings Current { get; set; } = new("gemma4:26b", null);

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

        public Task<string> GetUserPersonaAsync(CancellationToken ct) => Task.FromResult(string.Empty);
        public Task SetUserPersonaAsync(string text, CancellationToken ct) => Task.CompletedTask;
    }
}
