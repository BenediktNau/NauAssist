using Mediator;

namespace NauAssist.Backend.Features.Chat.ClearSession;

public sealed class ClearSessionHandler : IRequestHandler<ClearSessionRequest, ClearSessionResponse>
{
    private readonly ChatClearMarkerRepository _markers;
    private readonly Func<DateTimeOffset> _clock;

    public ClearSessionHandler(ChatClearMarkerRepository markers, Func<DateTimeOffset> clock)
    {
        _markers = markers;
        _clock = clock;
    }

    public async ValueTask<ClearSessionResponse> Handle(ClearSessionRequest request, CancellationToken cancellationToken)
    {
        var marker = await _markers.AddAsync(request.SessionId, _clock(), cancellationToken);
        return new ClearSessionResponse(marker.Id, marker.CreatedAt);
    }
}
