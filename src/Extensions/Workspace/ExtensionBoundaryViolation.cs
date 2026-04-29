using System;

namespace NauAssist.Extensions.Workspace;

/// <summary>
/// Wird geworfen, wenn ein Pfad kanonisiert nicht innerhalb der
/// Erweiterungs-Welt liegt. Konzept §6: „Der Agent darf hier nicht
/// schreiben." — gemeint ist die Kernwelt, alles andere ist offen.
/// </summary>
public sealed class ExtensionBoundaryViolation : InvalidOperationException
{
    public ExtensionBoundaryViolation(string path, string reason)
        : base($"Pfad '{path}' liegt außerhalb der Erweiterungs-Welt: {reason}")
    {
        Path = path;
        Reason = reason;
    }

    public string Path { get; }

    public string Reason { get; }
}
