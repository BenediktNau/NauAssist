using NauAssist.Backend.Features.Infrastructure.Auth;
using Dapper;
using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryGoogleCredentialsTests
{
    [Fact]
    public async Task GetGoogleCredentials_FreshDb_ReturnsNull()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        var creds = await repo.GetGoogleCredentialsAsync(CancellationToken.None);

        creds.Should().BeNull();
    }

    [Fact]
    public async Task SetThenGet_Roundtrips()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        await repo.SetGoogleCredentialsAsync(
            new GoogleCredentials("123.apps.googleusercontent.com", "GOCSPX-secret"),
            CancellationToken.None);

        var loaded = await repo.GetGoogleCredentialsAsync(CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.ClientId.Should().Be("123.apps.googleusercontent.com");
        loaded.ClientSecret.Should().Be("GOCSPX-secret");
    }

    [Fact]
    public async Task SetGoogleCredentials_ClearsExistingOauthTokens()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb, new UserContextHolder());

        using (var conn = db.AppDb.OpenConnection())
        {
            await conn.ExecuteAsync(
                @"INSERT INTO google_oauth(key, value, updated_at)
                  VALUES('test-key', X'00', @ts);",
                new { ts = DateTimeOffset.UtcNow.ToString("O") });
        }

        await repo.SetGoogleCredentialsAsync(
            new GoogleCredentials("new-id", "new-secret"),
            CancellationToken.None);

        using (var conn = db.AppDb.OpenConnection())
        {
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM google_oauth;");
            count.Should().Be(0);
        }
    }
}
