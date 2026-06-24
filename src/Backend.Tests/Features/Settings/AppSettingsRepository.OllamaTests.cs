using NauAssist.Backend.Features.Infrastructure.Auth;
using Dapper;
using AwesomeAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryOllamaTests
{
    [Fact]
    public async Task GetOllama_ReturnsSeededDefaults()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        var s = await repo.GetOllamaAsync(CancellationToken.None);

        s.Host.Should().Be("http://localhost:11434");
        s.ApiKey.Should().BeNull();
        s.NumCtx.Should().Be(16384);
        s.Temperature.Should().Be(0.3);
    }

    [Fact]
    public async Task SetOllama_RoundtripsAllFields()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetOllamaAsync(
            new OllamaUserSettings("https://ollama.lan:11434", "secret-key", 8192, 0.7),
            CancellationToken.None);

        var loaded = await repo.GetOllamaAsync(CancellationToken.None);

        loaded.Host.Should().Be("https://ollama.lan:11434");
        loaded.ApiKey.Should().Be("secret-key");
        loaded.NumCtx.Should().Be(8192);
        loaded.Temperature.Should().Be(0.7);
    }

    [Fact]
    public async Task SetOllama_EmptyApiKey_ReadsBackAsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", ApiKey: "", 16384, 0.3),
            CancellationToken.None);

        var loaded = await repo.GetOllamaAsync(CancellationToken.None);
        loaded.ApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetOllama_NullApiKey_PersistsAsEmpty()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetOllamaAsync(
            new OllamaUserSettings("http://h", ApiKey: null, 16384, 0.3),
            CancellationToken.None);

        using var conn = db.AppDb.OpenConnection();
        var raw = await conn.ExecuteScalarAsync<string>(
            "SELECT value FROM app_settings WHERE key = 'ollama.api_key';");
        raw.Should().Be("");
    }
}
