# Plan B — Kalender-Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Komplette Kalender-Schicht: Provider-Abstraktion, deterministische Freie-Slot-Berechnung, drei Mediator-Handler (`LookupFreeSlots`, `CreateEvent`, `GetCalendarRange`), Google-Calendar-Implementierung mit OAuth-Flow und SQLite-basiertem Token-Storage.

**Architecture:** `ICalendarProvider` als zentrale Abstraktion mit zwei Implementierungen — `FakeCalendarProvider` (Test-Double) und `GoogleCalendarProvider` (Produktion). `FreeSlotCalculator` ist pure Logik ohne Abhängigkeiten. Handler kombinieren Provider + `RuleApplicator` (aus Plan A) und liefern annotierte Slot-Kandidaten. OAuth-Tokens werden über eine eigene `SqliteDataStore`-Implementierung in der App-DB persistiert. Auth-Flow läuft als Sub-Command (`dotnet run -- auth`).

**Tech Stack:** .NET 10 · `Google.Apis.Calendar.v3` · `Google.Apis.Auth` · vorhandene Mediator/SQLite/Dapper-Infrastruktur aus Plan A

**Bezug zur Spec:** `docs/superpowers/specs/2026-05-19-kalender-agent-mvp-design.md`, Abschnitte 6.4 (Calendar Provider), 6.6 (Persistence), 8 (Fehlerbehandlung), 10 (Defaults).

**Was am Ende dieses Plans steht:**
- `ICalendarProvider`-Interface mit Fake- und Google-Implementierung
- `FreeSlotCalculator` mit ausführlicher Test-Suite
- Drei Handler: `LookupFreeSlots`, `CreateEvent`, `GetCalendarRange`
- OAuth-Flow + Token-Storage in SQLite
- `dotnet run --project src/Backend -- auth`-Sub-Command
- Migration `0002_google_oauth.sql` läuft idempotent
- Alle Tests grün (gegen `FakeCalendarProvider`)

**Voraussetzung außerhalb des Codes:**
Vor Task 9 muss der User einmalig:
1. Google-Cloud-Projekt anlegen
2. Calendar-API aktivieren
3. OAuth 2.0 Client ID (Type: Desktop) erstellen
4. `client_secret.json` herunterladen und nach `./data/google-credentials.json` legen

Diese Schritte sind im Plan dokumentiert (Task 9 Step 0), aber nicht automatisierbar.

---

## Datei-Übersicht (für diesen Plan)

**Neu anzulegen:**

| Pfad | Verantwortung |
|---|---|
| `src/Backend/Features/Calendar/CalendarEvent.cs` | Domain-Modell: gelesener Event |
| `src/Backend/Features/Calendar/NewEvent.cs` | Domain-Modell: anzulegender Event |
| `src/Backend/Features/Calendar/ICalendarProvider.cs` | Provider-Abstraktion |
| `src/Backend/Features/Calendar/CalendarOptions.cs` | Konfigurierbare Defaults (Arbeitszeiten, Dauer, Horizont) |
| `src/Backend/Features/Calendar/FreeSlotCalculator.cs` | Pure Logik für Lücken-Berechnung |
| `src/Backend/Features/Calendar/LookupFreeSlots/LookupFreeSlotsRequest.cs` | Mediator-Request |
| `src/Backend/Features/Calendar/LookupFreeSlots/LookupFreeSlotsHandler.cs` | Handler |
| `src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs` | Mediator-Request |
| `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs` | Handler |
| `src/Backend/Features/Calendar/GetCalendarRange/GetCalendarRangeRequest.cs` | Mediator-Request |
| `src/Backend/Features/Calendar/GetCalendarRange/GetCalendarRangeHandler.cs` | Handler |
| `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs` | Google-Implementierung von `ICalendarProvider` |
| `src/Backend/Features/Calendar/Google/GoogleAuthService.cs` | OAuth-Flow + Credential-Verwaltung |
| `src/Backend/Features/Calendar/Google/SqliteDataStore.cs` | `IDataStore` für Google.Apis, persistiert in SQLite |
| `src/Backend/Features/Calendar/Google/GoogleAuthCommand.cs` | CLI-Logik für `dotnet run -- auth` |
| `src/Backend/Features/Infrastructure/Persistence/Migrations/0002_google_oauth.sql` | Token-Tabelle |
| `src/Backend.Tests/Helpers/FakeCalendarProvider.cs` | In-Memory-Provider für Tests |
| `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs` | Sanity-Tests für Modelle |
| `src/Backend.Tests/Features/Calendar/FakeCalendarProviderTests.cs` | Tests des Fakes selbst |
| `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs` | Umfangreiche Pure-Logic-Tests |
| `src/Backend.Tests/Features/Calendar/LookupFreeSlotsHandlerTests.cs` | Handler-Tests |
| `src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs` | Handler-Tests |
| `src/Backend.Tests/Features/Calendar/GetCalendarRangeHandlerTests.cs` | Handler-Tests |
| `src/Backend.Tests/Features/Calendar/SqliteDataStoreTests.cs` | Persistenz-Tests für OAuth-Store |

**Zu modifizieren:**

| Pfad | Änderung |
|---|---|
| `src/Backend/Backend.csproj` | NuGets: `Google.Apis.Calendar.v3`, `Google.Apis.Auth` |
| `src/Backend/Program.cs` | DI-Registrierungen für Provider, CalendarOptions, Auth-Service; Sub-Command-Erkennung |
| `src/Backend/appsettings.json` | `Calendar`-Sektion mit Defaults |

---

## Task 1: Domain-Modelle (CalendarEvent + NewEvent + CalendarOptions)

**Files:**
- Create: `src/Backend/Features/Calendar/CalendarEvent.cs`
- Create: `src/Backend/Features/Calendar/NewEvent.cs`
- Create: `src/Backend/Features/Calendar/CalendarOptions.cs`
- Create: `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Calendar;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CalendarModelTests
{
    [Fact]
    public void CalendarEvent_CanBeConstructed()
    {
        var ev = new CalendarEvent(
            Id: "google-event-123",
            Title: "Sprint Planning",
            Start: DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-25T11:00:00+02:00"),
            Description: "Quartalsplanung",
            Location: "MS Teams");

        ev.Id.Should().Be("google-event-123");
        ev.End.Should().BeAfter(ev.Start);
    }

    [Fact]
    public void NewEvent_CanBeConstructed_WithMinimalFields()
    {
        var ev = new NewEvent(
            Title: "Pierre",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null);

        ev.Title.Should().Be("Pierre");
        ev.Description.Should().BeNull();
    }

    [Fact]
    public void CalendarOptions_HasReasonableDefaults()
    {
        var opts = new CalendarOptions();

        opts.WorkingHoursStart.Should().Be("09:00");
        opts.WorkingHoursEnd.Should().Be("18:00");
        opts.DefaultDurationMinutes.Should().Be(60);
        opts.SearchHorizonDays.Should().Be(14);
    }
}
```

