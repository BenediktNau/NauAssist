using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.Chat;

public abstract record SseEvent(string EventName);

public sealed record SseToken(string Text) : SseEvent("token");
public sealed record SseToolStarted(string Name) : SseEvent("tool_started");
public sealed record SseToolFinished(string Name, bool Ok) : SseEvent("tool_finished");
public sealed record SseProposals(IReadOnlyList<SlotInfo> Slots) : SseEvent("proposals");
public sealed record SseDone(long MessageId) : SseEvent("done");
public sealed record SseError(string Message, string? CorrelationId = null) : SseEvent("error");
