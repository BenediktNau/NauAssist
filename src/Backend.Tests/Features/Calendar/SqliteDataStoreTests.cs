using FluentAssertions;
using Google.Apis.Auth.OAuth2.Responses;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class SqliteDataStoreTests
{
    [Fact]
    public async Task Store_PersistsValue_AndRetrievesIt()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);

        var token = new TokenResponse { AccessToken = "abc", RefreshToken = "xyz" };
        await store.StoreAsync("user-key", token);

        var loaded = await store.GetAsync<TokenResponse>("user-key");

        loaded.Should().NotBeNull();
        loaded!.AccessToken.Should().Be("abc");
        loaded.RefreshToken.Should().Be("xyz");
    }

    [Fact]
    public async Task Store_Overwrite_UpdatesValue()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);

        await store.StoreAsync("k", new TokenResponse { AccessToken = "v1" });
        await store.StoreAsync("k", new TokenResponse { AccessToken = "v2" });

        var loaded = await store.GetAsync<TokenResponse>("k");
        loaded!.AccessToken.Should().Be("v2");
    }

    [Fact]
    public async Task Delete_RemovesValue()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);
        await store.StoreAsync("k", new TokenResponse { AccessToken = "v" });

        await store.DeleteAsync<TokenResponse>("k");

        var loaded = await store.GetAsync<TokenResponse>("k");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);

        var loaded = await store.GetAsync<TokenResponse>("nope");

        loaded.Should().BeNull();
    }
}
