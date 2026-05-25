using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Calendar;

namespace NauAssist.Backend.Features.Calendar.DeleteEvent;

public sealed class DeleteEventHandler : IRequestHandler<DeleteEventRequest, DeleteEventResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICalendarProvider _calendar;
    private readonly AuditLogRepository _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<DeleteEventHandler> _logger;

    public DeleteEventHandler(
        ICalendarProvider calendar,
        AuditLogRepository audit,
        Func<DateTimeOffset> clock,
        ILogger<DeleteEventHandler> logger)
    {
        _calendar = calendar;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<DeleteEventResponse> Handle(DeleteEventRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            throw new ArgumentException("EventId darf nicht leer sein.", nameof(request));
        }

        await _calendar.DeleteEventAsync(request.EventId, request.Scope, cancellationToken);

        await TryWriteAuditAsync(
            toolName: "delete_event",
            argsJson: JsonSerializer.Serialize(request, JsonOptions),
            resultJson: JsonSerializer.Serialize(new { id = request.EventId, scope = request.Scope.ToString(), deleted = true }, JsonOptions),
            providerEventId: request.EventId,
            cancellationToken);

        return new DeleteEventResponse(request.EventId, request.Scope);
    }

    private async Task TryWriteAuditAsync(
        string toolName, string argsJson, string resultJson, string? providerEventId, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new AuditEntry(
                Id: 0,
                TriggeringMessageId: null,
                ToolName: toolName,
                ToolArgsJson: argsJson,
                ResultJson: resultJson,
                ProviderEventId: providerEventId,
                CreatedAt: _clock()),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit-Eintrag für {Tool} fehlgeschlagen.", toolName);
        }
    }
}
