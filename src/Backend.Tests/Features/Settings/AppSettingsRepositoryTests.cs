using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryTests
{
    [Fact]
    public async Task GetLlm_ReturnsSeededDefaults()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var settings = await repo.GetLlmAsync(CancellationToken.None);

        settings.Provider.Should().Be("ollama");
        settings.OllamaModel.Should().Be("gemma4:26b");
        settings.GeminiModel.Should().Be("gemini-2.5-flash");
        settings.GeminiApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetLlm_RoundtripsAllFields()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(
            new LlmSettings(
                Provider: "gemini",
                OllamaModel: "qwen2.5:7b-instruct",
                GeminiModel: "gemini-2.5-pro",
                GeminiApiKey: "AIza-testkey"),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);

        loaded.Provider.Should().Be("gemini");
        loaded.OllamaModel.Should().Be("qwen2.5:7b-instruct");
        loaded.GeminiModel.Should().Be("gemini-2.5-pro");
        loaded.GeminiApiKey.Should().Be("AIza-testkey");
    }

    [Fact]
    public async Task SetLlm_NullKey_PersistsAsEmpty_ReadsBackAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: null),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.GeminiApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetLlm_EmptyStringKey_ReadsBackAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", GeminiApiKey: ""),
            CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.GeminiApiKey.Should().BeNull();
    }
}
