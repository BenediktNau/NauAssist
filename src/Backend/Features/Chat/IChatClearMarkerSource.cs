namespace NauAssist.Backend.Features.Chat;

public interface IChatClearMarkerSource
{
    Task<DateTimeOffset?> GetLatestCreatedAtSinceAsync(string sessionId, DateTimeOffset since, CancellationToken ct);
}
