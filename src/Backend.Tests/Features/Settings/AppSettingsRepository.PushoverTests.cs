using AwesomeAssertions;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryPushoverTests
{
    [Fact]
    public async Task Pushover_DefaultsToEmpty_AndRoundtrips()
    {
        using var temp = new TempSqliteDb();
        var repo = new AppSettingsRepository(temp.AppDb, new UserContextHolder());

        var initial = await repo.GetPushoverAsync(CancellationToken.None);
        initial.IsConfigured.Should().BeFalse();

        await repo.SetPushoverAsync(new PushoverSettings("app-token", "user-key"), CancellationToken.None);

        var loaded = await repo.GetPushoverAsync(CancellationToken.None);
        loaded.Token.Should().Be("app-token");
        loaded.UserKey.Should().Be("user-key");
        loaded.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Pushover_CanBeCleared()
    {
        using var temp = new TempSqliteDb();
        var repo = new AppSettingsRepository(temp.AppDb, new UserContextHolder());
        await repo.SetPushoverAsync(new PushoverSettings("t", "u"), CancellationToken.None);

        await repo.SetPushoverAsync(new PushoverSettings("", ""), CancellationToken.None);

        (await repo.GetPushoverAsync(CancellationToken.None)).IsConfigured.Should().BeFalse();
    }
}
