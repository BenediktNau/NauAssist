using Mediator;

namespace NauAssist.Backend.Features.Chat.ChatHistory;

public sealed record GetChatHistoryRequest(string SessionId, int Take = 50) : IRequest<GetChatHistoryResponse>;

public sealed record GetChatHistoryResponse(IReadOnlyList<Message> Messages);
