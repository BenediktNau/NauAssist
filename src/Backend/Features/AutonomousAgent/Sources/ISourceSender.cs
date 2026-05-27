namespace NauAssist.Backend.Features.AutonomousAgent.Sources;

/// <summary>
/// Quellen-spezifischer Versand: schickt einen Antwort-Text zurück in dieselbe Konversation
/// (Matrix-Raum, Mail-Reply, …). Implementierungen sind Scoped — werden bei Bedarf pro Send-Call
/// neu aufgelöst.
/// </summary>
public interface ISourceSender
{
    /// <summary>Muss mit <see cref="SourceAccount.Kind"/> übereinstimmen.</summary>
    string Source { get; }

    /// <summary>
    /// Sendet <paramref name="body"/> in das Ziel, das <paramref name="targetRef"/> beschreibt
    /// (z.B. Matrix-Raum-ID). Wirft bei Fehlern.
    /// </summary>
    Task SendAsync(SourceAccount account, string targetRef, string body, CancellationToken ct);
}
