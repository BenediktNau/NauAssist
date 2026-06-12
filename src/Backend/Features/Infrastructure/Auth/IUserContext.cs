namespace NauAssist.Backend.Features.Infrastructure.Auth;

/// <summary>
/// Liefert die User-ID (Keycloak <c>sub</c> bzw. <see cref="DefaultUser.Id"/>)
/// für den aktuellen Scope — Request oder Background-Lauf.
/// </summary>
public interface IUserContext
{
    string UserId { get; }
}

/// <summary>
/// Setter-Seite des User-Kontexts. Wird von der Auth-Middleware (HTTP) bzw.
/// vom Scheduler (Background, pro User-Scope) bedient — nie aus Feature-Code.
/// </summary>
public interface IUserContextSetter
{
    void Set(string userId);
}

public sealed class UserContextHolder : IUserContext, IUserContextSetter
{
    public string UserId { get; private set; } = DefaultUser.Id;

    public void Set(string userId) => UserId = userId;
}
