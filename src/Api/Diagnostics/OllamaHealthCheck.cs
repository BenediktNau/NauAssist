using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NauAssist.Common.Configuration;

namespace NauAssist.Api.Diagnostics;

public sealed class OllamaHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<LlmOptions> _options;

    public OllamaHealthCheck(IHttpClientFactory httpClientFactory, IOptionsMonitor<LlmOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProbeTimeout);

            var http = _httpClientFactory.CreateClient(nameof(OllamaHealthCheck));
            http.Timeout = ProbeTimeout;

            var endpoint = new Uri(new Uri(opts.Endpoint), "/api/tags");
            using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy(
                    $"Ollama erreichbar unter {opts.Endpoint}",
                    new Dictionary<string, object> { ["endpoint"] = opts.Endpoint, ["model"] = opts.Model });
            }

            return HealthCheckResult.Unhealthy(
                $"Ollama antwortet mit HTTP {(int)response.StatusCode}",
                data: new Dictionary<string, object> { ["endpoint"] = opts.Endpoint });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or UriFormatException)
        {
            return HealthCheckResult.Unhealthy(
                $"Ollama nicht erreichbar: {ex.GetType().Name}",
                exception: ex,
                data: new Dictionary<string, object> { ["endpoint"] = opts.Endpoint });
        }
    }
}
