using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;

namespace NauAssist.Backend.Features.Calendar.CreateEvent;

public sealed class CreateEventHandler : IRequestHandler<CreateEventRequest, CreateEventResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICalendarProvider _calendar;
    private readonly AuditLogRepository _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<CreateEventHandler> _logger;

    public CreateEventHandler(
        ICalendarProvider calendar,
        AuditLogRepository audit,
        Func<DateTimeOffset> clock,
        ILogger<CreateEventHandler> logger)
    {
        _calendar = calendar;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<CreateEventResponse> Handle(CreateEventRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Title darf nicht leer sein.", nameof(request));
        }

        if (request.End <= request.Start)
        {
            throw new ArgumentException("End muss nach Start liegen.", nameof(request));
        }

        var newEvent = new NewEvent(
            Title: request.Title.Trim(),
            Start: request.Start,
            End: request.End,
            Description: request.Description,
            Location: request.Location,
            IsAllDay: request.IsAllDay);

        var id = await _calendar.CreateEventAsync(newEvent, cancellationToken);

        await TryWriteAuditAsync(
            toolName: "create_event",
            argsJson: JsonSerializer.Serialize(request, JsonOptions),
            resultJson: JsonSerializer.Serialize(new { id }, JsonOptions),
            providerEventId: id,
            cancellationToken);

        return new CreateEventResponse(id);
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
