namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>Eine konkrete Belegstelle aus den gesammelten Quellen.</summary>
public sealed record JudgeEvidence(string Shop, string? Price, string Url, string Quote);

/// <summary>
/// Strukturiertes Urteil des Judge: ist das Ziel erfüllt, wie sicher, womit belegt.
/// Der Judge gibt ausschließlich dieses Urteil zurück — er löst nie Aktionen aus.
/// </summary>
public sealed record WatchJudgeResult(
    bool Met,
    double Confidence,
    IReadOnlyList<JudgeEvidence> Evidence,
    string Summary);
