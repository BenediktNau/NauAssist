using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class WatchJobExecutorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-25T10:00:00Z");

    private const string HitJson =
        """{"met":true,"confidence":0.9,"evidence":[{"shop":"ShopX","price":"599€","url":"https://shop.example/midea","quote":"auf Lager"}],"summary":"Bei ShopX lieferbar für 599€"}""";

    private const string NoHitJson =
        """{"met":false,"confidence":0.2,"evidence":[],"summary":"Überall ausverkauft"}""";

    private const string LowConfidenceJson =
        """{"met":true,"confidence":0.4,"evidence":[{"shop":"ShopX","price":null,"url":"u","quote":"q"}],"summary":"unsicher"}""";

    private static WatchJob SampleJob(
        bool fireOnce = true,
        string? firedHash = null,
        DateTimeOffset? lastCheckedAt = null,
        int checkCount = 0) => new(
        Id: 1,
        Title: "Midea PortaSplit Verfügbarkeit",
        Goal: "Midea-PortaSplit-Klimaanlage wieder bestellbar",
        Kind: WatchJobKind.WebAvailability,
        Spec: new WatchJobSpec(
            SearchQueries: new[] { "Midea PortaSplit lieferbar" },
            TargetUrls: Array.Empty<string>(),
            JudgeQuestion: "Ist eine Midea-PortaSplit aktuell bestellbar?",
            SuccessCriteria: "Mindestens ein seriöser Shop zeigt 'auf Lager'."),
        Schedule: new WatchJobSchedule(IntervalSeconds: 60, MaxIntervalSeconds: 1800),
        Notify: new WatchJobNotify(new[] { "webpush" }, FireOnce: fireOnce),
        Budget: new WatchJobBudget(MaxChecks: null, ExpiresAt: null),
        Status: WatchJobStatus.Active,
        LastCheckedAt: lastCheckedAt,
        NextDueAt: Now.AddMinutes(-1),
        CheckCount: checkCount,
        ConsecutiveErrors: 0,
        LastResultJson: null,
        FiredHash: firedHash,
        CreatedAt: Now.AddHours(-1));

    private static WatchJobExecutor BuildExecutor(FakeLlmClient llm, double confidenceThreshold = 0.6)
    {
        var search = new FakeWebSearch(new WebSearchHit("Midea bei ShopX", "https://shop.example/midea", "auf Lager 599€"));
        var fetch = new FakeWebFetch();
        var judge = new WatchJudge(llm, NullLogger<WatchJudge>.Instance);
        var clock = new ClockContext(() => Now, TimeZoneInfo.Utc);
        var options = Options.Create(new WatchJobOptions
        {
            ConfidenceThreshold = confidenceThreshold,
            MinIntervalSeconds = 30,
        });
        return new WatchJobExecutor(search, fetch, judge, clock, options, NullLogger<WatchJobExecutor>.Instance);
    }

    [Fact]
    public async Task ConfidentHit_FiresAndCompletesFireOnceJob()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk(HitJson));
        var executor = BuildExecutor(llm);

        var outcome = await executor.RunOnceAsync(SampleJob(fireOnce: true), CancellationToken.None);

        outcome.Fired.Should().BeTrue();
        outcome.Status.Should().Be(WatchJobStatus.Completed);
        outcome.FiredHash.Should().NotBeNullOrEmpty();
        outcome.CheckCount.Should().Be(1);
        outcome.JudgeResult!.Met.Should().BeTrue();
    }

    [Fact]
    public async Task NoHit_DoesNotFireAndBacksOff()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk(NoHitJson));
        var executor = BuildExecutor(llm);

        var outcome = await executor.RunOnceAsync(SampleJob(), CancellationToken.None);

        outcome.Fired.Should().BeFalse();
        outcome.Status.Should().Be(WatchJobStatus.Active);
        outcome.CheckCount.Should().Be(1);
        // Erster No-Change: nächste Prüfung ≈ jetzt + Basis-Intervall (60s) zzgl. kleinem Jitter.
        outcome.NextDueAt.Should().BeOnOrAfter(Now.AddSeconds(60));
        outcome.NextDueAt.Should().BeOnOrBefore(Now.AddSeconds(60 + 6 + 1));
    }

    [Fact]
    public async Task HitBelowConfidenceThreshold_DoesNotFire()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk(LowConfidenceJson));
        var executor = BuildExecutor(llm, confidenceThreshold: 0.6);

        var outcome = await executor.RunOnceAsync(SampleJob(), CancellationToken.None);

        outcome.Fired.Should().BeFalse();
        outcome.Status.Should().Be(WatchJobStatus.Active);
    }

    [Fact]
    public async Task SameEvidenceAsLastFire_DoesNotFireAgain()
    {
        // 1. Lauf: feuert (FireOnce=false ⇒ bleibt aktiv), liefert FiredHash.
        var llm1 = new FakeLlmClient();
        llm1.QueueResponse(new TextDeltaChunk(HitJson));
        var firstOutcome = await BuildExecutor(llm1).RunOnceAsync(SampleJob(fireOnce: false), CancellationToken.None);
        firstOutcome.Fired.Should().BeTrue();
        firstOutcome.FiredHash.Should().NotBeNullOrEmpty();

        // 2. Lauf: identische Evidenz, Job kennt den Hash bereits ⇒ kein erneutes Feuern.
        var llm2 = new FakeLlmClient();
        llm2.QueueResponse(new TextDeltaChunk(HitJson));
        var secondOutcome = await BuildExecutor(llm2)
            .RunOnceAsync(SampleJob(fireOnce: false, firedHash: firstOutcome.FiredHash), CancellationToken.None);

        secondOutcome.Fired.Should().BeFalse();
        secondOutcome.Status.Should().Be(WatchJobStatus.Active);
        secondOutcome.FiredHash.Should().Be(firstOutcome.FiredHash);
    }

    private sealed class FakeWebSearch : IWebSearch
    {
        private readonly IReadOnlyList<WebSearchHit> _hits;
        public FakeWebSearch(params WebSearchHit[] hits) => _hits = hits;

        public Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
            => Task.FromResult(_hits);
    }

    private sealed class FakeWebFetch : IWebFetch
    {
        public Task<WebDocument> FetchAsync(string url, string? etag, CancellationToken ct)
            => Task.FromResult(new WebDocument(url, 200, null, "", false));
    }
}
