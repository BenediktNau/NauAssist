using Google.Apis.Auth.OAuth2.Flows;
using Microsoft.Extensions.Caching.Memory;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class AuthSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly IMemoryCache _cache;

    public AuthSessionStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string Put(GoogleAuthorizationCodeFlow flow)
    {
        var id = Guid.NewGuid().ToString("N");
        _cache.Set(Key(id), flow, Ttl);
        return id;
    }

    public GoogleAuthorizationCodeFlow? Take(string id)
    {
        if (_cache.TryGetValue<GoogleAuthorizationCodeFlow>(Key(id), out var flow))
        {
            _cache.Remove(Key(id));
            return flow;
        }
        return null;
    }

    private static string Key(string id) => $"oauth-session:{id}";
}
