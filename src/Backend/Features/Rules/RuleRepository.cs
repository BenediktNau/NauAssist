using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Rules;

public sealed class RuleRepository
{
    private readonly AppDb _db;

    public RuleRepository(AppDb db)
    {
        _db = db;
    }

    public async Task<Rule> AddAsync(Rule draft, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO rules(text, days_of_week, time_range_start, time_range_end, hardness, created_at)
              VALUES(@Text, @Days, @Start, @End, @Hardness, @CreatedAt);
              SELECT last_insert_rowid();",
            new
            {
                Text = draft.Text,
                Days = (int)draft.DaysOfWeek,
                Start = draft.TimeRangeStart?.ToString("HH:mm"),
                End = draft.TimeRangeEnd?.ToString("HH:mm"),
                Hardness = draft.Hardness.ToString().ToLowerInvariant(),
                CreatedAt = draft.CreatedAt.ToString("O"),
            },
            cancellationToken: ct));

        return draft with { Id = id };
    }

    public async Task<IReadOnlyList<Rule>> ListAllAsync(CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var rows = await conn.QueryAsync<RuleRow>(new CommandDefinition(
            "SELECT id, text, days_of_week, time_range_start, time_range_end, hardness, created_at FROM rules ORDER BY created_at ASC;",
            cancellationToken: ct));

        return rows.Select(MapToDomain).ToList();
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();

        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM rules WHERE id = @Id;",
            new { Id = id },
            cancellationToken: ct));

        return rows > 0;
    }

    private static Rule MapToDomain(RuleRow row) => new(
        Id: row.id,
        Text: row.text,
        DaysOfWeek: (DayOfWeekFlags)(int)row.days_of_week,
        TimeRangeStart: ParseTime(row.time_range_start),
        TimeRangeEnd: ParseTime(row.time_range_end),
        Hardness: Enum.Parse<RuleHardness>(row.hardness, ignoreCase: true),
        CreatedAt: DateTimeOffset.Parse(row.created_at));

    private static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrEmpty(value) ? null : TimeOnly.Parse(value);

    // SQLite gibt INTEGER als Int64 zurück — Dapper braucht die Konstruktor-Signatur exakt passend.
    private sealed record RuleRow(
        long id,
        string text,
        long days_of_week,
        string? time_range_start,
        string? time_range_end,
        string hardness,
        string created_at);
}
