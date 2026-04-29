using System;

namespace NauAssist.Common.Logging;

/// <summary>
/// Trägt eine Korrelations-ID durch alle Stufen einer Verarbeitung — pro
/// HTTP-Anfrage (CorrelationIdMiddleware) und ab Etappe 5 pro
/// Reflexions-Iteration. Logs, Metriken und Audit-Einträge zitieren die
/// gleiche ID, damit ein Vorgang über Layer hinweg rekonstruierbar ist.
/// </summary>
public interface ICorrelationContext
{
    string? CurrentId { get; }

    /// <summary>Beginnt einen Korrelations-Scope. Verschachtelung unterstützt; beim Dispose wird der vorherige Wert wiederhergestellt.</summary>
    IDisposable Begin(string id);
}
