using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateOllamaSettingsHandlerTests
{
    [Fact]
    public async Task Handle_InvalidHostUri_ReturnsError()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("not a url", null, 16384, 0.3),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("Host");
    }

    [Fact]
    public async Task Handle_NegativeNumCtx_ReturnsError()
    {
        var handler = new UpdateOllamaSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://localhost:11434", null, -1, 0.3),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("NumCtx");
    }

    [Fact]
    public async Task Handle_TemperatureOutOfRange_ReturnsError()
    {
        var handler = new UpdateOllamaSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://h", null, 8192, 5.0),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("Temperature");
    }

    [Fact]
    public async Task Handle_NullApiKey_DoesNotOverwriteExistingKey()
    {
        var repo = new InMemoryRepo();
        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", "kept", 8192, 0.3),
            CancellationToken.None);
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://h2", ApiKey: null, NumCtx: 4096, Temperature: 0.5),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Current.ApiKey.Should().Be("kept");
        repo.Current.Host.Should().Be("http://h2");
    }

    [Fact]
    public async Task Handle_EmptyApiKey_ClearsKey()
    {
        var repo = new InMemoryRepo();
        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", "to-delete", 8192, 0.3),
            CancellationToken.None);
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("http://h", ApiKey: "", NumCtx: 8192, Temperature: 0.3),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Current.ApiKey.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidUpdate_Persists()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateOllamaSettingsHandler(repo);

        var r = await handler.Handle(
            new UpdateOllamaSettingsRequest("https://ollama.lan", "key123", 8192, 0.7),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Current.Host.Should().Be("https://ollama.lan");
        repo.Current.ApiKey.Should().Be("key123");
        repo.Current.NumCtx.Should().Be(8192);
        repo.Current.Temperature.Should().Be(0.7);
    }

    private sealed class InMemoryRepo : IAppSettingsRepository
    {
        public OllamaUserSettings Current { get; private set; } =
            new("http://localhost:11434", null, 16384, 0.3);

        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            Task.FromResult(Current);
        public Task SetOllamaAsync(OllamaUserSettings s, CancellationToken ct)
        {
            Current = s;
            return Task.CompletedTask;
        }

        // Unused stubs:
        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) =>
            Task.FromResult(new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", null));
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct) => Task.CompletedTask;
        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            Task.FromResult(new CalendarUserSettings("primary", new(9, 0), new(18, 0), 60, 14));
        public Task SetCalendarAsync(CalendarUserSettings s, CancellationToken ct) =>
            Task.CompletedTask;
        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            Task.FromResult<GoogleCredentials?>(null);
        public Task SetGoogleCredentialsAsync(GoogleCredentials c, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
