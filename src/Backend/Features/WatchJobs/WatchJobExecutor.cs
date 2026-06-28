using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.WatchJobs.Web;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>Ergebnis eines einzelnen Job-Laufs. Der Executor persistiert nichts — das macht der Scheduler.</summary>
public sealed record ExecutionOutcome(
    bool Fired,
    WatchJobStatus Status,
    DateTimeOffset NextDueAt,
    DateTimeOffset CheckedAt,
    int CheckCount,
    int ConsecutiveErrors,
    string? ResultJson,
    string? FiredHash,
    WatchJudgeResult? JudgeResult);

/// <summary>
/// Führt für <b>einen</b> fälligen Job die Pipeline Gather → Judge → Decide aus.
/// Gather ist mehrquellig (Suche + Direkt-Fetch) und tolerant gegen Teil-Fehler;
/// gefeuert wird nur über der Confidence-Schwelle und nie zweimal für dieselbe Evidenz
/// (Idempotenz über <c>firedHash</c>).
/// </summary>
public sealed class WatchJobExecutor
{
    private const int MaxResultsPerQuery = 5;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWebSearch _search;
    private readonly IWebFetch _fetch;
    private readonly WatchJudge _judge;
    private readonly ClockContext _clock;
    private readonly WatchJobOptions _options;
    private readonly ILogger<WatchJobExecutor> _logger;

