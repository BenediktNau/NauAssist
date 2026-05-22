using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Settings.GetLlmSettings;
using NauAssist.Backend.Features.Settings.UpdateLlmSettings;

namespace NauAssist.Backend.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/llm", async (
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new GetLlmSettingsRequest(), ct);
            return Results.Ok(new LlmSettingsDto(
                response.Provider,
                response.OllamaModel,
                response.GeminiModel,
                response.HasGeminiApiKey));
        });

        app.MapPut("/api/settings/llm", async (
            UpdateLlmSettingsPayload payload,
            IMediator mediator,
            AuditLogRepository audit,
            Func<DateTimeOffset> clock,
            ILogger<UpdateLlmSettingsResult> logger,
            CancellationToken ct) =>
        {
            var request = new UpdateLlmSettingsRequest(
                Provider: payload.Provider ?? "",
                OllamaModel: payload.OllamaModel ?? "",
                GeminiModel: payload.GeminiModel ?? "",
                GeminiApiKey: payload.GeminiApiKey);

            var result = await mediator.Send(request, ct);

            if (!result.Ok)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            var auditArgs = JsonSerializer.Serialize(new
            {
                provider = payload.Provider,
                ollamaModel = payload.OllamaModel,
                geminiModel = payload.GeminiModel,
                geminiKeyAction = payload.GeminiApiKey switch
                {
                    null => "unchanged",
                    "" => "cleared",
                    _ => "set",
                },
            });

            await audit.AppendAsync(
                new AuditEntry(
                    Id: 0,
                    TriggeringMessageId: null,
                    ToolName: "settings.llm.update",
                    ToolArgsJson: auditArgs,
                    ResultJson: "{\"ok\":true}",
                    ProviderEventId: null,
                    CreatedAt: clock()),
                ct);

            logger.LogInformation("LLM-Settings aktualisiert: {Args}", auditArgs);

            return Results.Ok(new { ok = true });
        });

        return app;
    }

    public sealed record UpdateLlmSettingsPayload(
        string? Provider,
        string? OllamaModel,
        string? GeminiModel,
        string? GeminiApiKey);

    private sealed record LlmSettingsDto(
        string Provider,
        string OllamaModel,
        string GeminiModel,
        bool HasGeminiApiKey);
}
