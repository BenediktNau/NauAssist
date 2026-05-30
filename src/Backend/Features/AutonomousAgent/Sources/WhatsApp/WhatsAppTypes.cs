namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>Antwort von <c>POST /sessions</c>.</summary>
public sealed record WhatsAppSession(string SessionId, string State);

/// <summary>Antwort von <c>GET /sessions/:id</c> (QR-Polling).</summary>
public sealed record WhatsAppSessionStatus(string State, string? Qr, string? Phone);

/// <summary>Ein Chat aus <c>GET /sessions/:id/chats</c> (für die Allowlist-Auswahl).</summary>
public sealed record WhatsAppChat(string ChatId, string Name);

/// <summary>Eine gepufferte Nachricht aus <c>GET /sessions/:id/messages</c>.</summary>
public sealed record WhatsAppMessage(
    long Seq,
    string MsgId,
    string ChatId,
    string? ChatName,
    string? Sender,
    string? SenderName,
    string Text,
    long Ts,
    bool FromMe);

/// <summary>Seite + neuer Cursor aus <c>GET /sessions/:id/messages</c>.</summary>
public sealed record WhatsAppMessagePage(IReadOnlyList<WhatsAppMessage> Messages, long Cursor);
