using AwesomeAssertions;
using Google.Apis.Auth.OAuth2.Flows;
using Microsoft.Extensions.Caching.Memory;
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Tests.Features.Calendar.Google;

public sealed class AuthSessionStoreTests
{
    private static GoogleAuthorizationCodeFlow MakeFlow() =>
        new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new global::Google.Apis.Auth.OAuth2.ClientSecrets
            {
                ClientId = "x", ClientSecret = "y",
            },
            Scopes = new[] { "scope" },
        });

    [Fact]
    public void PutThenTake_ReturnsFlow_AndRemovesSession()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new AuthSessionStore(cache);
        var flow = MakeFlow();

        var id = store.Put(flow);
        var taken = store.Take(id);
        var second = store.Take(id);

        taken.Should().BeSameAs(flow);
        second.Should().BeNull();
    }

    [Fact]
    public void Take_UnknownId_ReturnsNull()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new AuthSessionStore(cache);

        store.Take("nope").Should().BeNull();
    }
}
