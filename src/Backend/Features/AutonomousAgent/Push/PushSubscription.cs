namespace NauAssist.Backend.Features.AutonomousAgent.Push;

public sealed record PushSubscription(
    long Id,
    string Endpoint,
    string P256dh,
    string Auth,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsed);
