using System.Security.Claims;

namespace NauAssist.Backend.Features.Infrastructure.Auth;

/// <summary>
/// Überträgt die Keycloak-Identität (<c>sub</c>) aus dem authentifizierten Principal
/// in den scoped <see cref="UserContextHolder"/> und provisioniert den User per
/// Upsert (Keycloak bleibt Source-of-Truth, kein eigenes User-Management).
/// Nicht authentifizierte Requests laufen als Default-User weiter.
/// </summary>
public sealed class UserContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IUserContextSetter setter,
        UserRepository users,
        Func<DateTimeOffset> clock)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirstValue("sub")
                      ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(sub))
            {
                setter.Set(sub);
                await users.UpsertAsync(
                    sub,
                    username: context.User.FindFirstValue("preferred_username"),
                    email: context.User.FindFirstValue("email"),
                    now: clock(),
                    ct: context.RequestAborted);
            }
        }

        await _next(context);
    }
}
