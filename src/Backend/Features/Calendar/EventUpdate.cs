namespace NauAssist.Backend.Features.Calendar;

/// <summary>
/// Partial-Update für einen bestehenden Termin. Nicht-null Felder werden gesetzt,
/// null-Felder bleiben unverändert.
/// </summary>
public sealed record EventUpdate(
    string? Title = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    string? Description = null,
    string? Location = null,
    bool? IsAllDay = null)
{
    public bool HasAnyChange =>
        Title is not null ||
        Start is not null ||
        End is not null ||
        Description is not null ||
        Location is not null ||
        IsAllDay is not null;
}
