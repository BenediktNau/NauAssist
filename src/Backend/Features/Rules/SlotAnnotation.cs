namespace NauAssist.Backend.Features.Rules;

/// <summary>
/// Ein Vorschlag im Zeitfenster, der gegen Regeln geprüft wird.
/// Plan A definiert diesen Typ hier; Plan B (Kalender) wird ihn nutzen.
/// </summary>
public sealed record SlotCandidate(DateTimeOffset Start, DateTimeOffset End);

public enum AnnotationStatus
{
    Passes,
    SoftViolation,
    HardViolation,
}

public sealed record SlotAnnotation(
    SlotCandidate Slot,
    AnnotationStatus Status,
    Rule? ViolatedBy);