- [ ] **Step 2: Test laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~CalendarModelTests"
```

Expected: Compile-Fehler — Modelle existieren nicht.

- [ ] **Step 3: CalendarEvent.cs schreiben**

Datei `src/Backend/Features/Calendar/CalendarEvent.cs`:

```csharp
namespace NauAssist.Backend.Features.Calendar;

public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location);
```

- [ ] **Step 4: NewEvent.cs schreiben**

Datei `src/Backend/Features/Calendar/NewEvent.cs`:

```csharp
namespace NauAssist.Backend.Features.Calendar;

public sealed record NewEvent(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location);
```

- [ ] **Step 5: CalendarOptions.cs schreiben**

Datei `src/Backend/Features/Calendar/CalendarOptions.cs`:

```csharp
namespace NauAssist.Backend.Features.Calendar;

public sealed class CalendarOptions
{
    /// <summary>Arbeitszeit-Beginn als "HH:mm" in Lokalzeit.</summary>
    public string WorkingHoursStart { get; set; } = "09:00";

    /// <summary>Arbeitszeit-Ende als "HH:mm" in Lokalzeit.</summary>
    public string WorkingHoursEnd { get; set; } = "18:00";

    /// <summary>Default-Termin-Dauer, wenn nicht aus der Anfrage ableitbar.</summary>
    public int DefaultDurationMinutes { get; set; } = 60;

    /// <summary>Such-Horizont in Tagen, wenn der Agent keine explizite Range nennt.</summary>
    public int SearchHorizonDays { get; set; } = 14;

    /// <summary>Welcher Google-Calendar genutzt wird. "primary" = Standard-Kalender des Users.</summary>
    public string GoogleCalendarId { get; set; } = "primary";

    /// <summary>Pfad zur Google-OAuth-Client-Secret-Datei (vom User aus Google Console heruntergeladen).</summary>
    public string GoogleCredentialsPath { get; set; } = "./data/google-credentials.json";
}
```

- [ ] **Step 6: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~CalendarModelTests"
```

Expected: 3 Tests grün.

- [ ] **Step 7: Commit**

```bash
git add src/
git commit -m "Plan B Task 1: Calendar-Domain-Modelle (CalendarEvent, NewEvent, CalendarOptions)"
```

---

## Task 2: ICalendarProvider + FakeCalendarProvider

**Files:**
- Create: `src/Backend/Features/Calendar/ICalendarProvider.cs`
- Create: `src/Backend.Tests/Helpers/FakeCalendarProvider.cs`
- Create: `src/Backend.Tests/Features/Calendar/FakeCalendarProviderTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Calendar/FakeCalendarProviderTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class FakeCalendarProviderTests
{
    [Fact]
    public async Task GetEvents_ReturnsSeededEventsInRange()
    {
        var provider = new FakeCalendarProvider();
        var inRange = new CalendarEvent("e1", "A",
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-25T11:00:00+02:00"),
            null, null);
        var outOfRange = new CalendarEvent("e2", "B",
            DateTimeOffset.Parse("2026-06-25T10:00:00+02:00"),
            DateTimeOffset.Parse("2026-06-25T11:00:00+02:00"),
            null, null);
        provider.Seed(inRange, outOfRange);

        var events = await provider.GetEventsAsync(
            DateTimeOffset.Parse("2026-05-25T00:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-26T00:00:00+02:00"),
            CancellationToken.None);

        events.Should().ContainSingle(e => e.Id == "e1");
    }

    [Fact]
    public async Task CreateEvent_AppendsEventAndAssignsId()
    {
        var provider = new FakeCalendarProvider();

        var id = await provider.CreateEventAsync(new NewEvent(
            "Pierre",
            DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            null, null), CancellationToken.None);

        id.Should().NotBeNullOrEmpty();
        var events = await provider.GetEventsAsync(
            DateTimeOffset.Parse("2026-05-27T00:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-28T00:00:00+02:00"),
            CancellationToken.None);
        events.Should().ContainSingle(e => e.Id == id && e.Title == "Pierre");
    }

    [Fact]
    public async Task GetEvents_RangeOverlapMatching_IncludesPartialOverlap()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent("e1", "A",
            DateTimeOffset.Parse("2026-05-25T17:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-25T19:00:00+02:00"),
            null, null));

        // Range 18-20 — Event überschneidet im hinteren Teil
        var events = await provider.GetEventsAsync(
            DateTimeOffset.Parse("2026-05-25T18:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-25T20:00:00+02:00"),
            CancellationToken.None);

        events.Should().ContainSingle(e => e.Id == "e1");
    }
}
```

- [ ] **Step 2: ICalendarProvider schreiben**

Datei `src/Backend/Features/Calendar/ICalendarProvider.cs`:

```csharp
namespace NauAssist.Backend.Features.Calendar;

public interface ICalendarProvider
{
    /// <summary>
    /// Liefert alle Events, die mit [from, to) überschneiden.
    /// Sortierung: aufsteigend nach Start.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);

    /// <summary>
    /// Legt einen neuen Termin an. Gibt die vom Provider vergebene Event-ID zurück.
    /// </summary>
    Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct);
}
```

- [ ] **Step 3: FakeCalendarProvider schreiben**

Datei `src/Backend.Tests/Helpers/FakeCalendarProvider.cs`:

```csharp
using NauAssist.Backend.Features.Calendar;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// In-Memory-Provider für Tests. Thread-safe für simple Konkurrenz (lock).
/// </summary>
public sealed class FakeCalendarProvider : ICalendarProvider
{
    private readonly List<CalendarEvent> _events = new();
    private int _idCounter = 0;
    private readonly object _lock = new();

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
            // Überschneidung [from, to) mit [Start, End)
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
            _events.Add(new CalendarEvent(id, ev.Title, ev.Start, ev.End, ev.Description, ev.Location));
            return Task.FromResult(id);
        }
    }
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~FakeCalendarProviderTests"
```

