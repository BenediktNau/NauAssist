using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.AutonomousAgent;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Infrastructure.Auth;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Eigener, schneller Tick (Default 10 s): wählt fällige Watch-Jobs (<c>next_due_at &lt;= now</c>),
/// führt sie Semaphore-begrenzt parallel aus (jeder Job in eigenem DI-Scope, pro-User-Kontext),
/// persistiert das Ergebnis und auditiert jeden Check/Fire. Vorbild: <c>AutonomousAgentScheduler</c>.
/// Entkoppelt vom Chat-Turn — der Agent bleibt parallel bedienbar.
/// </summary>
public sealed class WatchJobScheduler : BackgroundService
{
    private const int DueJobLimit = 100;

    private readonly IServiceProvider _services;
    private readonly WatchJobOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<WatchJobScheduler> _logger;
    private readonly SemaphoreSlim _concurrency;
    private int _tickInFlight;

    public WatchJobScheduler(
        IServiceProvider services,
        IOptions<WatchJobOptions> options,
        Func<DateTimeOffset> clock,
        ILogger<WatchJobScheduler> logger)
    {
        _services = services;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrent));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WatchJobScheduler ist deaktiviert (WatchJobs.Enabled=false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.TickSeconds)));
        do
        {
            await RunTickAsync(stoppingToken);
        }
        while (await SafeWaitForNextTickAsync(timer, stoppingToken));
    }

    /// <summary>Ein vollständiger Tick über alle User. Public für Tests/manuelles Triggern.</summary>
    public async Task<WatchJobTickResult> RunTickAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _tickInFlight, 1, 0) != 0)
        {
            _logger.LogWarning("WatchJob-Tick übersprungen — vorheriger Lauf noch aktiv.");
            return new WatchJobTickResult(Skipped: true, Checked: 0, Fired: 0, Errors: 0);
        }

        var now = _clock();
        try
        {
            var due = await CollectDueJobsAsync(now, ct);
            if (due.Count == 0)
            {
                return new WatchJobTickResult(Skipped: false, Checked: 0, Fired: 0, Errors: 0);
            }

            var results = await Task.WhenAll(due.Select(item => ProcessJobAsync(item.UserId, item.Job, ct)));

            var fired = results.Count(r => r == JobRunResult.Fired);
            var errors = results.Count(r => r == JobRunResult.Error);
            _logger.LogInformation(
                "WatchJob-Tick fertig: {Checked} geprüft, {Fired} gefeuert, {Errors} Fehler.",
                results.Length, fired, errors);
            return new WatchJobTickResult(Skipped: false, Checked: results.Length, Fired: fired, Errors: errors);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unerwarteter Fehler im WatchJob-Tick.");
            return new WatchJobTickResult(Skipped: false, Checked: 0, Fired: 0, Errors: 1);
        }
        finally
        {
            Interlocked.Exchange(ref _tickInFlight, 0);
        }
    }

    private async Task<IReadOnlyList<DueJob>> CollectDueJobsAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<UserRecord> users;
        using (var userScope = _services.CreateScope())
        {
            users = await userScope.ServiceProvider.GetRequiredService<UserRepository>().ListAsync(ct);
        }

        var due = new List<DueJob>();
        foreach (var user in users)
        {
            try
            {
                using var scope = _services.CreateScope();
                scope.ServiceProvider.GetRequiredService<IUserContextSetter>().Set(user.Id);
                var repo = scope.ServiceProvider.GetRequiredService<WatchJobRepository>();
                var jobs = await repo.ListDueAsync(now, DueJobLimit, ct);
                foreach (var job in jobs)
                {
                    due.Add(new DueJob(user.Id, job));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fällige Jobs für User {UserId} konnten nicht geladen werden.", user.Id);
            }
        }

        return due;
    }

    private async Task<JobRunResult> ProcessJobAsync(string userId, WatchJob job, CancellationToken ct)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserContextSetter>().Set(userId);
            var repo = sp.GetRequiredService<WatchJobRepository>();
            var executor = sp.GetRequiredService<WatchJobExecutor>();
            var audit = sp.GetRequiredService<AuditLogRepository>();

            var outcome = await executor.RunOnceAsync(job, ct);

            await repo.UpdateAfterCheckAsync(
                job.Id,
                outcome.NextDueAt,
                outcome.CheckedAt,
                outcome.CheckCount,
                outcome.ConsecutiveErrors,
                outcome.ResultJson,
                ct);

            if (outcome.Fired || outcome.Status != WatchJobStatus.Active)
            {
                await repo.SetStatusAsync(job.Id, outcome.Status, outcome.FiredHash, ct);
            }

            if (outcome.Fired)
            {
                await OnFiredAsync(sp, job, outcome, ct);
            }

            await AuditAsync(audit, job, outcome, ct);
            return outcome.Fired ? JobRunResult.Fired : JobRunResult.Ok;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WatchJob {Id} (User {UserId}) ist mit einer Exception abgebrochen.", job.Id, userId);
            return JobRunResult.Error;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>Hook für die Benachrichtigung beim Feuern — verdrahtet in Task 6 (WatchJobNotifier).</summary>
    private Task OnFiredAsync(IServiceProvider scopedServices, WatchJob job, ExecutionOutcome outcome, CancellationToken ct)
        => Task.CompletedTask;

    private static async Task AuditAsync(AuditLogRepository audit, WatchJob job, ExecutionOutcome outcome, CancellationToken ct)
    {
        await audit.AppendAsync(new AuditEntry(
            Id: 0,
            TriggeringMessageId: null,
            ToolName: outcome.Fired ? AuditToolNames.WatchJobFired : AuditToolNames.WatchJobCheck,
            ToolArgsJson: JsonSerializer.Serialize(new { jobId = job.Id, title = job.Title }),
            ResultJson: JsonSerializer.Serialize(new
            {
                fired = outcome.Fired,
                status = outcome.Status.ToString().ToLowerInvariant(),
                checkCount = outcome.CheckCount,
                confidence = outcome.JudgeResult?.Confidence,
                summary = outcome.JudgeResult?.Summary,
            }),
            ProviderEventId: null,
            CreatedAt: outcome.CheckedAt), ct);
    }

    private static async Task<bool> SafeWaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed record DueJob(string UserId, WatchJob Job);

    private enum JobRunResult
    {
        Ok,
        Fired,
        Error,
    }
}

public sealed record WatchJobTickResult(bool Skipped, int Checked, int Fired, int Errors);
