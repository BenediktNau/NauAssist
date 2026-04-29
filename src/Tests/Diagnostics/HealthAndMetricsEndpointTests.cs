using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Api.Diagnostics;

namespace NauAssist.Tests.Diagnostics;

public class HealthAndMetricsEndpointTests : IClassFixture<NauAssistWebFactory>
{
    private readonly NauAssistWebFactory _factory;

    public HealthAndMetricsEndpointTests(NauAssistWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsJsonWithSubsystems_E0_4()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        // Status kann je nach Ollama-Erreichbarkeit Healthy oder Unhealthy sein —
        // entscheidend ist, dass alle Subsysteme im Report stehen.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"unerwarteter Status: {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"ollama\"", body);
        Assert.Contains("\"disk\"", body);
        Assert.Contains("\"memory-db\"", body);
    }

    [Fact]
    public async Task Metrics_ExposesRegisteredSeries_E0_4()
    {
        // Eine Metrik anstoßen, damit Prometheus-net sie ausliefert.
        NauAssistMetrics.LlmTokensTotal.WithLabels("prompt", "gemma4:e4b").Inc(0);
        NauAssistMetrics.ReflectionCyclesTotal.WithLabels("silent").Inc(0);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/metrics");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("nauassist_llm_tokens_total", body);
        Assert.Contains("nauassist_reflection_cycles_total", body);
    }

    [Fact]
    public async Task Root_EchoesCorrelationIdHeader_E0_4()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "test-12345");

        using var response = await client.SendAsync(request);

        Assert.Equal("test-12345", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }
}

public sealed class NauAssistWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
