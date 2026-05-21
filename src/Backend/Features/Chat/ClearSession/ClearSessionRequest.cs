using Mediator;

namespace NauAssist.Backend.Features.Chat.ClearSession;

public sealed record ClearSessionRequest(string SessionId) : IRequest<ClearSessionResponse>;

public sealed record ClearSessionResponse(long Id, DateTimeOffset CreatedAt);
