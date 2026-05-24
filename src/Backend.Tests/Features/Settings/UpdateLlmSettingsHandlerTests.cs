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
            new UpdateLlmSettingsRequest(""),
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
            new UpdateLlmSettingsRequest("mistral:7b"),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        repo.Current.OllamaModel.Should().Be("mistral:7b");
    }

    private sealed class InMemorySettingsRepo : IAppSettingsRepository
    {
        public LlmSettings Current { get; private set; } = new("gemma4:26b");

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
