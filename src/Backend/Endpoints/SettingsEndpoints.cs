using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.GetCalendarSettings;
using NauAssist.Backend.Features.Settings.GetLlmSettings;
using NauAssist.Backend.Features.Settings.GetOllamaSettings;
using NauAssist.Backend.Features.Settings.UpdateCalendarSettings;
using NauAssist.Backend.Features.Settings.UpdateLlmSettings;
using NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

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
                response.OllamaModel,
                response.SystemPrompt,
                response.DefaultSystemPrompt));
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
                payload.OllamaModel ?? "",
                payload.SystemPrompt);

            var result = await mediator.Send(request, ct);

            if (!result.Ok)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            var auditArgs = JsonSerializer.Serialize(new
            {
                ollamaModel = payload.OllamaModel,
                systemPromptAction = string.IsNullOrWhiteSpace(payload.SystemPrompt) ? "reset" : "set",
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

        app.MapGet("/api/settings/ollama", async (IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new GetOllamaSettingsRequest(), ct);
            return Results.Ok(new OllamaSettingsDto(r.Host, r.HasApiKey, r.NumCtx, r.Temperature));
        });

        app.MapPut("/api/settings/ollama", async (
            UpdateOllamaSettingsPayload payload,
            IMediator mediator,
            AuditLogRepository audit,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            var request = new UpdateOllamaSettingsRequest(
                Host: payload.Host ?? "",
                ApiKey: payload.ApiKey,
                NumCtx: payload.NumCtx,
                Temperature: payload.Temperature);

            var result = await mediator.Send(request, ct);
            if (!result.Ok) return Results.BadRequest(new { error = result.Error });

            var args = JsonSerializer.Serialize(new
            {
                host = payload.Host,
                numCtx = payload.NumCtx,
                temperature = payload.Temperature,
                apiKeyAction = payload.ApiKey switch { null => "unchanged", "" => "cleared", _ => "set" },
            });
            await audit.AppendAsync(
                new AuditEntry(0, null, "settings.ollama.update", args, "{\"ok\":true}", null, clock()),
                ct);

            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/settings/ollama/test", async (
            TestOllamaPayload payload,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            if (!Uri.TryCreate(payload.Host, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return Results.Ok(new TestOllamaResult(false, null, "Host muss absolute http(s)-URL sein."));
            }

            var http = httpFactory.CreateClient("Ollama");
            http.Timeout = TimeSpan.FromSeconds(5);
            var req = new HttpRequestMessage(HttpMethod.Get,
                new Uri(new Uri(payload.Host.TrimEnd('/') + "/"), "api/tags"));
            if (!string.IsNullOrWhiteSpace(payload.ApiKey))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", payload.ApiKey);
            }

            try
            {
                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    return Results.Ok(new TestOllamaResult(false, null, $"HTTP {(int)res.StatusCode}"));
                }

                using var stream = await res.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var models = doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray()
                         .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                         .Where(n => n is not null).Cast<string>().ToArray()
                    : Array.Empty<string>();

                return Results.Ok(new TestOllamaResult(true, models, null));
            }
            catch (Exception ex)
            {
                return Results.Ok(new TestOllamaResult(false, null, ex.Message));
            }
        });

        app.MapGet("/api/settings/calendar", async (IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new GetCalendarSettingsRequest(), ct);
            return Results.Ok(new CalendarSettingsDto(
                r.CalendarId, r.WorkingHoursStart, r.WorkingHoursEnd,
                r.DefaultDurationMinutes, r.SearchHorizonDays,
                r.HasGoogleCredentials, r.IsConnected));
        });

        app.MapPut("/api/settings/calendar", async (
            UpdateCalendarSettingsPayload payload,
            IMediator mediator,
            AuditLogRepository audit,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateCalendarSettingsRequest(
                CalendarId: payload.CalendarId ?? "",
                WorkingHoursStart: payload.WorkingHoursStart ?? "",
                WorkingHoursEnd: payload.WorkingHoursEnd ?? "",
                DefaultDurationMinutes: payload.DefaultDurationMinutes,
                SearchHorizonDays: payload.SearchHorizonDays,
                GoogleClientId: payload.GoogleClientId,
                GoogleClientSecret: payload.GoogleClientSecret), ct);

            if (!result.Ok) return Results.BadRequest(new { error = result.Error });

            var args = JsonSerializer.Serialize(new
            {
                calendarId = payload.CalendarId,
                workingHoursStart = payload.WorkingHoursStart,
                workingHoursEnd = payload.WorkingHoursEnd,
                clientIdAction = payload.GoogleClientId switch
                {
                    null => "unchanged", "" => "cleared", _ => "set",
                },
                clientSecretAction = payload.GoogleClientSecret switch
                {
                    null => "unchanged", "" => "cleared", _ => "set",
                },
            });
            await audit.AppendAsync(
                new AuditEntry(0, null, "settings.calendar.update", args, "{\"ok\":true}", null, clock()),
                ct);

            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/settings/persona", async (
            IAppSettingsRepository repo,
            CancellationToken ct) =>
        {
            var text = await repo.GetUserPersonaAsync(ct);
            return Results.Ok(new PersonaDto(text, AppSettingsRepository.UserPersonaMaxLength));
        });

        app.MapDelete("/api/settings/persona", async (
            IAppSettingsRepository repo,
            AuditLogRepository audit,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            await repo.SetUserPersonaAsync(string.Empty, ct);
            await audit.AppendAsync(
                new AuditEntry(0, null, "settings.persona.reset", "{}", "{\"ok\":true}", null, clock()),
                ct);
            return Results.NoContent();
        });

        return app;
    }

    private sealed record PersonaDto(string Text, int MaxLength);

    public sealed record UpdateLlmSettingsPayload(string? OllamaModel, string? SystemPrompt);

    private sealed record LlmSettingsDto(
        string OllamaModel,
        string? SystemPrompt,
        string DefaultSystemPrompt);

    public sealed record UpdateOllamaSettingsPayload(
        string? Host,
        string? ApiKey,
        int NumCtx,
        double Temperature);

    private sealed record OllamaSettingsDto(
        string Host,
        bool HasApiKey,
        int NumCtx,
        double Temperature);

    public sealed record TestOllamaPayload(string Host, string? ApiKey);
    private sealed record TestOllamaResult(bool Ok, string[]? Models, string? Error);

    public sealed record UpdateCalendarSettingsPayload(
        string? CalendarId,
        string? WorkingHoursStart,
        string? WorkingHoursEnd,
        int DefaultDurationMinutes,
        int SearchHorizonDays,
        string? GoogleClientId,
        string? GoogleClientSecret);

    private sealed record CalendarSettingsDto(
        string CalendarId,
        string WorkingHoursStart,
        string WorkingHoursEnd,
        int DefaultDurationMinutes,
        int SearchHorizonDays,
        bool HasGoogleCredentials,
        bool IsConnected);
}
