namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Bindet <c>AutonomousAgent:WhatsApp</c>. <see cref="Enabled"/> ist Default false —
/// WhatsApp ist opt-in (kein Sidecar, keine Endpoints, keine UI ohne diesen Schalter).
/// </summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    public string SidecarBaseUrl { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public int MaxBodyChars { get; set; } = 2000;
    public int MessageBatchLimit { get; set; } = 200;
}