Expected: 3 Tests grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan B Task 2: ICalendarProvider + FakeCalendarProvider"
```

---

## Task 3: FreeSlotCalculator

**Files:**
- Create: `src/Backend/Features/Calendar/FreeSlotCalculator.cs`
- Create: `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs`

**Logik:** Bekommt einen Range `[from, to)` und eine Liste vorhandener Events. Berechnet pro Tag in Lokalzeit die freien Lücken innerhalb der Arbeitszeit, in denen mindestens `duration` Platz ist. Wochenenden (nicht in `WorkingDays`) bleiben leer. Gibt eine Liste `SlotCandidate` zurück (siehe Plan A `Features/Rules/SlotAnnotation.cs`).

- [ ] **Step 1: Test-Suite schreiben**

Datei `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class FreeSlotCalculatorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static FreeSlotCalculator Calc() => new(
        Berlin,
        new TimeOnly(9, 0),
        new TimeOnly(18, 0),
        DayOfWeekFlags.WeekdaysOnly);

    [Fact]
    public void Calculate_OneEmptyWeekday_FullDayMinusLunchIfNoEvents()
    {
        // Mi 27.05.2026, keine Events, 60min Dauer
        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: Array.Empty<CalendarEvent>(),
            durationMinutes: 60);

        // Erwartung: 9 Slots (9-10, 10-11, ..., 17-18 — alle 1-Stunden-Slots)
        slots.Should().NotBeEmpty();
        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 9, 0));
        slots.Last().End.Should().BeOnOrBefore(BerlinTime(2026, 5, 27, 18, 0));
    }

    [Fact]
    public void Calculate_EventInMiddle_SplitsAroundIt()
    {
        var ev = new CalendarEvent("e1", "Mittagstermin",
            BerlinTime(2026, 5, 27, 12, 0),
            BerlinTime(2026, 5, 27, 13, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        // Es muss vor und nach dem Event Slots geben, aber nicht durch 12-13
        slots.Should().Contain(s => s.End <= BerlinTime(2026, 5, 27, 12, 0));
        slots.Should().Contain(s => s.Start >= BerlinTime(2026, 5, 27, 13, 0));
        slots.Should().NotContain(s => s.Start < BerlinTime(2026, 5, 27, 13, 0) && s.End > BerlinTime(2026, 5, 27, 12, 0));
    }

    [Fact]
    public void Calculate_DurationLargerThanGap_GapDoesNotAppear()
    {
        // Event 10-11 und 12-13, dazwischen nur 1h Lücke. Bei 90min Dauer → diese Lücke verschwindet.
        var events = new[]
        {
            new CalendarEvent("e1", "A", BerlinTime(2026, 5, 27, 10, 0), BerlinTime(2026, 5, 27, 11, 0), null, null),
            new CalendarEvent("e2", "B", BerlinTime(2026, 5, 27, 12, 0), BerlinTime(2026, 5, 27, 13, 0), null, null),
        };

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: events,
            durationMinutes: 90);

        slots.Should().NotContain(s => s.Start >= BerlinTime(2026, 5, 27, 11, 0) && s.End <= BerlinTime(2026, 5, 27, 12, 0));
    }

    [Fact]
    public void Calculate_OutsideWorkingHours_IsIgnored()
    {
        // Event 6:00-8:00 fällt komplett vor Arbeitszeit → ändert nichts
        var ev = new CalendarEvent("e1", "Frueh",
            BerlinTime(2026, 5, 27, 6, 0),
            BerlinTime(2026, 5, 27, 8, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 9, 0));
    }

    [Fact]
    public void Calculate_SaturdayAndSunday_AreSkipped()
    {
        // Sa 30.05 + So 31.05, keine Events
        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 30, 0, 0),
            to: BerlinTime(2026, 6, 1, 0, 0),
            events: Array.Empty<CalendarEvent>(),
            durationMinutes: 60);

        slots.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_MultipleDays_SpansAcrossAllWeekdays()
    {
        // Mo 25.05 bis Fr 29.05 (5 Tage), keine Events
        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 25, 0, 0),
            to: BerlinTime(2026, 5, 30, 0, 0),
            events: Array.Empty<CalendarEvent>(),
            durationMinutes: 60);

        var distinctDays = slots.Select(s => s.Start.LocalDateTime.Date).Distinct().Count();
        distinctDays.Should().Be(5);
    }

    [Fact]
    public void Calculate_EventPartiallyOverlappingMorning_ShrinksMorningWindow()
    {
        // Event 8:30-10:00 ragt in Arbeitszeit (ab 9:00) — nach 10:00 geht's los
        var ev = new CalendarEvent("e1", "Frueh-rein",
            BerlinTime(2026, 5, 27, 8, 30),
            BerlinTime(2026, 5, 27, 10, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 10, 0));
    }

    [Fact]
    public void Calculate_FullyBookedDay_ReturnsNoSlots()
    {
        // Event füllt 9-18 komplett
        var ev = new CalendarEvent("e1", "Ganztag",
            BerlinTime(2026, 5, 27, 9, 0),
            BerlinTime(2026, 5, 27, 18, 0),
            null, null);

        var slots = Calc().Calculate(
            from: BerlinTime(2026, 5, 27, 0, 0),
            to: BerlinTime(2026, 5, 28, 0, 0),
            events: new[] { ev },
            durationMinutes: 60);

        slots.Should().BeEmpty();
    }

    private static DateTimeOffset BerlinTime(int y, int m, int d, int h, int min) =>
        new DateTimeOffset(y, m, d, h, min, 0,
            Berlin.GetUtcOffset(new DateTime(y, m, d, h, min, 0)));
}
```

- [ ] **Step 2: Tests laufen lassen, FAIL bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~FreeSlotCalculatorTests"
```

Expected: Compile-Fehler — `FreeSlotCalculator` existiert nicht.

- [ ] **Step 3: FreeSlotCalculator implementieren**

Datei `src/Backend/Features/Calendar/FreeSlotCalculator.cs`:

