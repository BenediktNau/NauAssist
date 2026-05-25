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

    public Task DeleteEventAsync(string eventId, CancellationToken ct)
    {
        lock (_lock)
        {
            var idx = _events.FindIndex(e => e.Id == eventId);
            if (idx < 0)
            {
                throw new KeyNotFoundException($"Event '{eventId}' nicht gefunden.");
            }
            _events.RemoveAt(idx);
            return Task.CompletedTask;
        }
    }

    public Task UpdateEventAsync(string eventId, EventUpdate update, CancellationToken ct)
    {
        lock (_lock)
        {
            var idx = _events.FindIndex(e => e.Id == eventId);
            if (idx < 0)
            {
                throw new KeyNotFoundException($"Event '{eventId}' nicht gefunden.");
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
            return Task.CompletedTask;
        }
    }
}
