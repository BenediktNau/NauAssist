using AwesomeAssertions;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class WatchJobRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-25T10:00:00Z");

    private static WatchJobSpec SampleSpec() => new(
        SearchQueries: new[] { "Midea PortaSplit kaufen lieferbar", "Midea Port-a-Split verfügbar" },
        TargetUrls: new[] { "https://shop.example/midea" },
        JudgeQuestion: "Ist eine Midea-PortaSplit aktuell bestellbar?",
        SuccessCriteria: "Mindestens ein seriöser Shop zeigt 'auf Lager'.");

    private static WatchJobSchedule SampleSchedule() => new(IntervalSeconds: 60, MaxIntervalSeconds: 1800);

    private static WatchJobNotify SampleNotify() => new(Channels: new[] { "webpush" }, FireOnce: true);

    private static WatchJobBudget SampleBudget() => new(MaxChecks: 100, ExpiresAt: DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

    private static Task<WatchJob> InsertSampleAsync(
        WatchJobRepository repo,
        DateTimeOffset nextDueAt,
        string title = "Midea PortaSplit Verfügbarkeit")
        => repo.InsertAsync(
            title: title,
            goal: "Midea-PortaSplit-Klimaanlage wieder bestellbar",
            kind: WatchJobKind.WebAvailability,
            spec: SampleSpec(),
            schedule: SampleSchedule(),
            notify: SampleNotify(),
            budget: SampleBudget(),
            nextDueAt: nextDueAt,
            now: Now,
            CancellationToken.None);

    [Fact]
    public async Task InsertAsync_RoundtripsAllFields()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());

        var inserted = await InsertSampleAsync(repo, nextDueAt: Now);

        inserted.Id.Should().BeGreaterThan(0);

        var listed = await repo.ListActiveByUserAsync(CancellationToken.None);
        listed.Should().HaveCount(1);

        var job = listed[0];
        job.Title.Should().Be("Midea PortaSplit Verfügbarkeit");
        job.Goal.Should().Be("Midea-PortaSplit-Klimaanlage wieder bestellbar");
        job.Kind.Should().Be(WatchJobKind.WebAvailability);
        job.Status.Should().Be(WatchJobStatus.Active);
        job.CheckCount.Should().Be(0);
        job.ConsecutiveErrors.Should().Be(0);
        job.LastCheckedAt.Should().BeNull();
        job.FiredHash.Should().BeNull();
        job.NextDueAt.Should().Be(Now);
        job.CreatedAt.Should().Be(Now);

        job.Spec.SearchQueries.Should().Equal("Midea PortaSplit kaufen lieferbar", "Midea Port-a-Split verfügbar");
        job.Spec.TargetUrls.Should().Equal("https://shop.example/midea");
        job.Spec.JudgeQuestion.Should().Be("Ist eine Midea-PortaSplit aktuell bestellbar?");
        job.Spec.SuccessCriteria.Should().Be("Mindestens ein seriöser Shop zeigt 'auf Lager'.");
        job.Schedule.IntervalSeconds.Should().Be(60);
        job.Schedule.MaxIntervalSeconds.Should().Be(1800);
        job.Notify.Channels.Should().Equal("webpush");
        job.Notify.FireOnce.Should().BeTrue();
        job.Budget.MaxChecks.Should().Be(100);
        job.Budget.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-07-24T00:00:00Z"));
    }

    [Fact]
    public async Task ListDueAsync_ReturnsOnlyActiveJobsDueNow()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());

        var due = await InsertSampleAsync(repo, nextDueAt: Now.AddMinutes(-1), title: "due");
        await InsertSampleAsync(repo, nextDueAt: Now.AddMinutes(10), title: "future");

        var result = await repo.ListDueAsync(Now, limit: 50, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(due.Id);
    }

    [Fact]
    public async Task SetStatusAsync_Completed_RemovesJobFromDueList()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());

        var job = await InsertSampleAsync(repo, nextDueAt: Now.AddMinutes(-1));
        (await repo.ListDueAsync(Now, 50, CancellationToken.None)).Should().HaveCount(1);

        var ok = await repo.SetStatusAsync(job.Id, WatchJobStatus.Completed, firedHash: "abc123", CancellationToken.None);

        ok.Should().BeTrue();
        (await repo.ListDueAsync(Now, 50, CancellationToken.None)).Should().BeEmpty();
        (await repo.ListActiveByUserAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAfterCheckAsync_AdvancesScheduleAndCounters()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());

        var job = await InsertSampleAsync(repo, nextDueAt: Now.AddMinutes(-1));
        var nextDue = Now.AddSeconds(120);

        await repo.UpdateAfterCheckAsync(
            job.Id,
            nextDueAt: nextDue,
            lastCheckedAt: Now,
            checkCount: 1,
            consecutiveErrors: 0,
            lastResultJson: """{"met":false}""",
            CancellationToken.None);

        var updated = (await repo.ListActiveByUserAsync(CancellationToken.None)).Single();
        updated.CheckCount.Should().Be(1);
        updated.NextDueAt.Should().Be(nextDue);
        updated.LastCheckedAt.Should().Be(Now);
        updated.LastResultJson.Should().Be("""{"met":false}""");

        // Mit fortgeschrittenem next_due_at ist der Job jetzt nicht mehr fällig.
        (await repo.ListDueAsync(Now, 50, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyCheckOutcomeAsync_WritesStatusFiredHashAndBookkeepingAtomically()
    {
        using var temp = new TempSqliteDb();
        var repo = new WatchJobRepository(temp.AppDb, new UserContextHolder());

        var job = await InsertSampleAsync(repo, nextDueAt: Now.AddMinutes(-1));

        await repo.ApplyCheckOutcomeAsync(
            job.Id,
            status: WatchJobStatus.Completed,
            firedHash: "HASH123",
            nextDueAt: Now.AddSeconds(60),
            lastCheckedAt: Now,
            checkCount: 1,
            consecutiveErrors: 0,
            lastResultJson: """{"met":true}""",
            CancellationToken.None);

        // completed ⇒ nicht mehr aktiv/fällig; Buchhaltung dennoch geschrieben.
        (await repo.ListActiveByUserAsync(CancellationToken.None)).Should().BeEmpty();
        (await repo.ListDueAsync(Now, 50, CancellationToken.None)).Should().BeEmpty();

        // firedHash bleibt erhalten: erneutes Setzen mit null überschreibt ihn nicht (COALESCE).
        await repo.SetStatusAsync(job.Id, WatchJobStatus.Active, firedHash: null, CancellationToken.None);
        var reactivated = (await repo.ListActiveByUserAsync(CancellationToken.None)).Single();
        reactivated.FiredHash.Should().Be("HASH123");
        reactivated.CheckCount.Should().Be(1);
        reactivated.LastResultJson.Should().Be("""{"met":true}""");
    }

    [Fact]
    public async Task Jobs_AreIsolatedPerUser()
    {
        using var temp = new TempSqliteDb();
        var repoA = new WatchJobRepository(temp.AppDb, new UserContextHolder());
        var userB = new UserContextHolder();
        userB.Set("user-b");
        var repoB = new WatchJobRepository(temp.AppDb, userB);

        await InsertSampleAsync(repoA, nextDueAt: Now.AddMinutes(-1), title: "A-job");

        (await repoB.ListActiveByUserAsync(CancellationToken.None)).Should().BeEmpty();
        (await repoB.ListDueAsync(Now, 50, CancellationToken.None)).Should().BeEmpty();
        (await repoA.ListActiveByUserAsync(CancellationToken.None)).Should().HaveCount(1);
    }
}
