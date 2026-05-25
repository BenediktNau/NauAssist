using NauAssist.Backend.Features.Calendar;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// In-Memory-Provider für Tests. Thread-safe für simple Konkurrenz (lock).
/// </summary>
public sealed class FakeCalendarProvider : ICalendarProvider
{
    private readonly List<CalendarEvent> _events = new();
    private readonly List<NewEvent> _created = new();
    private int _idCounter = 0;
    private readonly object _lock = new();

    public IReadOnlyList<NewEvent> CreatedEvents
    {
        get { lock (_lock) { return _created.ToList(); } }
    }

    public void Seed(params CalendarEvent[] events)
    {
        lock (_lock)
        {
            _events.AddRange(events);
        }
    }

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        lock (_lock)
        {
            var hits = _events
                .Where(e => e.Start < to && e.End > from)
                .OrderBy(e => e.Start)
                .ToList();
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(hits);
        }
    }

    public Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct)
    {
        lock (_lock)
        {
            _idCounter++;
            var id = $"fake-{_idCounter}";
            _events.Add(new CalendarEvent(id, ev.Title, ev.Start, ev.End, ev.Description, ev.Location, ev.IsAllDay));
            _created.Add(ev);
            return Task.FromResult(id);
        }
    }

    public IReadOnlyList<(string EventId, EventScope Scope)> Deletions
    {
        get { lock (_lock) { return _deletions.ToList(); } }
    }

    public IReadOnlyList<(string EventId, EventUpdate Update, EventScope Scope)> Updates
    {
        get { lock (_lock) { return _updates.ToList(); } }
    }

    private readonly List<(string EventId, EventScope Scope)> _deletions = new();
    private readonly List<(string EventId, EventUpdate Update, EventScope Scope)> _updates = new();

    public Task DeleteEventAsync(string eventId, EventScope scope, CancellationToken ct)
    {
        lock (_lock)
        {
            var targetId = ResolveTargetId(eventId, scope);
            // Bei Series-Scope alle Events mit derselben SeriesId entfernen (+ Master, falls vorhanden).
            if (scope == EventScope.Series)
            {
                _events.RemoveAll(e => e.Id == targetId || e.SeriesId == targetId);
            }
            else
            {
                var idx = _events.FindIndex(e => e.Id == eventId);
                if (idx < 0)
                {
                    throw new KeyNotFoundException($"Event '{eventId}' nicht gefunden.");
                }
                _events.RemoveAt(idx);
            }
            _deletions.Add((eventId, scope));
            return Task.CompletedTask;
        }
    }

    public Task UpdateEventAsync(string eventId, EventUpdate update, EventScope scope, CancellationToken ct)
    {
        lock (_lock)
        {
            var targetId = ResolveTargetId(eventId, scope);
            var idx = _events.FindIndex(e => e.Id == targetId);
            if (idx < 0)
            {
                // Bei Series ohne separates Master-Event reicht die Instanz als Ziel.
                idx = _events.FindIndex(e => e.Id == eventId);
                if (idx < 0)
                {
                    throw new KeyNotFoundException($"Event '{eventId}' nicht gefunden.");
                }
            }
            var cur = _events[idx];
            _events[idx] = cur with
            {
                Title = update.Title ?? cur.Title,
                Start = update.Start ?? cur.Start,
                End = update.End ?? cur.End,
                Description = update.Description ?? cur.Description,
                Location = update.Location ?? cur.Location,
                IsAllDay = update.IsAllDay ?? cur.IsAllDay,
            };
            _updates.Add((eventId, update, scope));
            return Task.CompletedTask;
        }
    }

    private string ResolveTargetId(string eventId, EventScope scope)
    {
        if (scope != EventScope.Series) return eventId;
        var instance = _events.FirstOrDefault(e => e.Id == eventId);
        return instance?.SeriesId ?? eventId;
    }
}
