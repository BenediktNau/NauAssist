namespace NauAssist.Backend.Features.Chat;

/// <summary>Bekannte Chat-Session-IDs. Phase 1 nutzt genau eine Session.</summary>
public static class ChatSessions
{
    /// <summary>Die einzige Chat-Session (Frontend wie auch proaktive Nachrichten nutzen sie).</summary>
    public const string Default = "default";
}
