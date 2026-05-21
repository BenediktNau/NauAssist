using Mediator;

namespace NauAssist.Backend.Features.Chat.ChatHistory;

public sealed class GetChatHistoryHandler : IRequestHandler<GetChatHistoryRequest, GetChatHistoryResponse>
{
    private readonly MessageRepository _messages;
    private readonly ChatClearMarkerRepository _markers;
    private readonly ChatContextCutoff _cutoff;

    public GetChatHistoryHandler(
        MessageRepository messages,
        ChatClearMarkerRepository markers,
        ChatContextCutoff cutoff)
    {
        _messages = messages;
        _markers = markers;
        _cutoff = cutoff;
    }

    public async ValueTask<GetChatHistoryResponse> Handle(
        GetChatHistoryRequest request, CancellationToken cancellationToken)
    {
        var dayStart = _cutoff.ComputeDayStart();
        var messages = await _messages.GetAllSinceAsync(request.SessionId, dayStart, cancellationToken);
        var markers = await _markers.GetAllSinceAsync(request.SessionId, dayStart, cancellationToken);
        return new GetChatHistoryResponse(messages, markers);
    }
}
