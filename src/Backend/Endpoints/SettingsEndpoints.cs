using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Settings.GetLlmSettings;
using NauAssist.Backend.Features.Settings.GetOllamaSettings;
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
}
