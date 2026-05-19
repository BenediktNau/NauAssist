namespace NauAssist.Backend.Features.Chat;

public enum MessageRole
{
    User,
    Assistant,
}

public sealed record Message(
    long Id,
    string SessionId,
    MessageRole Role,
    string Content,
    string? ProposalsJson,
    bool Incomplete,
    DateTimeOffset CreatedAt);
