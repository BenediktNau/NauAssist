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

        response.OllamaModel.Should().Be("gemma4:26b");
    }

    [Fact]
    public async Task Handle_ReturnsUpdatedModel()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);
        await repo.SetLlmAsync(new LlmSettings("mistral:7b"), CancellationToken.None);

        var handler = new GetLlmSettingsHandler(repo);
        var response = await handler.Handle(new GetLlmSettingsRequest(), CancellationToken.None);

        response.OllamaModel.Should().Be("mistral:7b");
    }
}
