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
    /// (z.B. Matrix-Raum-ID). <paramref name="metadata"/> trägt optionale Reply-Header
    /// (z.B. Message-ID für In-Reply-To, From-Address für Subject). Wirft bei Fehlern.
    /// </summary>
    Task SendAsync(
        SourceAccount account,
        string targetRef,
        string body,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct);
}
