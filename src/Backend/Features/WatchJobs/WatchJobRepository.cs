using System.Globalization;
using System.Text.Json;
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Dapper-CRUD auf <c>watch_jobs</c>, user-getrennt über <see cref="IUserContext"/>
/// (Vorbild: <c>SuggestionRepository</c>). JSON-Spalten (Spec/Schedule/Notify/Budget)
/// werden mit denselben CamelCase-<see cref="JsonOpts"/> serialisiert. Zeitstempel werden
/// als UTC im ISO-8601-"O"-Format abgelegt, damit der lexikografische Vergleich in
/// <see cref="ListDueAsync"/> über <c>next_due_at</c> zuverlässig ist.
/// </summary>
public sealed class WatchJobRepository
{
    private const string SelectColumns =
        "id, title, goal, kind, spec_json, schedule_json, notify_json, budget_json, status, " +
        "last_checked_at, next_due_at, check_count, consecutive_errors, last_result_json, fired_hash, created_at";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDb _db;
    private readonly IUserContext _user;

    public WatchJobRepository(AppDb db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    public async Task<WatchJob> InsertAsync(
        string title,
        string goal,
        WatchJobKind kind,
        WatchJobSpec spec,
        WatchJobSchedule schedule,
        WatchJobNotify notify,
        WatchJobBudget budget,
        DateTimeOffset nextDueAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO watch_jobs(
                user_id, title, goal, kind, spec_json, schedule_json, notify_json, budget_json,
                status, next_due_at, check_count, consecutive_errors, created_at)
            VALUES(
                @userId, @title, @goal, @kind, @specJson, @scheduleJson, @notifyJson, @budgetJson,
                'active', @nextDueAt, 0, 0, @createdAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                userId = _user.UserId,
                title,
                goal,
                kind = kind.ToWire(),
                specJson = JsonSerializer.Serialize(spec, JsonOpts),
                scheduleJson = JsonSerializer.Serialize(schedule, JsonOpts),
                notifyJson = JsonSerializer.Serialize(notify, JsonOpts),
                budgetJson = JsonSerializer.Serialize(budget, JsonOpts),
                nextDueAt = Iso(nextDueAt),
                createdAt = Iso(now),
            },
            cancellationToken: ct));

