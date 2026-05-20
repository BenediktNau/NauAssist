namespace NauAssist.Backend.Features.Infrastructure.Audit;

public sealed record AuditEntry(
    long Id,
    long? TriggeringMessageId,
    string ToolName,
    string ToolArgsJson,
    string ResultJson,
    string? ProviderEventId,
    DateTimeOffset CreatedAt);
