namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>Art des Watch-Jobs — wählt die Skill-Pipeline im Executor. Phase 1: nur Web-Verfügbarkeit.</summary>
public enum WatchJobKind
{
    WebAvailability,
}

/// <summary>Lebenszyklus eines Watch-Jobs.</summary>
public enum WatchJobStatus
{
    Active,
    Paused,
    Fired,
    Completed,
    Failed,
    Expired,
}

/// <summary>Was geprüft wird (pro Kind festes, validiertes Schema; das LLM füllt nur erlaubte Felder).</summary>
public sealed record WatchJobSpec(
    IReadOnlyList<string> SearchQueries,
    IReadOnlyList<string> TargetUrls,
    string JudgeQuestion,
    string SuccessCriteria);

/// <summary>Wie oft geprüft wird. Phase 1: einfaches Intervall + Obergrenze fürs Backoff.</summary>
public sealed record WatchJobSchedule(
    int IntervalSeconds,
    int MaxIntervalSeconds);

/// <summary>Über welche Kanäle beim Treffer benachrichtigt wird und ob danach Schluss ist.</summary>
public sealed record WatchJobNotify(
    IReadOnlyList<string> Channels,
    bool FireOnce);

/// <summary>Harte Grenzen gegen Kosten-/Last-Runaway.</summary>
public sealed record WatchJobBudget(
    int? MaxChecks,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Persistente, deklarative Job-Spezifikation — das "selbstgeschriebene Tool" des Assistenten.
/// </summary>
public sealed record WatchJob(
    long Id,
    string Title,
    string Goal,
    WatchJobKind Kind,
    WatchJobSpec Spec,
    WatchJobSchedule Schedule,
    WatchJobNotify Notify,
    WatchJobBudget Budget,
    WatchJobStatus Status,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset NextDueAt,
    int CheckCount,
    int ConsecutiveErrors,
    string? LastResultJson,
    string? FiredHash,
    DateTimeOffset CreatedAt);

internal static class WatchJobKindExtensions
{
    public static string ToWire(this WatchJobKind k) => k switch
    {
        WatchJobKind.WebAvailability => "web_availability",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "Unbekannter WatchJobKind."),
    };

    public static WatchJobKind ParseWire(string s) => s switch
    {
        "web_availability" => WatchJobKind.WebAvailability,
        _ => throw new ArgumentException($"Unbekannter WatchJobKind '{s}'.", nameof(s)),
    };

    public static bool TryParseWire(string s, out WatchJobKind kind)
    {
        switch (s)
        {
            case "web_availability":
                kind = WatchJobKind.WebAvailability;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

internal static class WatchJobStatusExtensions
{
    public static string ToWire(this WatchJobStatus s) => s switch
    {
        WatchJobStatus.Active => "active",
        WatchJobStatus.Paused => "paused",
        WatchJobStatus.Fired => "fired",
        WatchJobStatus.Completed => "completed",
        WatchJobStatus.Failed => "failed",
        WatchJobStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unbekannter WatchJobStatus."),
    };

    public static WatchJobStatus ParseWire(string s) => s switch
    {
        "active" => WatchJobStatus.Active,
        "paused" => WatchJobStatus.Paused,
        "fired" => WatchJobStatus.Fired,
        "completed" => WatchJobStatus.Completed,
        "failed" => WatchJobStatus.Failed,
        "expired" => WatchJobStatus.Expired,
        _ => throw new ArgumentException($"Unbekannter WatchJobStatus '{s}'.", nameof(s)),
    };
}
