namespace NauAssist.Backend.Features.Agent;

public abstract record AgentStreamEvent;

public sealed record TokenEvent(string Text) : AgentStreamEvent;
public sealed record ToolStartedEvent(string Name) : AgentStreamEvent;
public sealed record ToolFinishedEvent(string Name, bool Ok) : AgentStreamEvent;
public sealed record ProposalsEvent(IReadOnlyList<SlotInfo> Slots) : AgentStreamEvent;
public sealed record DoneEvent() : AgentStreamEvent;
public sealed record ErrorEvent(string Message, string? CorrelationId = null) : AgentStreamEvent;
