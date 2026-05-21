namespace NauAssist.Backend.Features.Chat;

public sealed record ChatClearMarker(
    long Id,
    string SessionId,
    DateTimeOffset CreatedAt);