```csharp
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Features.Calendar;

public sealed class FreeSlotCalculator
{
    private readonly TimeZoneInfo _localZone;
    private readonly TimeOnly _workStart;
    private readonly TimeOnly _workEnd;
    private readonly DayOfWeekFlags _workingDays;

    public FreeSlotCalculator(
        TimeZoneInfo localZone,
        TimeOnly workStart,
        TimeOnly workEnd,
        DayOfWeekFlags workingDays)
    {
        _localZone = localZone;
        _workStart = workStart;
        _workEnd = workEnd;
        _workingDays = workingDays;
    }

    public IReadOnlyList<SlotCandidate> Calculate(
        DateTimeOffset from,
        DateTimeOffset to,
        IEnumerable<CalendarEvent> events,
        int durationMinutes)
    {
        if (durationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        if (to <= from) return Array.Empty<SlotCandidate>();

        var duration = TimeSpan.FromMinutes(durationMinutes);
        var eventList = events
            .Select(e => (Start: e.Start, End: e.End))
            .OrderBy(e => e.Start)
            .ToList();

        var result = new List<SlotCandidate>();

        // Tageweise iterieren (in Lokalzeit), Range begrenzen
        var localFrom = TimeZoneInfo.ConvertTime(from, _localZone);
        var localTo = TimeZoneInfo.ConvertTime(to, _localZone);
        var day = localFrom.Date;

        while (day < localTo.Date.AddDays(1))
        {
            var dayFlag = DayFlagOf(day.DayOfWeek);
            if (_workingDays.HasFlag(dayFlag))
            {
                // Tagesfenster in Lokalzeit, dann auf Offset des Tages konvertieren
                var dayStartLocal = day.Add(_workStart.ToTimeSpan());
                var dayEndLocal = day.Add(_workEnd.ToTimeSpan());

                var dayStartUtc = new DateTimeOffset(dayStartLocal, _localZone.GetUtcOffset(dayStartLocal));
                var dayEndUtc = new DateTimeOffset(dayEndLocal, _localZone.GetUtcOffset(dayEndLocal));

                // Auf gesuchten Range zuschneiden
                var windowStart = dayStartUtc < from ? from : dayStartUtc;
                var windowEnd = dayEndUtc > to ? to : dayEndUtc;
                if (windowStart >= windowEnd)
                {
                    day = day.AddDays(1);
                    continue;
                }

                // Events, die mit dem Tagesfenster überlappen, schneiden den freien Raum aus
                var occupants = eventList
                    .Where(e => e.Start < windowEnd && e.End > windowStart)
                    .ToList();

                var cursor = windowStart;
                foreach (var occ in occupants)
                {
                    if (occ.Start > cursor)
                    {
                        EmitSlots(result, cursor, occ.Start, duration);
                    }
                    if (occ.End > cursor)
                    {
                        cursor = occ.End;
                    }
                }

                if (cursor < windowEnd)
                {
                    EmitSlots(result, cursor, windowEnd, duration);
                }
            }

            day = day.AddDays(1);
        }

        return result;
    }

    /// <summary>
    /// Erzeugt feste Slot-Kandidaten von <paramref name="start"/> bis <paramref name="end"/>,
    /// jeweils Dauer-lang, nicht überlappend.
    /// </summary>
    private static void EmitSlots(List<SlotCandidate> sink, DateTimeOffset start, DateTimeOffset end, TimeSpan duration)
    {
        var cursor = start;
        while (cursor + duration <= end)
        {
            sink.Add(new SlotCandidate(cursor, cursor + duration));
            cursor += duration;
        }
    }

    private static DayOfWeekFlags DayFlagOf(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => DayOfWeekFlags.Monday,
        DayOfWeek.Tuesday   => DayOfWeekFlags.Tuesday,
        DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday,
        DayOfWeek.Thursday  => DayOfWeekFlags.Thursday,
        DayOfWeek.Friday    => DayOfWeekFlags.Friday,
        DayOfWeek.Saturday  => DayOfWeekFlags.Saturday,
        DayOfWeek.Sunday    => DayOfWeekFlags.Sunday,
        _ => DayOfWeekFlags.None,
    };
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~FreeSlotCalculatorTests"
```

Expected: 8 Tests grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan B Task 3: FreeSlotCalculator mit Pure-Logic-Tests"
```

---

## Task 4: LookupFreeSlots-Handler

**Files:**
- Create: `src/Backend/Features/Calendar/LookupFreeSlots/LookupFreeSlotsRequest.cs`
- Create: `src/Backend/Features/Calendar/LookupFreeSlots/LookupFreeSlotsHandler.cs`
- Create: `src/Backend.Tests/Features/Calendar/LookupFreeSlotsHandlerTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Calendar/LookupFreeSlotsHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class LookupFreeSlotsHandlerTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Handle_NoEventsNoRules_ReturnsPassingSlots()
    {
        using var db = new TempSqliteDb();
        var ruleRepo = new RuleRepository(db.AppDb);
        var provider = new FakeCalendarProvider();
        var calc = MakeCalculator();
        var applicator = new RuleApplicator(Berlin);

        var handler = new LookupFreeSlotsHandler(ruleRepo, provider, calc, applicator);

        var response = await handler.Handle(new LookupFreeSlotsRequest(
            From: BerlinTime(2026, 5, 27, 0, 0),
            To: BerlinTime(2026, 5, 28, 0, 0),
            DurationMinutes: 60), CancellationToken.None);

        response.Annotations.Should().NotBeEmpty();
        response.Annotations.Should().OnlyContain(a => a.Status == AnnotationStatus.Passes);
    }

    [Fact]
    public async Task Handle_RuleBlocksEvening_EveningSlotsAreHardViolations()
    {
        using var db = new TempSqliteDb();
        var ruleRepo = new RuleRepository(db.AppDb);
        await ruleRepo.AddAsync(new Rule(
            Id: 0,
            Text: "Mo-Fr nach 17 nicht",
            DaysOfWeek: DayOfWeekFlags.WeekdaysOnly,
            TimeRangeStart: new TimeOnly(17, 0),
            TimeRangeEnd: new TimeOnly(23, 59),
            Hardness: RuleHardness.Hard,
            CreatedAt: DateTimeOffset.UtcNow), CancellationToken.None);

        var provider = new FakeCalendarProvider();
        var calc = MakeCalculator();
        var applicator = new RuleApplicator(Berlin);

        var handler = new LookupFreeSlotsHandler(ruleRepo, provider, calc, applicator);

        var response = await handler.Handle(new LookupFreeSlotsRequest(
            From: BerlinTime(2026, 5, 27, 0, 0),
            To: BerlinTime(2026, 5, 28, 0, 0),
            DurationMinutes: 60), CancellationToken.None);

        // Slot 17-18 (oder Teile ab 17 Uhr) müssen als HardViolation kommen
        response.Annotations.Should().Contain(a =>
            a.Status == AnnotationStatus.HardViolation
            && a.Slot.Start.Hour >= 17);
    }

    [Fact]
    public async Task Handle_BlockedByExistingEvent_GapDisappearsCompletely()
    {
        using var db = new TempSqliteDb();
        var ruleRepo = new RuleRepository(db.AppDb);
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent("blocker", "Sprint",
            BerlinTime(2026, 5, 27, 9, 0),
            BerlinTime(2026, 5, 27, 18, 0),
            null, null));
        var calc = MakeCalculator();
        var applicator = new RuleApplicator(Berlin);

        var handler = new LookupFreeSlotsHandler(ruleRepo, provider, calc, applicator);

        var response = await handler.Handle(new LookupFreeSlotsRequest(
            From: BerlinTime(2026, 5, 27, 0, 0),
            To: BerlinTime(2026, 5, 28, 0, 0),
            DurationMinutes: 60), CancellationToken.None);

        response.Annotations.Should().BeEmpty();
    }

    private static FreeSlotCalculator MakeCalculator() => new(
        Berlin,
        new TimeOnly(9, 0),
        new TimeOnly(18, 0),
        DayOfWeekFlags.WeekdaysOnly);

    private static DateTimeOffset BerlinTime(int y, int m, int d, int h, int min) =>
        new DateTimeOffset(y, m, d, h, min, 0,
            Berlin.GetUtcOffset(new DateTime(y, m, d, h, min, 0)));
}
```

- [ ] **Step 2: LookupFreeSlotsRequest schreiben**

Datei `src/Backend/Features/Calendar/LookupFreeSlots/LookupFreeSlotsRequest.cs`:

```csharp
using Mediator;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Features.Calendar.LookupFreeSlots;

