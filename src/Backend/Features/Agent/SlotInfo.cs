namespace NauAssist.Backend.Features.Agent;

public sealed record SlotInfo(
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Note);
