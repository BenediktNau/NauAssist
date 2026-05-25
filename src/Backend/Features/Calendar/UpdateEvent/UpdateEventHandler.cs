using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;

namespace NauAssist.Backend.Features.Calendar.UpdateEvent;

public sealed class UpdateEventHandler : IRequestHandler<UpdateEventRequest, UpdateEventResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICalendarProvider _calendar;
    private readonly AuditLogRepository _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<UpdateEventHandler> _logger;

    public UpdateEventHandler(
        ICalendarProvider calendar,
        AuditLogRepository audit,
        Func<DateTimeOffset> clock,
        ILogger<UpdateEventHandler> logger)
    {
        _calendar = calendar;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<UpdateEventResponse> Handle(UpdateEventRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            throw new ArgumentException("EventId darf nicht leer sein.", nameof(request));
        }

        if (!request.Update.HasAnyChange)
        {
            throw new ArgumentException("Mindestens ein Feld muss geändert werden.", nameof(request));
        }

        if (request.Update.Start is { } s && request.Update.End is { } e && e <= s)
        {
            throw new ArgumentException("End muss nach Start liegen.", nameof(request));
        }

        if (request.Update.Title is not null && string.IsNullOrWhiteSpace(request.Update.Title))
        {
            throw new ArgumentException("Title darf nicht leer sein.", nameof(request));
        }

        await _calendar.UpdateEventAsync(request.EventId, request.Update, request.Scope, cancellationToken);

        await TryWriteAuditAsync(
            toolName: "update_event",
            argsJson: JsonSerializer.Serialize(request, JsonOptions),
            resultJson: JsonSerializer.Serialize(new { id = request.EventId, scope = request.Scope.ToString(), updated = true }, JsonOptions),
            providerEventId: request.EventId,
            cancellationToken);

        return new UpdateEventResponse(request.EventId, request.Scope);
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
