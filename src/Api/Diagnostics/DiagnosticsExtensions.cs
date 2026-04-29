using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NauAssist.Common.Logging;
using Prometheus;

namespace NauAssist.Api.Diagnostics;

public static class DiagnosticsExtensions
{
    public static IServiceCollection AddNauAssistDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddHttpClient();

        services.AddHealthChecks()
            .AddCheck<OllamaHealthCheck>("ollama", tags: new[] { "ready", "external" })
            .AddCheck<DiskSpaceHealthCheck>("disk", tags: new[] { "ready" })
            .AddCheck<MemoryDatabaseHealthCheck>("memory-db", tags: new[] { "ready" });

        return services;
    }

    public static IEndpointRouteBuilder MapNauAssistDiagnostics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse,
        });

        endpoints.MapMetrics();

        return endpoints;
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => (object)new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    durationMs = e.Value.Duration.TotalMilliseconds,
                    data = e.Value.Data,
                    exception = e.Value.Exception?.Message,
                }),
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, payload, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
    }
}
