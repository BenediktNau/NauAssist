namespace NauAssist.Backend.Features.AutonomousAgent;

/// <summary>
/// Eingehende, noch nicht klassifizierte Nachricht aus einer externen Quelle.
/// Wird vom Observer geliefert und durchläuft Cheap-Filter → Classifier → Suggestion.
/// </summary>
public sealed record RawSignal(
    string Source,
    string SourceRef,
    string? Sender,
    string Text,
    DateTimeOffset ReceivedAt);
