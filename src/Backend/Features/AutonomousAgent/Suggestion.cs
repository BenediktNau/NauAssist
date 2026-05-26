namespace NauAssist.Backend.Features.AutonomousAgent;

public sealed record Suggestion(
    long Id,
    string Source,
    string SourceRef,
    string Intent,
    string? Topic,
    string? Requester,
    string? QuotedText,
    IReadOnlyList<SuggestionSlot> Slots,
    string DraftReply,
    SuggestionStatus Status,
    int? PickedSlot,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RespondedAt);
