namespace NauAssist.Backend.Features.AutonomousAgent.Classification;

public sealed record ClassificationResult(
    string Intent,
    string? Topic,
    string? Requester,
    string? DateHint,
    int? DurationMinutes,
    string? DraftReply,
    double Confidence,
    string? PersonaUpdate);
