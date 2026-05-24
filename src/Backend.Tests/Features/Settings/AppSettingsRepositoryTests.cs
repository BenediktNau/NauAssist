using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryTests
{
    [Fact]
    public async Task GetLlm_ReturnsSeededDefault()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var settings = await repo.GetLlmAsync(CancellationToken.None);

        settings.OllamaModel.Should().Be("gemma4:26b");
    }

    [Fact]
    public async Task SetLlm_RoundtripsOllamaModel()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetLlmAsync(new LlmSettings("qwen2.5:7b-instruct"), CancellationToken.None);

        var loaded = await repo.GetLlmAsync(CancellationToken.None);
        loaded.OllamaModel.Should().Be("qwen2.5:7b-instruct");
    }
}
