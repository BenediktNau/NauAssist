using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class WatchJobsEndpointsTests
{
    [Fact]
    public async Task GetWatchJobs_ReturnsOwnJobsOnly()
    {
        // UseSetting statt ConfigureAppConfiguration: Program.cs liest das Enabled-Flag eager
        // beim Service-Aufbau (vor Build) — ConfigureAppConfiguration greift dafür zu spät.
        using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AutonomousAgent:WatchJobs:Enabled", "true");
            builder.UseSetting("AutonomousAgent:WatchJobs:TickSeconds", "3600"); // Scheduler im Test faktisch ruhig
        });

        var client = factory.CreateClient();

        // Job für einen anderen User direkt einsäen ...
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserContextSetter>().Set("user-b");
            await InsertJobAsync(sp.GetRequiredService<WatchJobRepository>(), "B-job");
        }

        // ... und einen für den Default-User (als der die anonyme Anfrage läuft).
        using (var scope = factory.Services.CreateScope())
        {
            await InsertJobAsync(scope.ServiceProvider.GetRequiredService<WatchJobRepository>(), "A-job");
        }

        var response = await client.GetAsync("/api/watch-jobs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jobs = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        jobs.Should().NotBeNull();
        var titles = jobs!.Select(j => j.GetProperty("title").GetString()).ToList();
        titles.Should().Contain("A-job").And.NotContain("B-job");
    }

    [Fact]
    public async Task PauseResumeCancel_ChangeStatus_AndUnknownIdReturns404()
    {
        using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AutonomousAgent:WatchJobs:Enabled", "true");
            builder.UseSetting("AutonomousAgent:WatchJobs:TickSeconds", "3600");
        });
        var client = factory.CreateClient();

        WatchJob job;
        using (var scope = factory.Services.CreateScope())
        {
            job = await InsertJobAsync(scope.ServiceProvider.GetRequiredService<WatchJobRepository>(), "Steuerbar");
        }

        (await client.PostAsync($"/api/watch-jobs/{job.Id}/pause", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetStatusAsync(client, job.Id)).Should().Be("paused");

        (await client.PostAsync($"/api/watch-jobs/{job.Id}/resume", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetStatusAsync(client, job.Id)).Should().Be("active");

        (await client.PostAsync($"/api/watch-jobs/{job.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetStatusAsync(client, job.Id)).Should().Be("completed");

        (await client.PostAsync("/api/watch-jobs/999999/pause", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resume_OnCompletedJob_Returns404()
    {
        using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AutonomousAgent:WatchJobs:Enabled", "true");
            builder.UseSetting("AutonomousAgent:WatchJobs:TickSeconds", "3600");
        });
        var client = factory.CreateClient();

        WatchJob job;
        using (var scope = factory.Services.CreateScope())
        {
            job = await InsertJobAsync(scope.ServiceProvider.GetRequiredService<WatchJobRepository>(), "Abgeschlossen");
        }

        (await client.PostAsync($"/api/watch-jobs/{job.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetStatusAsync(client, job.Id)).Should().Be("completed");

        // completed ist keine gültige Ausgangslage für resume ⇒ 404, kein heimliches Wiederbeleben.
        (await client.PostAsync($"/api/watch-jobs/{job.Id}/resume", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetStatusAsync(client, job.Id)).Should().Be("completed");
    }

    private static async Task<string?> GetStatusAsync(HttpClient client, long id)
    {
        var jobs = await (await client.GetAsync("/api/watch-jobs")).Content.ReadFromJsonAsync<List<JsonElement>>();
        return jobs!.Single(j => j.GetProperty("id").GetInt64() == id).GetProperty("status").GetString();
    }

    private static Task<WatchJob> InsertJobAsync(WatchJobRepository repo, string title)
        => repo.InsertAsync(
            title: title,
            goal: "Ziel",
            kind: WatchJobKind.WebAvailability,
            spec: new WatchJobSpec(new[] { "q" }, Array.Empty<string>(), "frage?", "kriterium"),
            schedule: new WatchJobSchedule(60, 1800),
            notify: new WatchJobNotify(new[] { "webpush" }, FireOnce: true),
            budget: new WatchJobBudget(null, null),
            nextDueAt: DateTimeOffset.UtcNow.AddHours(1), // in der Zukunft ⇒ Scheduler greift nicht ein
            now: DateTimeOffset.UtcNow,
            CancellationToken.None);
}
