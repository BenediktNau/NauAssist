namespace NauAssist.Backend.Features.Infrastructure.Auth;

/// <summary>
/// Der implizite Single-User-Betrieb (Auth aus) läuft vollständig unter dieser ID.
/// Bestehende Daten wurden in Migration 0014 per DEFAULT auf diese ID gehoben.
/// </summary>
public static class DefaultUser
{
    public const string Id = "nauassist-default";
}
