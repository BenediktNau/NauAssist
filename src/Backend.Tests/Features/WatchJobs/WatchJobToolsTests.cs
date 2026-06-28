using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Features.WatchJobs.Tools;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class WatchJobToolsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-25T10:00:00Z");

    private static ClockContext Clock => new(() => Now, TimeZoneInfo.Utc);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private const string ValidCreateArgs = """
        {
          "title": "Midea PortaSplit",
          "goal": "Midea PortaSplit wieder bestellbar",
          "spec": {
            "searchQueries": ["Midea PortaSplit lieferbar"],
            "judgeQuestion": "Ist sie bestellbar?",
            "successCriteria": "auf Lager"
          },
          "schedule": { "intervalSeconds": 5 }
        }
        """;

    [Fact]
    public async Task Create_ClampsIntervalToMinimum_AndPersistsActiveJob()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var options = Options.Create(new WatchJobOptions { MinIntervalSeconds = 30, MaxActivePerUser = 10 });
        var tool = new CreateWatchJobTool(repo, Clock, options);

        var result = await tool.ExecuteAsync(Args(ValidCreateArgs), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("interval_seconds").GetInt32().Should().Be(30); // 5 → auf Min angehoben

        var jobs = await repo.ListActiveByUserAsync(CancellationToken.None);
        jobs.Should().HaveCount(1);
        jobs[0].Schedule.IntervalSeconds.Should().Be(30);
        jobs[0].Status.Should().Be(WatchJobStatus.Active);
    }

    [Fact]
    public async Task Create_WithoutSearchQueryOrUrl_ReturnsError()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var tool = new CreateWatchJobTool(repo, Clock, Options.Create(new WatchJobOptions()));

        var result = await tool.ExecuteAsync(Args("""
            { "title": "X", "goal": "Y", "spec": { "judgeQuestion": "q", "successCriteria": "c" } }
            """), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        (await repo.ListActiveByUserAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_RejectsNonHttpTargetUrl()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var tool = new CreateWatchJobTool(repo, Clock, Options.Create(new WatchJobOptions { MinIntervalSeconds = 30, MaxActivePerUser = 10 }));

        var result = await tool.ExecuteAsync(Args("""
            {
              "title": "X", "goal": "Y",
              "spec": {
                "targetUrls": ["file:///etc/passwd"],
                "judgeQuestion": "q", "successCriteria": "c"
              }
            }
            """), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        (await repo.ListActiveByUserAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_RejectsUnknownMode_WithoutChangingJob()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var tool = new CancelWatchJobTool(repo);
        var job = await CreateSampleAsync(repo);

        var result = await tool.ExecuteAsync(Args($$"""{ "id": {{job.Id}}, "mode": "pasue" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        var active = await repo.ListActiveByUserAsync(CancellationToken.None);
        active.Single().Status.Should().Be(WatchJobStatus.Active); // unverändert, nicht still completed
    }

    [Fact]
    public async Task Create_RejectsWhenMaxActivePerUserReached()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var options = Options.Create(new WatchJobOptions { MinIntervalSeconds = 30, MaxActivePerUser = 1 });
        var tool = new CreateWatchJobTool(repo, Clock, options);

        var first = await tool.ExecuteAsync(Args(ValidCreateArgs), CancellationToken.None);
        first.GetProperty("ok").GetBoolean().Should().BeTrue();

        var second = await tool.ExecuteAsync(Args(ValidCreateArgs), CancellationToken.None);
        second.GetProperty("ok").GetBoolean().Should().BeFalse();
        (await repo.ListActiveByUserAsync(CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task List_ReturnsActiveJobsWithLastSummary()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var job = await CreateSampleAsync(repo);
        await repo.UpdateAfterCheckAsync(job.Id, Now.AddMinutes(1), Now, 1, 0, """{"summary":"noch nichts"}""", CancellationToken.None);

        var tool = new ListWatchJobsTool(repo);
        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        var jobs = result.GetProperty("jobs");
        jobs.GetArrayLength().Should().Be(1);
        jobs[0].GetProperty("check_count").GetInt32().Should().Be(1);
        jobs[0].GetProperty("last_summary").GetString().Should().Be("noch nichts");
    }

    [Fact]
    public async Task Cancel_StopsJob_Pause_PausesJob_UnknownId_Errors()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var tool = new CancelWatchJobTool(repo);

        var toCancel = await CreateSampleAsync(repo);
        var toPause = await CreateSampleAsync(repo);

        var cancelResult = await tool.ExecuteAsync(Args($$"""{ "id": {{toCancel.Id}}, "mode": "cancel" }"""), CancellationToken.None);
        cancelResult.GetProperty("ok").GetBoolean().Should().BeTrue();
        cancelResult.GetProperty("status").GetString().Should().Be("completed");

        var pauseResult = await tool.ExecuteAsync(Args($$"""{ "id": {{toPause.Id}}, "mode": "pause" }"""), CancellationToken.None);
        pauseResult.GetProperty("status").GetString().Should().Be("paused");

        var unknown = await tool.ExecuteAsync(Args("""{ "id": 99999, "mode": "cancel" }"""), CancellationToken.None);
        unknown.GetProperty("ok").GetBoolean().Should().BeFalse();

        // Cancelled (completed) verschwindet aus der Liste, pausierter bleibt sichtbar.
        var active = await repo.ListActiveByUserAsync(CancellationToken.None);
        active.Select(j => j.Id).Should().Contain(toPause.Id).And.NotContain(toCancel.Id);
    }

    private static Task<WatchJob> CreateSampleAsync(WatchJobRepository repo)
        => repo.InsertAsync(
            "Sample", "Ziel",
            WatchJobKind.WebAvailability,
            new WatchJobSpec(new[] { "q" }, Array.Empty<string>(), "frage?", "kriterium"),
            new WatchJobSchedule(60, 1800),
            new WatchJobNotify(new[] { "webpush" }, true),
            new WatchJobBudget(null, null),
            Now, Now, CancellationToken.None);
}