public sealed record LookupFreeSlotsRequest(
    DateTimeOffset From,
    DateTimeOffset To,
    int DurationMinutes) : IRequest<LookupFreeSlotsResponse>;

public sealed record LookupFreeSlotsResponse(IReadOnlyList<SlotAnnotation> Annotations);
```

- [ ] **Step 3: LookupFreeSlotsHandler schreiben**

Datei `src/Backend/Features/Calendar/LookupFreeSlots/LookupFreeSlotsHandler.cs`:

```csharp
using Mediator;
using NauAssist.Backend.Features.Rules;

namespace NauAssist.Backend.Features.Calendar.LookupFreeSlots;

public sealed class LookupFreeSlotsHandler : IRequestHandler<LookupFreeSlotsRequest, LookupFreeSlotsResponse>
{
    private readonly RuleRepository _ruleRepo;
    private readonly ICalendarProvider _calendar;
    private readonly FreeSlotCalculator _calculator;
    private readonly RuleApplicator _applicator;

    public LookupFreeSlotsHandler(
        RuleRepository ruleRepo,
        ICalendarProvider calendar,
        FreeSlotCalculator calculator,
        RuleApplicator applicator)
    {
        _ruleRepo = ruleRepo;
        _calendar = calendar;
        _calculator = calculator;
        _applicator = applicator;
    }

