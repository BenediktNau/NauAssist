using Mediator;

namespace NauAssist.Backend.Features.Chat.ChatHistory;

public sealed class GetChatHistoryHandler : IRequestHandler<GetChatHistoryRequest, GetChatHistoryResponse>
{
    private readonly MessageRepository _repo;

    public GetChatHistoryHandler(MessageRepository repo)
    {
        _repo = repo;
    }

    public async ValueTask<GetChatHistoryResponse> Handle(
        GetChatHistoryRequest request, CancellationToken cancellationToken)
    {
        var recent = await _repo.GetRecentAsync(request.SessionId, request.Take, cancellationToken);
        return new GetChatHistoryResponse(recent.Reverse().ToList());
    }
}
