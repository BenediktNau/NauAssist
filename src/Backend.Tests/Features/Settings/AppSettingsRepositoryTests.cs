using NauAssist.Backend.Features.Infrastructure.Auth;
using AwesomeAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryTests
{
    [Fact]
    public async Task GetLlm_ReturnsSeededDefault()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        var settings = await repo.GetLlmAsync(CancellationToken.None);

        settings.OllamaModel.Should().Be("gemma4:26b");
        settings.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public async Task SetLlm_RoundtripsOllamaModel()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetLlmAsync(new LlmSettings("qwen2.5:7b-instruct", null), CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.OllamaModel.Should().Be("qwen2.5:7b-instruct");
        loaded.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public async Task SetLlm_RoundtripsSystemPrompt()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetLlmAsync(
            new LlmSettings("qwen2.5:7b-instruct", "Du bist Nau."),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.SystemPrompt.Should().Be("Du bist Nau.");
    }

    [Fact]
    public async Task SetLlm_EmptySystemPrompt_ReadsAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetLlmAsync(
            new LlmSettings("qwen2.5:7b-instruct", "Du bist Nau."),
            CancellationToken.None);
        await repo.SetLlmAsync(
            new LlmSettings("qwen2.5:7b-instruct", null),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.SystemPrompt.Should().BeNull();
    }
}
