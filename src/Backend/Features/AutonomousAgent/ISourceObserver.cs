namespace NauAssist.Backend.Features.AutonomousAgent;

/// <summary>
/// Quellen-spezifischer Adapter: holt neue Rohsignale seit dem letzten Cursor.
/// Implementierungen sind Scoped — werden pro Tick neu erzeugt.
/// </summary>
public interface ISourceObserver
{
    /// <summary>Source-Kennung, z.B. "matrix" oder "gmail".</summary>
    string Source { get; }

    /// <summary>
    /// Holt neue Signale. Implementierungen sind selbst zuständig für Cursor-Pflege
    /// (Lesen + Schreiben in source_cursors).
    /// </summary>
    Task<IReadOnlyList<RawSignal>> PollAsync(CancellationToken ct);
}
