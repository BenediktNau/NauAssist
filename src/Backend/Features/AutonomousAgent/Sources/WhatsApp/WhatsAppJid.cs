namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Bringt eine WhatsApp-JID auf eine kanonische Vergleichsform: Device-/Agent-Suffix
/// entfernen, lowercase, c.us → s.whatsapp.net. Spiegelt Baileys' jidNormalizedUser.
/// Verschiedene Domains (lid vs s.whatsapp.net) bleiben bewusst verschieden — ihr
/// User-Teil ist je Domain ein anderer Identifier (siehe jid-utils.js).
/// </summary>
public static class WhatsAppJid
{
    public static string Normalize(string? jid)
    {
        if (string.IsNullOrWhiteSpace(jid)) return "";
        var at = jid.IndexOf('@');
        if (at < 0) return jid.Trim().ToLowerInvariant();

        var userCombined = jid[..at];
        var server = jid[(at + 1)..].ToLowerInvariant();
        // user:device → user ; user_agent → user
        var user = userCombined.Split(':')[0].Split('_')[0].ToLowerInvariant();
        if (server == "c.us") server = "s.whatsapp.net";
        return $"{user}@{server}";
    }
}
