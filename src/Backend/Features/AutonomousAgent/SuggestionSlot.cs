namespace NauAssist.Backend.Features.AutonomousAgent;

public sealed record SuggestionSlot(
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Note);
