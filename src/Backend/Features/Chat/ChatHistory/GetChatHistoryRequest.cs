using Mediator;

namespace NauAssist.Backend.Features.Chat.ChatHistory;

public sealed record GetChatHistoryRequest(string SessionId) : IRequest<GetChatHistoryResponse>;

public sealed record GetChatHistoryResponse(
    IReadOnlyList<Message> Messages,
    IReadOnlyList<ChatClearMarker> Markers);
