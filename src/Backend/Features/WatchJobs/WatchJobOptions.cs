namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Bindet <c>AutonomousAgent:WatchJobs</c>. <see cref="Enabled"/> ist Default false —
/// Watch-Jobs sind opt-in (kein Scheduler, keine Chat-Tools, keine Endpoints ohne Schalter).
/// </summary>
public sealed class WatchJobOptions
{
    /// <summary>Feature-Schalter. Ohne diesen verhält sich alles wie bisher.</summary>
    public bool Enabled { get; set; }

    /// <summary>Tick-Intervall des Schedulers (wie oft auf fällige Jobs geprüft wird).</summary>
    public int TickSeconds { get; set; } = 10;

    /// <summary>Erzwungene Untergrenze für das Prüfintervall (Schutz gegen IP-Sperren/Last).</summary>
    public int MinIntervalSeconds { get; set; } = 30;

    /// <summary>Maximale parallele Job-Executions über alle User.</summary>
    public int MaxConcurrent { get; set; } = 4;

    /// <summary>Obergrenze aktiver Jobs pro User (Kosten-/Last-Schutz).</summary>
    public int MaxActivePerUser { get; set; } = 10;

    /// <summary>Mindest-Confidence des Judge, damit ein Treffer feuert (analog autonomer Agent: 0,6).</summary>
    public double ConfidenceThreshold { get; set; } = 0.6;

    /// <summary>
    /// Hot-Mode-Intervall: greift, wenn der Judge ein Teilsignal meldet (etwas tut sich,
    /// ist aber unbestätigt). Darf MinIntervalSeconds bewusst unterschreiten; harte Untergrenze 10 s.
    /// </summary>
    public int HotIntervalSeconds { get; set; } = 15;
}
