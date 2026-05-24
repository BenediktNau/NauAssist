using FluentAssertions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
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
        var handler = new GetLlmSettingsHandler(repo, OllamaOpts("default-prompt"));

        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.OllamaModel.Should().Be("gemma4:26b");
        response.SystemPrompt.Should().BeNull();
        response.DefaultSystemPrompt.Should().Be("default-prompt");
    }

    [Fact]
    public async Task Handle_ReturnsUpdatedModelAndSystemPrompt()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        await repo.SetLlmAsync(
            new LlmSettings("mistral:7b", "you are a helper"),
            CancellationToken.None);

        var handler = new GetLlmSettingsHandler(repo, OllamaOpts("default-prompt"));
        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.OllamaModel.Should().Be("mistral:7b");
        response.SystemPrompt.Should().Be("you are a helper");
        response.DefaultSystemPrompt.Should().Be("default-prompt");
    }

    [Fact]
    public async Task Handle_NullDefaultSystemPrompt_ReturnsEmptyString()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        var handler = new GetLlmSettingsHandler(repo, OllamaOpts(null));

        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.DefaultSystemPrompt.Should().Be("");
    }

    private static IOptions<OllamaOptions> OllamaOpts(string? systemPrompt) =>
        Options.Create(new OllamaOptions { SystemPrompt = systemPrompt });
}
