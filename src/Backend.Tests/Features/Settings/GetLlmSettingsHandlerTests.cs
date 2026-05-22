using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.GetLlmSettings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class GetLlmSettingsHandlerTests
{
    [Fact]
    public async Task Handle_DefaultsFromMigration()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        var handler = new GetLlmSettingsHandler(repo);

        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.Provider.Should().Be("ollama");
        response.OllamaModel.Should().Be("gemma4:26b");
        response.GeminiModel.Should().Be("gemini-2.5-flash");
        response.HasGeminiApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_HasGeminiApiKeyTrue_AfterKeyIsSet()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        await repo.SetLlmAsync(
            new LlmSettings("ollama", "gemma4:26b", "gemini-2.5-flash", "AIza-x"),
            CancellationToken.None);

        var handler = new GetLlmSettingsHandler(repo);
        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.HasGeminiApiKey.Should().BeTrue();
    }
}