    public async ValueTask<LookupFreeSlotsResponse> Handle(LookupFreeSlotsRequest request, CancellationToken cancellationToken)
    {
        if (request.To <= request.From)
        {
            throw new ArgumentException("To muss nach From liegen.", nameof(request));
        }

        if (request.DurationMinutes <= 0)
        {
            throw new ArgumentException("DurationMinutes muss > 0 sein.", nameof(request));
        }

        var rules = await _ruleRepo.ListAllAsync(cancellationToken);
        var events = await _calendar.GetEventsAsync(request.From, request.To, cancellationToken);
        var candidates = _calculator.Calculate(request.From, request.To, events, request.DurationMinutes);
        var annotations = _applicator.Annotate(candidates, rules);

        return new LookupFreeSlotsResponse(annotations);
    }
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~LookupFreeSlotsHandlerTests"
```

Expected: 3 Tests grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan B Task 4: LookupFreeSlots-Handler (Provider + FreeSlotCalculator + RuleApplicator)"
```

---

## Task 5: CreateEvent-Handler

**Files:**
- Create: `src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs`
- Create: `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs`
- Create: `src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CreateEventHandlerTests
{
    [Fact]
    public async Task Handle_CreatesEvent_AndReturnsProviderId()
    {
        var provider = new FakeCalendarProvider();
        var handler = new CreateEventHandler(provider);

        var response = await handler.Handle(new CreateEventRequest(
            Title: "Pierre",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        response.EventId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_RejectsEmptyTitle()
    {
        var provider = new FakeCalendarProvider();
        var handler = new CreateEventHandler(provider);

        var act = async () => await handler.Handle(new CreateEventRequest(
            Title: "",
            Start: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Title*");
    }

    [Fact]
    public async Task Handle_RejectsEndBeforeStart()
    {
        var provider = new FakeCalendarProvider();
        var handler = new CreateEventHandler(provider);

        var act = async () => await handler.Handle(new CreateEventRequest(
            Title: "X",
            Start: DateTimeOffset.Parse("2026-05-27T15:00:00+02:00"),
            End: DateTimeOffset.Parse("2026-05-27T14:00:00+02:00"),
            Description: null,
            Location: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*End*");
    }
}
```

- [ ] **Step 2: CreateEventRequest schreiben**

Datei `src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Calendar.CreateEvent;

public sealed record CreateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location) : IRequest<CreateEventResponse>;

public sealed record CreateEventResponse(string EventId);
```

- [ ] **Step 3: CreateEventHandler schreiben**

Datei `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Calendar.CreateEvent;

public sealed class CreateEventHandler : IRequestHandler<CreateEventRequest, CreateEventResponse>
{
    private readonly ICalendarProvider _calendar;

    public CreateEventHandler(ICalendarProvider calendar)
    {
        _calendar = calendar;
    }

    public async ValueTask<CreateEventResponse> Handle(CreateEventRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Title darf nicht leer sein.", nameof(request));
        }

        if (request.End <= request.Start)
        {
            throw new ArgumentException("End muss nach Start liegen.", nameof(request));
        }

        var newEvent = new NewEvent(
            Title: request.Title.Trim(),
            Start: request.Start,
            End: request.End,
            Description: request.Description,
            Location: request.Location);

        var id = await _calendar.CreateEventAsync(newEvent, cancellationToken);
        return new CreateEventResponse(id);
    }
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~CreateEventHandlerTests"
```

Expected: 3 Tests grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan B Task 5: CreateEvent-Handler mit Validierung"
```

---

## Task 6: GetCalendarRange-Handler

**Files:**
- Create: `src/Backend/Features/Calendar/GetCalendarRange/GetCalendarRangeRequest.cs`
- Create: `src/Backend/Features/Calendar/GetCalendarRange/GetCalendarRangeHandler.cs`
- Create: `src/Backend.Tests/Features/Calendar/GetCalendarRangeHandlerTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Calendar/GetCalendarRangeHandlerTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class GetCalendarRangeHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsEventsFromProvider()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent("e1", "A",
            DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"),
            DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"),
            null, null));
        var handler = new GetCalendarRangeHandler(provider);

        var response = await handler.Handle(new GetCalendarRangeRequest(
            From: DateTimeOffset.Parse("2026-05-27T00:00:00+02:00"),
            To: DateTimeOffset.Parse("2026-05-28T00:00:00+02:00")), CancellationToken.None);

        response.Events.Should().ContainSingle(e => e.Id == "e1");
    }

    [Fact]
    public async Task Handle_RejectsInvalidRange()
    {
        var provider = new FakeCalendarProvider();
        var handler = new GetCalendarRangeHandler(provider);

        var act = async () => await handler.Handle(new GetCalendarRangeRequest(
            From: DateTimeOffset.Parse("2026-05-28T00:00:00+02:00"),
            To: DateTimeOffset.Parse("2026-05-27T00:00:00+02:00")), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
```

- [ ] **Step 2: GetCalendarRangeRequest schreiben**

Datei `src/Backend/Features/Calendar/GetCalendarRange/GetCalendarRangeRequest.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Calendar.GetCalendarRange;

public sealed record GetCalendarRangeRequest(
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<GetCalendarRangeResponse>;

public sealed record GetCalendarRangeResponse(IReadOnlyList<CalendarEvent> Events);
```

- [ ] **Step 3: GetCalendarRangeHandler schreiben**

Datei `src/Backend/Features/Calendar/GetCalendarRange/GetCalendarRangeHandler.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Calendar.GetCalendarRange;

public sealed class GetCalendarRangeHandler : IRequestHandler<GetCalendarRangeRequest, GetCalendarRangeResponse>
{
    private readonly ICalendarProvider _calendar;

    public GetCalendarRangeHandler(ICalendarProvider calendar)
    {
        _calendar = calendar;
    }

    public async ValueTask<GetCalendarRangeResponse> Handle(GetCalendarRangeRequest request, CancellationToken cancellationToken)
    {
        if (request.To <= request.From)
        {
            throw new ArgumentException("To muss nach From liegen.", nameof(request));
        }

        var events = await _calendar.GetEventsAsync(request.From, request.To, cancellationToken);
        return new GetCalendarRangeResponse(events);
    }
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~GetCalendarRangeHandlerTests"
```

Expected: 2 Tests grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan B Task 6: GetCalendarRange-Handler"
```

---

## Task 7: SQLite-OAuth-Storage + Migration 0002

**Files:**
- Create: `src/Backend/Features/Infrastructure/Persistence/Migrations/0002_google_oauth.sql`
- Create: `src/Backend/Features/Calendar/Google/SqliteDataStore.cs`
- Create: `src/Backend.Tests/Features/Calendar/SqliteDataStoreTests.cs`
- Modify: `src/Backend/Backend.csproj` (NuGet `Google.Apis.Auth`)

- [ ] **Step 1: Google.Apis.Auth installieren**

Run:
```bash
dotnet add src/Backend/Backend.csproj package Google.Apis.Auth
```

- [ ] **Step 2: Migration 0002 anlegen**

Datei `src/Backend/Features/Infrastructure/Persistence/Migrations/0002_google_oauth.sql`:

```sql
CREATE TABLE google_oauth (
    key         TEXT PRIMARY KEY,
    value       BLOB NOT NULL,
    updated_at  TEXT NOT NULL
);
```

- [ ] **Step 3: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Calendar/SqliteDataStoreTests.cs`:

```csharp
using FluentAssertions;
using Google.Apis.Auth.OAuth2.Responses;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class SqliteDataStoreTests
{
    [Fact]
    public async Task Store_PersistsValue_AndRetrievesIt()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);

        var token = new TokenResponse { AccessToken = "abc", RefreshToken = "xyz" };
        await store.StoreAsync("user-key", token);

        var loaded = await store.GetAsync<TokenResponse>("user-key");

        loaded.Should().NotBeNull();
        loaded!.AccessToken.Should().Be("abc");
        loaded.RefreshToken.Should().Be("xyz");
    }

    [Fact]
    public async Task Store_Overwrite_UpdatesValue()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);

        await store.StoreAsync("k", new TokenResponse { AccessToken = "v1" });
        await store.StoreAsync("k", new TokenResponse { AccessToken = "v2" });

        var loaded = await store.GetAsync<TokenResponse>("k");
        loaded!.AccessToken.Should().Be("v2");
    }

    [Fact]
    public async Task Delete_RemovesValue()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);
        await store.StoreAsync("k", new TokenResponse { AccessToken = "v" });

        await store.DeleteAsync<TokenResponse>("k");

        var loaded = await store.GetAsync<TokenResponse>("k");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        using var db = new TempSqliteDb();
        var store = new SqliteDataStore(db.AppDb);

        var loaded = await store.GetAsync<TokenResponse>("nope");

        loaded.Should().BeNull();
    }
}
```

- [ ] **Step 4: SqliteDataStore implementieren**

Datei `src/Backend/Features/Calendar/Google/SqliteDataStore.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Dapper;
using Google.Apis.Util.Store;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Calendar.Google;

/// <summary>
/// IDataStore-Implementierung für Google.Apis, persistiert in SQLite (Tabelle google_oauth).
/// Serialisiert Werte als UTF-8-JSON, Schlüssel = {typeName}::{key}.
/// </summary>
public sealed class SqliteDataStore : IDataStore
{
    private readonly AppDb _db;

    public SqliteDataStore(AppDb db)
    {
        _db = db;
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        var combinedKey = MakeKey<T>(key);
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO google_oauth(key, value, updated_at)
              VALUES(@k, @v, @ts)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;",
            new { k = combinedKey, v = bytes, ts = DateTimeOffset.UtcNow.ToString("O") });
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var combinedKey = MakeKey<T>(key);

        using var conn = _db.OpenConnection();
        var bytes = await conn.QueryFirstOrDefaultAsync<byte[]?>(
            "SELECT value FROM google_oauth WHERE key = @k;",
            new { k = combinedKey });

        if (bytes is null)
        {
            return default!;
        }

        var json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    public async Task DeleteAsync<T>(string key)
    {
        var combinedKey = MakeKey<T>(key);

        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync("DELETE FROM google_oauth WHERE key = @k;", new { k = combinedKey });
    }

    public async Task ClearAsync()
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync("DELETE FROM google_oauth;");
    }

    private static string MakeKey<T>(string key) => $"{typeof(T).FullName}::{key}";
}
```

- [ ] **Step 5: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~SqliteDataStoreTests"
```

