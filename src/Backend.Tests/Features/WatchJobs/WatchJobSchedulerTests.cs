using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class WatchJobSchedulerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-25T10:00:00Z");

    private const string NoHitJson =
        """{"met":false,"confidence":0.1,"evidence":[],"summary":"ausverkauft"}""";

    [Fact]
    public async Task RunTickAsync_ChecksOnlyDueJobsAndPersistsResult()
    {
        using var temp = new TempSqliteDb();
        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk(NoHitJson)); // genau ein fälliger Job ⇒ ein Judge-Aufruf
        var provider = BuildProvider(temp.AppDb, llm);

        // Zwei Jobs für den Default-User: einer fällig, einer in der Zukunft.
        WatchJob dueJob, futureJob;
        using (var scope = provider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<WatchJobRepository>();
            dueJob = await InsertJobAsync(repo, "due", Now.AddMinutes(-1));
            futureJob = await InsertJobAsync(repo, "future", Now.AddMinutes(10));
        }

        var scheduler = new WatchJobScheduler(
            provider,
            Options.Create(new WatchJobOptions { MaxConcurrent = 4 }),
            () => Now,
            NullLogger<WatchJobScheduler>.Instance);

        var result = await scheduler.RunTickAsync(CancellationToken.None);

        result.Skipped.Should().BeFalse();
        result.Checked.Should().Be(1);
        result.Fired.Should().Be(0);

        using var verifyScope = provider.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<WatchJobRepository>();
        var jobs = await verifyRepo.ListActiveByUserAsync(CancellationToken.None);

        jobs.Single(j => j.Id == dueJob.Id).CheckCount.Should().Be(1);
        jobs.Single(j => j.Id == futureJob.Id).CheckCount.Should().Be(0);
    }

    private static Task<WatchJob> InsertJobAsync(WatchJobRepository repo, string title, DateTimeOffset nextDueAt)
        => repo.InsertAsync(
            title: title,
            goal: "irgendetwas wieder verfügbar",
            kind: WatchJobKind.WebAvailability,
            spec: new WatchJobSpec(new[] { "suchanfrage" }, Array.Empty<string>(), "verfügbar?", "auf Lager"),
            schedule: new WatchJobSchedule(60, 1800),
            notify: new WatchJobNotify(new[] { "webpush" }, FireOnce: true),
            budget: new WatchJobBudget(null, null),
            nextDueAt: nextDueAt,
            now: Now,
            CancellationToken.None);

    private static ServiceProvider BuildProvider(AppDb db, FakeLlmClient llm)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton(new ClockContext(() => Now, TimeZoneInfo.Utc));
        services.AddSingleton<IOptions<WatchJobOptions>>(Options.Create(new WatchJobOptions
        {
            ConfidenceThreshold = 0.6,
            MinIntervalSeconds = 30,
        }));
        services.AddSingleton<ILlmClient>(llm);
        services.AddSingleton<IWebSearch>(new StubWebSearch());
        services.AddSingleton<IWebFetch>(new StubWebFetch());

        services.AddScoped<UserContextHolder>();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<UserContextHolder>());
        services.AddScoped<IUserContextSetter>(sp => sp.GetRequiredService<UserContextHolder>());
        services.AddScoped<UserRepository>();
        services.AddScoped<WatchJobRepository>();
        services.AddScoped<AuditLogRepository>();
        services.AddScoped<WatchJudge>();
        services.AddScoped<WatchJobExecutor>();

        return services.BuildServiceProvider();
    }

    private sealed class StubWebSearch : IWebSearch
    {
        public Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WebSearchHit>>(new[]
            {
                new WebSearchHit("Treffer", "https://shop.example/x", "Schnipsel"),
            });
    }

    private sealed class StubWebFetch : IWebFetch
    {
        public Task<WebDocument> FetchAsync(string url, string? etag, CancellationToken ct)
            => Task.FromResult(new WebDocument(url, 200, null, "", false));
    }
}
