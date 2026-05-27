using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Audit;

namespace NauAssist.Backend.Features.AutonomousAgent;

/// <summary>
/// Periodischer Tick (Default 20 min): ruft alle <see cref="ISourceObserver"/>-Implementierungen
/// auf, räumt abgelaufene <c>pending</c>-Suggestions auf und loggt jeden Tick im audit_log.
///
/// Phase-1-Verhalten: Observer fehlen noch — der Tick ist no-op außer Retention + Audit.
/// Spätere Phasen registrieren Matrix/Gmail-Observer und schicken RawSignals durch
/// CheapFilter → IntentClassifier → Suggestion.
/// </summary>
public sealed class AutonomousAgentScheduler : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly AutonomousAgentOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<AutonomousAgentScheduler> _logger;
    private int _tickInFlight;

    public AutonomousAgentScheduler(
        IServiceProvider services,
        IOptions<AutonomousAgentOptions> options,
        Func<DateTimeOffset> clock,
        ILogger<AutonomousAgentScheduler> logger)
    {
        _services = services;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("AutonomousAgentScheduler ist deaktiviert (Agent.Enabled=false).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.PollIntervalMinutes));
        do
        {
            await RunTickAsync(TickTrigger.Scheduled, stoppingToken);
        }
        while (await SafeWaitForNextTickAsync(timer, stoppingToken));
    }

    public async Task<TickResult> RunTickAsync(TickTrigger trigger, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _tickInFlight, 1, 0) != 0)
        {
            _logger.LogWarning("Tick übersprungen ({Trigger}) — vorheriger Lauf noch aktiv.", trigger);
            return new TickResult(Skipped: true, SignalCount: 0, CreatedCount: 0, UpdatedCount: 0, ExpiredCount: 0, ErrorCount: 0);
        }

        var startedAt = _clock();
        var signalCount = 0;
        var errorCount = 0;
        var expiredCount = 0;
        var createdCount = 0;
        var updatedCount = 0;

        try
        {
            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;
            var observers = sp.GetServices<ISourceObserver>().ToList();
            var audit = sp.GetRequiredService<AuditLogRepository>();
            var suggestions = sp.GetRequiredService<SuggestionRepository>();
            var reasoner = sp.GetRequiredService<AutonomousReasoner>();

            var allSignals = new List<RawSignal>();
            foreach (var observer in observers)
            {
                try
                {
                    var signals = await observer.PollAsync(ct);
                    signalCount += signals.Count;
                    allSignals.AddRange(signals);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogWarning(ex, "Observer {Source} ist mit einer Exception abgebrochen.", observer.Source);
                }
            }

            if (allSignals.Count > 0)
            {
                try
                {
                    var outcome = await reasoner.ProcessAsync(allSignals, ct);
                    createdCount = outcome.Created;
                    updatedCount = outcome.Updated;
                    _logger.LogDebug(
                        "Reasoner: {Classified} klassifiziert, {Created} neu, {Updated} aktualisiert, {Persona} Persona-Updates.",
                        outcome.Classified, outcome.Created, outcome.Updated, outcome.PersonaUpdates);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogWarning(ex, "Reasoner hat eine Exception geworfen — Signale dieses Ticks verloren.");
                }
            }

            var cutoff = startedAt.AddDays(-_options.PendingRetentionDays);
            expiredCount = await suggestions.ExpirePendingAsync(cutoff, startedAt, ct);

            var result = new TickResult(
                Skipped: false,
                SignalCount: signalCount,
                CreatedCount: createdCount,
                UpdatedCount: updatedCount,
                ExpiredCount: expiredCount,
                ErrorCount: errorCount);

            await audit.AppendAsync(new AuditEntry(
                Id: 0,
                TriggeringMessageId: null,
                ToolName: AuditToolNames.AutonomousPoll,
                ToolArgsJson: JsonSerializer.Serialize(new
                {
                    trigger = trigger.ToString().ToLowerInvariant(),
                    observerCount = observers.Count,
                }),
                ResultJson: JsonSerializer.Serialize(result),
                ProviderEventId: null,
                CreatedAt: startedAt), ct);

            _logger.LogInformation(
                "Autonomer Tick fertig ({Trigger}): {Observers} Observer, {Signals} Signale, {Created} neu, {Updated} aktualisiert, {Expired} expired, {Errors} Fehler.",
                trigger, observers.Count, signalCount, createdCount, updatedCount, expiredCount, errorCount);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unerwarteter Fehler im autonomen Tick.");
            return new TickResult(
                Skipped: false,
                SignalCount: signalCount,
                CreatedCount: createdCount,
                UpdatedCount: updatedCount,
                ExpiredCount: expiredCount,
                ErrorCount: errorCount + 1);
        }
        finally
        {
            Interlocked.Exchange(ref _tickInFlight, 0);
        }
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
}

public enum TickTrigger
{
    Scheduled,
    Manual,
}

public sealed record TickResult(
    bool Skipped,
    int SignalCount,
    int CreatedCount,
    int UpdatedCount,
    int ExpiredCount,
    int ErrorCount);

internal static class AuditToolNames
{
    public const string AutonomousPoll = "autonomous_poll";
    public const string AutonomousSuggestionCreated = "autonomous_suggestion_created";
}