Expected: 4 Tests grün. Migration 0002 muss von `DbInitializer` automatisch geladen und angewendet werden — falls nicht, ist die Embedded-Resource-Glob-Pattern fehlerhaft.

- [ ] **Step 6: Volle Suite verifizieren**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle Tests grün.

- [ ] **Step 7: Commit**

```bash
git add src/
git commit -m "Plan B Task 7: SQLite-basierter OAuth-Storage + Migration 0002"
```

---

## Task 8: GoogleCalendarProvider + GoogleAuthService

**Files:**
- Modify: `src/Backend/Backend.csproj` (NuGet `Google.Apis.Calendar.v3`)
- Create: `src/Backend/Features/Calendar/Google/GoogleAuthService.cs`
- Create: `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`

**Hinweis:** `GoogleCalendarProvider` wird nicht im Default-Test-Run getestet — er ruft die echte Google API. Smoke-Tests dagegen sind explizit `[Trait("Category","RequiresGoogle")]` markiert und werden manuell gestartet.

- [ ] **Step 1: Google.Apis.Calendar.v3 installieren**

Run:
```bash
dotnet add src/Backend/Backend.csproj package Google.Apis.Calendar.v3
```

- [ ] **Step 2: GoogleAuthService schreiben**

Datei `src/Backend/Features/Calendar/Google/GoogleAuthService.cs`:

```csharp
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleAuthService
{
    private readonly CalendarOptions _options;
    private readonly SqliteDataStore _dataStore;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IOptions<CalendarOptions> options,
        SqliteDataStore dataStore,
        ILogger<GoogleAuthService> logger)
    {
        _options = options.Value;
        _dataStore = dataStore;
        _logger = logger;
    }

    /// <summary>
    /// Liefert Credentials. Wenn noch keine im Store: löst interaktiven OAuth-Flow aus
    /// (öffnet Browser auf der lokalen Maschine).
    /// </summary>
    public async Task<UserCredential> GetCredentialAsync(CancellationToken ct)
    {
        var credentialsPath = Path.GetFullPath(_options.GoogleCredentialsPath);
        if (!File.Exists(credentialsPath))
        {
            throw new InvalidOperationException(
                $"Google-OAuth-Client-Secret nicht gefunden unter '{credentialsPath}'. " +
                "Bitte Datei aus der Google Cloud Console (OAuth 2.0 Client ID, Type: Desktop) herunterladen und dorthin legen.");
        }

        await using var stream = File.OpenRead(credentialsPath);
        var clientSecrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            new[] { CalendarService.Scope.Calendar },
            user: "nauassist-default",
            taskCancellationToken: ct,
            dataStore: _dataStore);

        if (credential.Token.IsStale)
        {
            _logger.LogInformation("Google-Token ist abgelaufen — refreshe.");
            await credential.RefreshTokenAsync(ct);
        }

        return credential;
    }
}
```

- [ ] **Step 3: GoogleCalendarProvider schreiben**

Datei `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`:

```csharp
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class GoogleCalendarProvider : ICalendarProvider
{
    private readonly GoogleAuthService _auth;
    private readonly CalendarOptions _options;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(
        GoogleAuthService auth,
        IOptions<CalendarOptions> options,
        ILogger<GoogleCalendarProvider> logger)
    {
        _auth = auth;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var service = await CreateServiceAsync(ct);
        var req = service.Events.List(_options.GoogleCalendarId);
        req.TimeMinDateTimeOffset = from;
        req.TimeMaxDateTimeOffset = to;
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        req.MaxResults = 250;

        var resp = await req.ExecuteAsync(ct);

        return resp.Items
            .Where(e => e.Start?.DateTimeDateTimeOffset is not null && e.End?.DateTimeDateTimeOffset is not null)
            .Select(MapToDomain)
            .ToList();
    }

    public async Task<string> CreateEventAsync(NewEvent ev, CancellationToken ct)
    {
        var service = await CreateServiceAsync(ct);
        var googleEvent = new Event
        {
            Summary = ev.Title,
            Description = ev.Description,
            Location = ev.Location,
            Start = new EventDateTime { DateTimeDateTimeOffset = ev.Start },
            End = new EventDateTime { DateTimeDateTimeOffset = ev.End },
        };

        var created = await service.Events.Insert(googleEvent, _options.GoogleCalendarId).ExecuteAsync(ct);
        _logger.LogInformation("Google-Event {EventId} angelegt für '{Title}' am {Start}.", created.Id, ev.Title, ev.Start);
        return created.Id;
    }

    private async Task<CalendarService> CreateServiceAsync(CancellationToken ct)
    {
        var credential = await _auth.GetCredentialAsync(ct);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "NauAssist",
        });
    }

    private static CalendarEvent MapToDomain(Event e) => new(
        Id: e.Id,
        Title: e.Summary ?? "(ohne Titel)",
        Start: e.Start!.DateTimeDateTimeOffset!.Value,
        End: e.End!.DateTimeDateTimeOffset!.Value,
        Description: e.Description,
        Location: e.Location);
}
```

- [ ] **Step 4: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: 0 Errors. Warnings sind tolerierbar (Google-SDK hat manchmal welche).

- [ ] **Step 5: Tests laufen lassen, GREEN bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle Tests grün (GoogleCalendarProvider wird hier noch nicht geprüft, das passiert manuell in Task 10).

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Plan B Task 8: GoogleCalendarProvider + GoogleAuthService"
```

---

## Task 9: DI-Verkabelung + appsettings + Auth-CLI-Sub-Command

**Files:**
- Create: `src/Backend/Features/Calendar/Google/GoogleAuthCommand.cs`
- Modify: `src/Backend/Program.cs`
- Modify: `src/Backend/appsettings.json`

**Voraussetzung (vom User außerhalb des Codes erledigt):**
1. https://console.cloud.google.com → Projekt anlegen
2. "APIs & Services" → "Library" → "Google Calendar API" → Enable
3. "APIs & Services" → "Credentials" → "Create Credentials" → "OAuth client ID" → Type: "Desktop app"
4. JSON herunterladen, in `./data/google-credentials.json` legen
5. "OAuth consent screen" → Testing-Mode mit eigener E-Mail als Test-User

- [ ] **Step 1: appsettings.json erweitern**

In `src/Backend/appsettings.json` die `Calendar`-Sektion ergänzen. Die finale Datei soll so aussehen:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Persistence": {
    "DatabasePath": "./data/nauassist.db"
  },
  "Calendar": {
    "WorkingHoursStart": "09:00",
    "WorkingHoursEnd": "18:00",
    "DefaultDurationMinutes": 60,
    "SearchHorizonDays": 14,
    "GoogleCalendarId": "primary",
    "GoogleCredentialsPath": "./data/google-credentials.json"
  }
}
```