        return new WatchJob(
            Id: id,
            Title: title,
            Goal: goal,
            Kind: kind,
            Spec: spec,
            Schedule: schedule,
            Notify: notify,
            Budget: budget,
            Status: WatchJobStatus.Active,
            LastCheckedAt: null,
            NextDueAt: nextDueAt,
            CheckCount: 0,
            ConsecutiveErrors: 0,
            LastResultJson: null,
            FiredHash: null,
            CreatedAt: now);
    }

    public async Task<IReadOnlyList<WatchJob>> ListActiveByUserAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<WatchJobRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM watch_jobs " +
            "WHERE user_id = @userId AND status IN ('active', 'paused') ORDER BY id DESC;",
            new { userId = _user.UserId },
            cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    /// <summary>Alle Jobs des Users (alle Status), neueste zuerst — Grundlage der Watcher-UI.</summary>
    public async Task<IReadOnlyList<WatchJob>> ListByUserAsync(int limit, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<WatchJobRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM watch_jobs " +
            "WHERE user_id = @userId ORDER BY created_at DESC, id DESC LIMIT @limit;",
            new { userId = _user.UserId, limit },
            cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<WatchJob>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<WatchJobRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM watch_jobs " +
            "WHERE user_id = @userId AND status = 'active' AND next_due_at <= @now " +
            "ORDER BY next_due_at ASC LIMIT @limit;",
            new { userId = _user.UserId, now = Iso(now), limit },
            cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task UpdateAfterCheckAsync(
        long id,
        DateTimeOffset nextDueAt,
        DateTimeOffset lastCheckedAt,
        int checkCount,
        int consecutiveErrors,
        string? lastResultJson,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE watch_jobs
            SET next_due_at = @nextDueAt,
                last_checked_at = @lastCheckedAt,
                check_count = @checkCount,
                consecutive_errors = @consecutiveErrors,
                last_result_json = @lastResultJson
            WHERE id = @id AND user_id = @userId;
            """,
            new
            {
                id,
                userId = _user.UserId,
                nextDueAt = Iso(nextDueAt),
                lastCheckedAt = Iso(lastCheckedAt),
                checkCount,
                consecutiveErrors,
                lastResultJson,
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// Schreibt das Ergebnis eines Scheduler-Checks (Status + optional firedHash + Buchhaltung)
    /// in <b>einer</b> Anweisung — atomar, damit ein Crash nicht halb aktualisierte Jobs hinterlässt.
    /// </summary>
    public async Task ApplyCheckOutcomeAsync(
        long id,
        WatchJobStatus status,
        string? firedHash,
        DateTimeOffset nextDueAt,
        DateTimeOffset lastCheckedAt,
        int checkCount,
        int consecutiveErrors,
        string? lastResultJson,
        CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE watch_jobs
            SET status = @status,
                fired_hash = COALESCE(@firedHash, fired_hash),
                next_due_at = @nextDueAt,
                last_checked_at = @lastCheckedAt,
                check_count = @checkCount,
                consecutive_errors = @consecutiveErrors,
                last_result_json = @lastResultJson
            WHERE id = @id AND user_id = @userId;
            """,
            new
            {
                id,
                userId = _user.UserId,
                status = status.ToWire(),
                firedHash,
                nextDueAt = Iso(nextDueAt),
                lastCheckedAt = Iso(lastCheckedAt),
                checkCount,
                consecutiveErrors,
                lastResultJson,
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// Setzt den Status eines Jobs. Optional auf gültige Ausgangszustände beschränkt
    /// (<paramref name="allowedFrom"/>) — verhindert z.B. dass <c>resume</c> einen bereits
    /// <c>completed</c>/<c>failed</c>/<c>expired</c> Job wiederbelebt und so den
    /// <c>MaxActivePerUser</c>-Deckel umgeht. Ohne <paramref name="allowedFrom"/> (null)
    /// unverändertes Verhalten — u.a. für Testaufbau, der einen Status erzwingt.
    /// </summary>
    public async Task<bool> SetStatusAsync(
        long id,
        WatchJobStatus status,
        string? firedHash,
        CancellationToken ct,
        IReadOnlyCollection<WatchJobStatus>? allowedFrom = null)
    {
        using var conn = _db.OpenConnection();
        var sql = """
            UPDATE watch_jobs
            SET status = @status,
                fired_hash = COALESCE(@firedHash, fired_hash)
            WHERE id = @id AND user_id = @userId
            """
            + (allowedFrom is null ? "" : " AND status IN @allowedStatuses")
            + ";";
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                id,
                userId = _user.UserId,
                status = status.ToWire(),
                firedHash,
                allowedStatuses = allowedFrom?.Select(s => s.ToWire()).ToArray(),
            },
            cancellationToken: ct));
        return affected > 0;
    }

    private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private static WatchJob MapToDomain(WatchJobRow r) => new(
        Id: r.id,
        Title: r.title,
        Goal: r.goal,
        Kind: WatchJobKindExtensions.ParseWire(r.kind),
        Spec: JsonSerializer.Deserialize<WatchJobSpec>(r.spec_json, JsonOpts)!,
        Schedule: JsonSerializer.Deserialize<WatchJobSchedule>(r.schedule_json, JsonOpts)!,
        Notify: JsonSerializer.Deserialize<WatchJobNotify>(r.notify_json, JsonOpts)!,
        Budget: JsonSerializer.Deserialize<WatchJobBudget>(r.budget_json, JsonOpts)!,
        Status: WatchJobStatusExtensions.ParseWire(r.status),
        LastCheckedAt: string.IsNullOrEmpty(r.last_checked_at) ? null : ParseIso(r.last_checked_at),
        NextDueAt: ParseIso(r.next_due_at),
        CheckCount: (int)r.check_count,
        ConsecutiveErrors: (int)r.consecutive_errors,
        LastResultJson: r.last_result_json,
        FiredHash: r.fired_hash,
        CreatedAt: ParseIso(r.created_at));

    private static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record WatchJobRow(
        long id,
        string title,
        string goal,
        string kind,
        string spec_json,
        string schedule_json,
        string notify_json,
        string budget_json,
        string status,
        string? last_checked_at,
        string next_due_at,
        long check_count,
        long consecutive_errors,
        string? last_result_json,
        string? fired_hash,
        string created_at);
}