    public WatchJobExecutor(
        IWebSearch search,
        IWebFetch fetch,
        WatchJudge judge,
        ClockContext clock,
        IOptions<WatchJobOptions> options,
        ILogger<WatchJobExecutor> logger)
    {
        _search = search;
        _fetch = fetch;
        _judge = judge;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExecutionOutcome> RunOnceAsync(WatchJob job, CancellationToken ct)
    {
        var now = _clock.Build().NowUtc;
        var checkCount = job.CheckCount + 1;

        // Budget: TTL bzw. maximale Prüfanzahl erreicht ⇒ Job läuft aus.
        if (job.Budget.ExpiresAt is { } expires && now >= expires)
        {
            return Expired(job, now, checkCount);
        }

        if (job.Budget.MaxChecks is { } maxChecks && checkCount > maxChecks)
        {
            return Expired(job, now, checkCount);
        }

        WatchJudgeResult judgeResult;
        try
        {
            var sources = await GatherAsync(job, ct);
            judgeResult = sources.Count == 0
                ? new WatchJudgeResult(false, 0.0, Array.Empty<JudgeEvidence>(), "Keine Quellen erreichbar.")
                : await _judge.EvaluateAsync(job, sources, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WatchJob {Id} Lauf fehlgeschlagen.", job.Id);
            return Error(job, now, checkCount);
        }

        var resultJson = JsonSerializer.Serialize(judgeResult, JsonOpts);

        var shouldFire = judgeResult.Met && judgeResult.Confidence >= _options.ConfidenceThreshold;
        if (shouldFire)
        {
            var firedHash = ComputeFiredHash(judgeResult.Evidence);

            // Idempotenz: gleiche Evidenz wie beim letzten Feuern ⇒ nicht erneut melden.
            if (firedHash == job.FiredHash)
            {
                return new ExecutionOutcome(
                    Fired: false,
                    Status: WatchJobStatus.Active,
                    NextDueAt: NextDueAt(job, now, backoff: true),
                    CheckedAt: now,
                    CheckCount: checkCount,
                    ConsecutiveErrors: 0,
                    ResultJson: resultJson,
                    FiredHash: job.FiredHash,
                    JudgeResult: judgeResult);
            }

            return new ExecutionOutcome(
                Fired: true,
                Status: job.Notify.FireOnce ? WatchJobStatus.Completed : WatchJobStatus.Active,
                NextDueAt: NextDueAt(job, now, backoff: false),
                CheckedAt: now,
                CheckCount: checkCount,
                ConsecutiveErrors: 0,
                ResultJson: resultJson,
                FiredHash: firedHash,
                JudgeResult: judgeResult);
        }

        // Kein (sicherer) Treffer ⇒ weiterbeobachten mit Backoff.
        return new ExecutionOutcome(
            Fired: false,
            Status: WatchJobStatus.Active,
            NextDueAt: NextDueAt(job, now, backoff: true),
            CheckedAt: now,
            CheckCount: checkCount,
            ConsecutiveErrors: 0,
            ResultJson: resultJson,
            FiredHash: job.FiredHash,
            JudgeResult: judgeResult);
    }

    private async Task<IReadOnlyList<GatheredSource>> GatherAsync(WatchJob job, CancellationToken ct)
    {
        var sources = new List<GatheredSource>();

        foreach (var query in job.Spec.SearchQueries)
        {
            var hits = await _search.SearchAsync(query, MaxResultsPerQuery, ct);
            foreach (var hit in hits)
            {
                sources.Add(new GatheredSource($"search:{query}", hit.Url, hit.Title, hit.Snippet));
            }
        }

        foreach (var url in job.Spec.TargetUrls)
        {
            var doc = await _fetch.FetchAsync(url, etag: null, ct);
            if (doc.NotModified || string.IsNullOrWhiteSpace(doc.TextContent)) continue;
            sources.Add(new GatheredSource("fetch", doc.Url, doc.Url, doc.TextContent));
        }

        return sources;
    }

    /// <summary>
    /// Nächster Fälligkeitszeitpunkt. Bei Backoff wird das zuletzt genutzte Intervall
    /// verdoppelt (gedeckelt durch <c>MaxIntervalSeconds</c>); der erste No-Change nutzt
    /// noch das Basis-Intervall. Ohne Backoff (nach einem Treffer) wird zurückgesetzt.
    /// Ein kleiner Jitter (≤10 %) verhindert Gleichtakt gegen die Zielserver.
    /// </summary>
    private DateTimeOffset NextDueAt(WatchJob job, DateTimeOffset now, bool backoff)
    {
        var floor = Math.Max(1, _options.MinIntervalSeconds);
        var ceil = Math.Max(floor, job.Schedule.MaxIntervalSeconds);
        var baseInterval = Math.Clamp(job.Schedule.IntervalSeconds, floor, ceil);

        int nextInterval;
        if (!backoff)
        {
            nextInterval = baseInterval;
        }
        else
        {
            var prevInterval = job.LastCheckedAt is { } last
                ? (int)Math.Round((job.NextDueAt - last).TotalSeconds)
                : baseInterval;
            prevInterval = Math.Clamp(prevInterval, floor, ceil);
            var grown = job.LastCheckedAt is null ? prevInterval : prevInterval * 2;
            nextInterval = Math.Clamp(grown, floor, ceil);
        }

        var jitter = Random.Shared.Next(0, Math.Max(1, nextInterval / 10) + 1);
        return now.AddSeconds(nextInterval + jitter);
    }

    private static ExecutionOutcome Expired(WatchJob job, DateTimeOffset now, int checkCount) => new(
        Fired: false,
        Status: WatchJobStatus.Expired,
        NextDueAt: now,
        CheckedAt: now,
        CheckCount: checkCount,
        ConsecutiveErrors: job.ConsecutiveErrors,
        ResultJson: job.LastResultJson,
        FiredHash: job.FiredHash,
        JudgeResult: null);

    private ExecutionOutcome Error(WatchJob job, DateTimeOffset now, int checkCount) => new(
        Fired: false,
        Status: WatchJobStatus.Active,
        NextDueAt: NextDueAt(job, now, backoff: true),
        CheckedAt: now,
        CheckCount: checkCount,
        ConsecutiveErrors: job.ConsecutiveErrors + 1,
        ResultJson: job.LastResultJson,
        FiredHash: job.FiredHash,
        JudgeResult: null);

    /// <summary>Stabiler Hash über die normalisierte Evidenz — Basis der Fire-Idempotenz.</summary>
    public static string ComputeFiredHash(IReadOnlyList<JudgeEvidence> evidence)
    {
        var normalized = string.Join("|", evidence
            .Select(e => string.Join(
                "#",
                (e.Shop ?? "").Trim().ToLowerInvariant(),
                (e.Url ?? "").Trim().ToLowerInvariant(),
                (e.Price ?? "").Trim().ToLowerInvariant()))
            .OrderBy(s => s, StringComparer.Ordinal));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}