- [ ] **Step 2: GoogleAuthCommand schreiben**

Datei `src/Backend/Features/Calendar/Google/GoogleAuthCommand.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NauAssist.Backend.Features.Calendar.Google;

/// <summary>
/// CLI-Logik für `dotnet run --project src/Backend -- auth`.
/// Öffnet den Browser, lässt den User die App autorisieren, persistiert Tokens in SQLite.
/// </summary>
public static class GoogleAuthCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILogger<object>>();
        var auth = services.GetRequiredService<GoogleAuthService>();

        try
        {
            logger.LogInformation("Starte OAuth-Flow gegen Google. Browser öffnet sich gleich…");
            var credential = await auth.GetCredentialAsync(ct);
            logger.LogInformation("OAuth-Flow erfolgreich. Token-Ablauf: {Expiry}", credential.Token.ExpiresInSeconds);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth-Flow fehlgeschlagen.");
            return 1;
        }
    }
}
```

- [ ] **Step 3: Program.cs aktualisieren — DI-Registrierungen + Sub-Command**

`src/Backend/Program.cs` komplett überschreiben:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Endpoints;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Features.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<CalendarOptions>(builder.Configuration.GetSection("Calendar"));

builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<DbInitializer>();

builder.Services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
builder.Services.AddScoped<NauAssist.Backend.Features.Rules.RuleRepository>();
builder.Services.AddSingleton(_ =>
    new NauAssist.Backend.Features.Rules.RuleApplicator(
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));

// Calendar
builder.Services.AddSingleton<SqliteDataStore>();
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CalendarOptions>>().Value;
    return new FreeSlotCalculator(
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"),
        TimeOnly.Parse(opts.WorkingHoursStart),
        TimeOnly.Parse(opts.WorkingHoursEnd),
        NauAssist.Backend.Features.Rules.DayOfWeekFlags.WeekdaysOnly);
});

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

var app = builder.Build();

// Migrationen beim Startup
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    initializer.Initialize();
}

// Sub-Command "auth": OAuth-Flow ausführen und beenden, ohne den Web-Host zu starten
if (args.Contains("auth"))
{
    var exitCode = await GoogleAuthCommand.RunAsync(app.Services, CancellationToken.None);
    return exitCode;
}

app.MapHealthEndpoints();
app.MapRulesEndpoints();

await app.RunAsync();
return 0;

// Für WebApplicationFactory<Program>
public partial class Program;
```

- [ ] **Step 4: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: 0 Errors. (Hinweis: das Mischen von Top-Level-Statements mit `return`-Ausdrücken erfordert .NET 9+. Für .NET 10 läuft das problemlos.)

- [ ] **Step 5: Volle Test-Suite**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle bisherigen Tests grün. Bei Versagen wahrscheinlich DI-Wiring-Problem.

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Plan B Task 9: DI-Verkabelung, appsettings Calendar-Sektion, auth-Sub-Command"
```

---

## Task 10: Plan-B-Abschluss-Verifikation

**Files:** keine neuen.

- [ ] **Step 1: Komplettes Test-Suite-Run**

Run:
```bash
dotnet test src/NauAssist.slnx
```

Expected: Alle Tests grün. Erwartete neue Tests aus Plan B:
- CalendarModelTests × 3
- FakeCalendarProviderTests × 3
- FreeSlotCalculatorTests × 8
- LookupFreeSlotsHandlerTests × 3
- CreateEventHandlerTests × 3
- GetCalendarRangeHandlerTests × 2
- SqliteDataStoreTests × 4

= 26 neue Tests. Plus die 30 aus Plan A = 56 insgesamt.

- [ ] **Step 2: Manueller Auth-Flow** *(nur falls Google-Credentials vorliegen)*

Run:
```bash
# Voraussetzung: ./data/google-credentials.json existiert mit gültigem Client Secret
dotnet run --project src/Backend -- auth
```

Expected:
- Browser öffnet sich mit Google-Login
- Nach Login + Consent: Console zeigt "OAuth-Flow erfolgreich"
- In SQLite `google_oauth`-Tabelle steht jetzt ein Eintrag
- Exit-Code 0

Falls keine Google-Credentials vorliegen: Schritt überspringen, der Fehler "Google-OAuth-Client-Secret nicht gefunden unter '...'" ist erwartet und sauber.

- [ ] **Step 3: Smoke-Test gegen echtes Google Calendar** *(nur falls Auth-Flow lief)*

Bei vorhandenem Token kann der echte Provider getestet werden, indem über einen kleinen Test-Endpoint oder direkt via .NET Interactive eine Mediator-Anfrage abgesetzt wird. Da Plan B keine REST-Endpoints für die Calendar-Handler exponiert (das macht Plan C über die Agent-Tools), ist dies optional und wird normalerweise erst in Plan D praktisch.

- [ ] **Step 4: Abschluss-Commit** *(falls Korrekturen aus den Smoke-Tests nötig)*

Falls keine Änderungen → kein Commit nötig. Sonst:

```bash
git add src/
git commit -m "Plan B Task 10: Smoke-Test-Korrekturen"
```

---

## Plan-B-Abschluss

Nach Task 10 läuft:
- ✅ `ICalendarProvider`-Abstraktion mit `FakeCalendarProvider` (Tests) und `GoogleCalendarProvider` (Produktion)
- ✅ `FreeSlotCalculator` mit ausführlichen Pure-Logic-Tests
- ✅ Drei Handler: `LookupFreeSlots`, `CreateEvent`, `GetCalendarRange`
- ✅ SQLite-OAuth-Storage (Migration 0002)
- ✅ `dotnet run -- auth`-Sub-Command für initiale Autorisierung
- ✅ DI-Verkabelung: GoogleCalendarProvider ist die Default-Implementierung

**Was als Nächstes kommt (Plan C):**
- `ILlmClient`-Interface + `OllamaLlmClient` (OpenAI-kompatibel) + `FakeLlmClient` (Test-Double)
- `AgentRunner` (Microsoft Agent Framework)
- Tool-Adapter, die `Mediator.Send` für jede registrierte Funktion aufrufen
- Tool-Loop-Logik mit Iterations-Limit

Der MVP-Workflow ist nach Plan C "fast da" — es fehlt dann nur noch die Chat-Surface und das Streaming (Plan D) und das Frontend (Plan E).
