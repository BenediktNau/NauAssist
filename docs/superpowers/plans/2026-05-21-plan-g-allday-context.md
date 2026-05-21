# Plan G: All-Day Context — Implementierungsplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All-Day-Events aus dem Google-Kalender erreichen das Domänenmodell, blockieren aber keine Slots; statt­dessen prependet der `AgentRunner` einen zweiten System-Prompt-Block mit den anstehenden All-Day-Einträgen, und `create_event` kann All-Day-Termine selbst anlegen.

**Architecture:** `CalendarEvent`/`NewEvent` bekommen `IsAllDay`. Mapping-Logik wandert aus `GoogleCalendarProvider` in einen unit-testbaren `GoogleEventMapper`, der `Date`-only-Events zu `DateTimeOffset` 00:00 in lokaler TZ auflöst (End exklusiv, Folgetag-Konvention). `FreeSlotCalculator` filtert `IsAllDay` vor dem Slot-Schnitt aus. Neuer `CalendarContextBuilder` liest All-Day-Events im 14-Tage-Horizont und rendert einen Markdown-Block; `AgentRunner` hängt ihn als zweite system-Message nach dem Zeit-Kontext an. `CreateEventTool` lernt einen optionalen `is_all_day`-Parameter.

**Tech Stack:** .NET 10, xUnit + FluentAssertions, Mediator, Ollama-LLM-Client (gemma4), Google.Apis.Calendar.v3.

**Referenz-Spec:** `docs/superpowers/specs/2026-05-21-allday-context-design.md`

---

## File Structure

**Neu erstellt:**
- `src/Backend/Features/Calendar/Google/GoogleEventMapper.cs` — internal static, Mapping Google `Event` → `CalendarEvent?` (DateTime + Date-only).
- `src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs` — Scoped Service, baut den All-Day-Markdown-Block.

**Modifiziert:**
- `src/Backend/Features/Calendar/CalendarEvent.cs` — Record-Parameter `IsAllDay = false` ergänzt.
- `src/Backend/Features/Calendar/NewEvent.cs` — analog.
- `src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs` — analog.
- `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs` — `IsAllDay` an `NewEvent` durchreichen.
- `src/Backend/Features/Calendar/FreeSlotCalculator.cs` — All-Day-Filter vor dem `.Select`-Tupel.
- `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs` — Mapping über neuen Mapper; Filter umgebaut; `CreateEventAsync` für All-Day; `TimeZoneInfo` per DI.
- `src/Backend/Features/Agent/AgentRunner.cs` — Konstruktor mit `CalendarContextBuilder`; zweite system-Message nach Zeit-Kontext.
- `src/Backend/Features/Agent/Tools/CreateEventTool.cs` — Schema-Parameter `is_all_day`, Date-only-Parse mit `TimeZoneInfo` aus DI.
- `src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs` — Event-DTO mit `is_all_day`.
- `src/Backend/Program.cs` — `CalendarContextBuilder` registrieren.
- `src/Backend/appsettings.json` — Schlusssatz im `Ollama.SystemPrompt`.
- `src/Backend.Tests/Helpers/FakeCalendarProvider.cs` — `IsAllDay` beim Roundtrip `Create → Seed` kopieren.

**Tests neu / erweitert:**
- `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs` — Defaults + All-Day-Konstruktion.
- `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs` — All-Day blockiert nicht; Mischfall.
- `src/Backend.Tests/Features/Calendar/Google/GoogleEventMapperTests.cs` (neu) — DateTime, Date-only, Multi-Day, DST.
- `src/Backend.Tests/Features/Calendar/CalendarContextBuilderTests.cs` (neu).
- `src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs` — All-Day-Durchreichung.
- `src/Backend.Tests/Features/Agent/Tools/CreateEventToolTests.cs` (neu).
- `src/Backend.Tests/Features/Agent/AgentRunnerCalendarContextTests.cs` (neu).

---

## Task 1: `IsAllDay` an die Records anflanschen

**Files:**
- Modify: `src/Backend/Features/Calendar/CalendarEvent.cs`
- Modify: `src/Backend/Features/Calendar/NewEvent.cs`
- Modify: `src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs`
- Modify: `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs`

- [ ] **Step 1: Failing tests in `CalendarModelTests` ergänzen**

In `src/Backend.Tests/Features/Calendar/CalendarModelTests.cs` zwei Tests anhängen (vor der schließenden `}` der Klasse):

```csharp
[Fact]
public void CalendarEvent_IsAllDay_DefaultsToFalse()
{
    var ev = new CalendarEvent(
        Id: "e",
        Title: "Sprint",
        Start: DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"),
        End: DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"),
        Description: null,
        Location: null);

    ev.IsAllDay.Should().BeFalse();
}

[Fact]
public void NewEvent_IsAllDay_DefaultsToFalse_AndCanBeTrue()
{
    var regular = new NewEvent(
        Title: "Lunch",
        Start: DateTimeOffset.Parse("2026-05-27T12:00:00+02:00"),
        End: DateTimeOffset.Parse("2026-05-27T13:00:00+02:00"),
        Description: null,
        Location: null);

    regular.IsAllDay.Should().BeFalse();

    var allDay = new NewEvent(
        Title: "Urlaub",
        Start: DateTimeOffset.Parse("2026-06-01T00:00:00+02:00"),
        End: DateTimeOffset.Parse("2026-06-02T00:00:00+02:00"),
        Description: null,
        Location: null,
        IsAllDay: true);

    allDay.IsAllDay.Should().BeTrue();
}
```

- [ ] **Step 2: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CalendarModelTests"`
Expected: Build error — Property `IsAllDay` existiert nicht.

- [ ] **Step 3: `CalendarEvent` erweitern**

Datei `src/Backend/Features/Calendar/CalendarEvent.cs` ersetzen durch:

```csharp
namespace NauAssist.Backend.Features.Calendar;

public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location,
    bool IsAllDay = false);
```

- [ ] **Step 4: `NewEvent` erweitern**

Datei `src/Backend/Features/Calendar/NewEvent.cs` ersetzen durch:

```csharp
namespace NauAssist.Backend.Features.Calendar;

public sealed record NewEvent(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location,
    bool IsAllDay = false);
```

- [ ] **Step 5: `CreateEventRequest` erweitern**

Datei `src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs` ersetzen durch:

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Calendar.CreateEvent;

public sealed record CreateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location,
    bool IsAllDay = false) : IRequest<CreateEventResponse>;

public sealed record CreateEventResponse(string EventId);
```

- [ ] **Step 6: Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CalendarModelTests"`
Expected: 5 passed (3 bestehende + 2 neue).

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded, 0 errors (alle bestehenden Aufrufer kompilieren weiter wegen Default `= false`).

- [ ] **Step 7: Commit**

```bash
git add src/Backend/Features/Calendar/CalendarEvent.cs \
        src/Backend/Features/Calendar/NewEvent.cs \
        src/Backend/Features/Calendar/CreateEvent/CreateEventRequest.cs \
        src/Backend.Tests/Features/Calendar/CalendarModelTests.cs
git commit -m "Plan G Task 1: IsAllDay-Flag in CalendarEvent/NewEvent/CreateEventRequest"
```

---

## Task 2: `FakeCalendarProvider` reicht `IsAllDay` durch

**Files:**
- Modify: `src/Backend.Tests/Helpers/FakeCalendarProvider.cs`

Im FakeProvider wird beim `CreateEventAsync` aus dem `NewEvent` ein `CalendarEvent` für die interne Liste gebaut — dort geht aktuell das `IsAllDay`-Flag verloren. Ohne diesen Fix würde der Roundtrip in späteren Tests (z. B. `AgentRunnerCalendarContextTests`, die Create + spätere Awareness gleichzeitig prüfen wollen) falsch laufen.

- [ ] **Step 1: `CreateEventAsync` anpassen**

In `src/Backend.Tests/Helpers/FakeCalendarProvider.cs` Zeile 49 ersetzen.

Alt:
```csharp
_events.Add(new CalendarEvent(id, ev.Title, ev.Start, ev.End, ev.Description, ev.Location));
```

Neu:
```csharp
_events.Add(new CalendarEvent(id, ev.Title, ev.Start, ev.End, ev.Description, ev.Location, ev.IsAllDay));
```

- [ ] **Step 2: Bestehende Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/Backend.Tests/Helpers/FakeCalendarProvider.cs
git commit -m "Plan G Task 2: FakeCalendarProvider reicht IsAllDay beim Roundtrip durch"
```

---

## Task 3: `FreeSlotCalculator` ignoriert All-Day-Events (TDD)

**Files:**
- Modify: `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs`
- Modify: `src/Backend/Features/Calendar/FreeSlotCalculator.cs`

- [ ] **Step 1: Failing Tests ergänzen**

In `src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs` vor der `private static DateTimeOffset BerlinTime(...)`-Methode zwei Tests anhängen:

```csharp
[Fact]
public void Calculate_AllDayEvent_DoesNotBlockSlots()
{
    var schulung = new CalendarEvent(
        Id: "e-allday",
        Title: "Schulung",
        Start: BerlinTime(2026, 5, 27, 0, 0),
        End:   BerlinTime(2026, 5, 28, 0, 0),
        Description: null,
        Location: null,
        IsAllDay: true);

    var slots = Calc().Calculate(
        from: BerlinTime(2026, 5, 27, 0, 0),
        to:   BerlinTime(2026, 5, 28, 0, 0),
        events: new[] { schulung },
        durationMinutes: 60);

    slots.Should().NotBeEmpty();
    slots.First().Start.Should().Be(BerlinTime(2026, 5, 27, 9, 0));
    slots.Last().End.Should().BeOnOrBefore(BerlinTime(2026, 5, 27, 18, 0));
}

[Fact]
public void Calculate_AllDayPlusRegularSameDay_OnlyRegularBlocks()
{
    var schulung = new CalendarEvent(
        Id: "e-allday",
        Title: "Schulung",
        Start: BerlinTime(2026, 5, 27, 0, 0),
        End:   BerlinTime(2026, 5, 28, 0, 0),
        Description: null, Location: null, IsAllDay: true);

    var mittag = new CalendarEvent(
        Id: "e-mittag",
        Title: "Mittagstermin",
        Start: BerlinTime(2026, 5, 27, 12, 0),
        End:   BerlinTime(2026, 5, 27, 13, 0),
        Description: null, Location: null);

    var slots = Calc().Calculate(
        from: BerlinTime(2026, 5, 27, 0, 0),
        to:   BerlinTime(2026, 5, 28, 0, 0),
        events: new[] { schulung, mittag },
        durationMinutes: 60);

    slots.Should().Contain(s => s.End <= BerlinTime(2026, 5, 27, 12, 0));
    slots.Should().Contain(s => s.Start >= BerlinTime(2026, 5, 27, 13, 0));
    slots.Should().NotContain(s => s.Start < BerlinTime(2026, 5, 27, 13, 0) && s.End > BerlinTime(2026, 5, 27, 12, 0));
}
```

- [ ] **Step 2: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~FreeSlotCalculatorTests"`
Expected: Der erste Test schlägt fehl (`slots.Should().NotBeEmpty()` — All-Day blockiert aktuell den ganzen Tag), der zweite ebenfalls.

- [ ] **Step 3: `FreeSlotCalculator` anpassen**

In `src/Backend/Features/Calendar/FreeSlotCalculator.cs` Zeile 34–37 ändern:

Alt:
```csharp
var eventList = events
    .Select(e => (Start: e.Start, End: e.End))
    .OrderBy(e => e.Start)
    .ToList();
```

Neu (Filter VOR `.Select`, sonst geht die Flag-Info verloren):
```csharp
var eventList = events
    .Where(e => !e.IsAllDay)
    .Select(e => (Start: e.Start, End: e.End))
    .OrderBy(e => e.Start)
    .ToList();
```

- [ ] **Step 4: Tests laufen lassen — sollen grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~FreeSlotCalculatorTests"`
Expected: 9 passed (7 bestehende + 2 neue).

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Calendar/FreeSlotCalculator.cs \
        src/Backend.Tests/Features/Calendar/FreeSlotCalculatorTests.cs
git commit -m "Plan G Task 3: FreeSlotCalculator ignoriert IsAllDay-Events"
```

---

## Task 4a: `GoogleEventMapper` extrahieren (Refactor ohne Verhaltensänderung)

**Files:**
- Create: `src/Backend/Features/Calendar/Google/GoogleEventMapper.cs`
- Modify: `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`

Reines Refactor: Mapping-Logik aus dem Provider in eine internal-static-Klasse holen, **noch ohne All-Day-Support**. Bestehende Tests müssen grün bleiben.

- [ ] **Step 1: `GoogleEventMapper` anlegen — Verhalten 1:1 wie aktuell**

Datei `src/Backend/Features/Calendar/Google/GoogleEventMapper.cs`:

```csharp
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

namespace NauAssist.Backend.Features.Calendar.Google;

internal static class GoogleEventMapper
{
    public static CalendarEvent? Map(GoogleEvent e, TimeZoneInfo zone)
    {
        if (e.Start is null || e.End is null) return null;

        if (e.Start.DateTimeDateTimeOffset is { } startDt && e.End.DateTimeDateTimeOffset is { } endDt)
        {
            return new CalendarEvent(
                Id: e.Id,
                Title: e.Summary ?? "(ohne Titel)",
                Start: startDt,
                End: endDt,
                Description: e.Description,
                Location: e.Location);
        }

        return null;
    }
}
```

- [ ] **Step 2: `GoogleCalendarProvider` auf den Mapper umstellen**

In `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`:

(a) `GetEventsAsync` — Z.40–43 ersetzen.

Alt:
```csharp
return resp.Items
    .Where(e => e.Start?.DateTimeDateTimeOffset is not null && e.End?.DateTimeDateTimeOffset is not null)
    .Select(MapToDomain)
    .ToList();
```

Neu (TimeZoneInfo wird in Task 4c per DI injiziert — fürs Refactor reicht `TimeZoneInfo.Utc` als Platzhalter, da der Mapper im Nicht-AllDay-Pfad die Zone gar nicht nutzt):
```csharp
return resp.Items
    .Select(e => GoogleEventMapper.Map(e, TimeZoneInfo.Utc))
    .Where(e => e is not null)
    .Select(e => e!)
    .ToList();
```

(b) Die private `MapToDomain`-Methode (Z.73–79) löschen — sie ist jetzt im Mapper.

- [ ] **Step 3: Build + alle Tests laufen lassen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded.

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alle Tests grün — kein Verhaltenswechsel.

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Features/Calendar/Google/GoogleEventMapper.cs \
        src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs
git commit -m "Plan G Task 4a: GoogleEventMapper extrahiert (Refactor)"
```

---

## Task 4b: `GoogleEventMapper` lernt All-Day-Events (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Calendar/Google/GoogleEventMapperTests.cs`
- Modify: `src/Backend/Features/Calendar/Google/GoogleEventMapper.cs`

- [ ] **Step 1: Test-Datei mit failing tests anlegen**

Datei `src/Backend.Tests/Features/Calendar/Google/GoogleEventMapperTests.cs`:

```csharp
using FluentAssertions;
using Google.Apis.Calendar.v3.Data;
using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Tests.Features.Calendar.Google;

public sealed class GoogleEventMapperTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void Map_DateTimeEvent_NotAllDay()
    {
        var e = new Event
        {
            Id = "e1",
            Summary = "Meeting",
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-05-27T10:00:00+02:00") },
            End   = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-05-27T11:00:00+02:00") },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().NotBeNull();
        result!.IsAllDay.Should().BeFalse();
        result.Start.Should().Be(DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"));
        result.End.Should().Be(DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"));
    }

    [Fact]
    public void Map_DateOnlyEvent_SingleDay_IsAllDay_LocalMidnight()
    {
        var e = new Event
        {
            Id = "e-urlaub",
            Summary = "Urlaub",
            Start = new EventDateTime { Date = "2026-06-01" },
            End   = new EventDateTime { Date = "2026-06-02" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().NotBeNull();
        result!.IsAllDay.Should().BeTrue();
        result.Start.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        result.End.Should().Be(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Map_DateOnlyEvent_MultiDay_EndIsExclusiveNextDay()
    {
        // Google-Konvention: Schulung 27.5.–29.5. → Date-End = 30.5. (exklusiv).
        var e = new Event
        {
            Id = "e-schulung",
            Summary = "Schulung Köln",
            Start = new EventDateTime { Date = "2026-05-27" },
            End   = new EventDateTime { Date = "2026-05-30" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().NotBeNull();
        result!.IsAllDay.Should().BeTrue();
        result.Start.Should().Be(new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)));
        result.End.Should().Be(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Map_DateOnly_DST_SpringForward_UsesCorrectOffset()
    {
        // 2026: DST-Start 29.03. Wir testen ein All-Day genau am Übergangstag.
        var e = new Event
        {
            Id = "e-dst",
            Summary = "DST-Tag",
            Start = new EventDateTime { Date = "2026-03-29" },
            End   = new EventDateTime { Date = "2026-03-30" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result!.IsAllDay.Should().BeTrue();
        // 29.3.2026 00:00 Berlin liegt noch in Winterzeit (UTC+1).
        result.Start.Offset.Should().Be(TimeSpan.FromHours(1));
        // 30.3.2026 00:00 Berlin liegt nach DST-Start (UTC+2).
        result.End.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Map_DateOnly_DST_FallBack_UsesCorrectOffset()
    {
        // 2026: DST-Ende 25.10.
        var e = new Event
        {
            Id = "e-dst-fall",
            Summary = "DST-Tag",
            Start = new EventDateTime { Date = "2026-10-25" },
            End   = new EventDateTime { Date = "2026-10-26" },
        };

        var result = GoogleEventMapper.Map(e, Berlin);

        result!.IsAllDay.Should().BeTrue();
        // 25.10.2026 00:00 Berlin: noch Sommerzeit (UTC+2).
        result.Start.Offset.Should().Be(TimeSpan.FromHours(2));
        // 26.10.2026 00:00 Berlin: Winterzeit (UTC+1).
        result.End.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Map_BothFieldsMissing_ReturnsNull()
    {
        var e = new Event { Id = "e-leer", Start = new EventDateTime(), End = new EventDateTime() };

        var result = GoogleEventMapper.Map(e, Berlin);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Tests laufen lassen — All-Day-Tests sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~GoogleEventMapperTests"`
Expected: 4 von 6 Tests fehlschlagen (DateTime-Test und das Null-Test bleiben grün, die vier All-Day-Tests scheitern).

- [ ] **Step 3: `GoogleEventMapper` um All-Day-Pfad erweitern**

Datei `src/Backend/Features/Calendar/Google/GoogleEventMapper.cs` ersetzen durch:

```csharp
using System.Globalization;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

namespace NauAssist.Backend.Features.Calendar.Google;

internal static class GoogleEventMapper
{
    public static CalendarEvent? Map(GoogleEvent e, TimeZoneInfo zone)
    {
        if (e.Start is null || e.End is null) return null;

        if (e.Start.DateTimeDateTimeOffset is { } startDt && e.End.DateTimeDateTimeOffset is { } endDt)
        {
            return new CalendarEvent(
                Id: e.Id,
                Title: e.Summary ?? "(ohne Titel)",
                Start: startDt,
                End: endDt,
                Description: e.Description,
                Location: e.Location,
                IsAllDay: false);
        }

        if (!string.IsNullOrEmpty(e.Start.Date) && !string.IsNullOrEmpty(e.End.Date))
        {
            var startDate = DateOnly.ParseExact(e.Start.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var endDate = DateOnly.ParseExact(e.End.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new CalendarEvent(
                Id: e.Id,
                Title: e.Summary ?? "(ohne Titel)",
                Start: ToLocalMidnight(startDate, zone),
                End: ToLocalMidnight(endDate, zone),
                Description: e.Description,
                Location: e.Location,
                IsAllDay: true);
        }

        return null;
    }

    private static DateTimeOffset ToLocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
```

- [ ] **Step 4: Tests laufen lassen — alle grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~GoogleEventMapperTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Calendar/Google/GoogleEventMapper.cs \
        src/Backend.Tests/Features/Calendar/Google/GoogleEventMapperTests.cs
git commit -m "Plan G Task 4b: GoogleEventMapper unterstützt All-Day-Events (Date + DST)"
```

---

## Task 4c: `GoogleCalendarProvider` nutzt zentralen `TimeZoneInfo` + schreibt All-Day

**Files:**
- Modify: `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs`

Der Provider bekommt jetzt den zentralen `TimeZoneInfo` aus DI (statt `TimeZoneInfo.Utc`-Platzhalter aus Task 4a) und kann All-Day-Events schreiben.

- [ ] **Step 1: Konstruktor erweitern, Mapping mit echter TZ aufrufen, Create für All-Day**

Datei `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs` ersetzen durch:

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
    private readonly TimeZoneInfo _zone;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(
        GoogleAuthService auth,
        IOptions<CalendarOptions> options,
        TimeZoneInfo zone,
        ILogger<GoogleCalendarProvider> logger)
    {
        _auth = auth;
        _options = options.Value;
        _zone = zone;
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
            .Select(e => GoogleEventMapper.Map(e, _zone))
            .Where(e => e is not null)
            .Select(e => e!)
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
        };

        if (ev.IsAllDay)
        {
            googleEvent.Start = new EventDateTime { Date = ev.Start.ToString("yyyy-MM-dd") };
            googleEvent.End   = new EventDateTime { Date = ev.End.ToString("yyyy-MM-dd") };
        }
        else
        {
            googleEvent.Start = new EventDateTime { DateTimeDateTimeOffset = ev.Start };
            googleEvent.End   = new EventDateTime { DateTimeDateTimeOffset = ev.End };
        }

        var created = await service.Events.Insert(googleEvent, _options.GoogleCalendarId).ExecuteAsync(ct);
        _logger.LogInformation(
            "Google-Event {EventId} angelegt für '{Title}' am {Start} (AllDay={AllDay}).",
            created.Id, ev.Title, ev.Start, ev.IsAllDay);
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
}
```

- [ ] **Step 2: Build prüfen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded.

Der `GoogleCalendarProvider` wird in `Program.cs:47` als `AddSingleton<ICalendarProvider, GoogleCalendarProvider>` registriert. Der DI-Container löst den neuen `TimeZoneInfo`-Konstruktor-Parameter automatisch über die bestehende Singleton-Registrierung (Program.cs:29) auf — kein expliziter Eingriff nötig.

- [ ] **Step 3: Alle Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alles grün (Provider hat keine direkten Tests; das Mapping ist in Task 4b abgedeckt).

- [ ] **Step 4: Commit**

```bash
git add src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs
git commit -m "Plan G Task 4c: GoogleCalendarProvider nutzt TimeZoneInfo per DI + schreibt All-Day"
```

---

## Task 5: `CalendarContextBuilder` (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Calendar/CalendarContextBuilderTests.cs`
- Create: `src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs`

- [ ] **Step 1: Failing test schreiben**

Datei `src/Backend.Tests/Features/Calendar/CalendarContextBuilderTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Calendar;

public sealed class CalendarContextBuilderTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static TimeSnapshot SnapshotForMittwoch_21_5_2026_14h32()
    {
        var local = new DateTime(2026, 5, 21, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        return clock.Build();
    }

    private static CalendarContextBuilder BuildBuilder(FakeCalendarProvider provider) =>
        new(provider, Options.Create(new CalendarOptions { SearchHorizonDays = 14 }), Berlin);

    [Fact]
    public async Task BuildAsync_NoAllDayEvents_ReturnsEmptyString()
    {
        var provider = new FakeCalendarProvider();
        var builder = BuildBuilder(provider);

        var block = await builder.BuildAsync(SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_SingleDayAllDay_RendersOneDate()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e1", Title: "Urlaub",
            Start: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().Contain("[Längerfristiger Kontext");
        block.Should().Contain("- Mo 1.6.: Urlaub");
        block.Should().NotContain("–");
    }

    [Fact]
    public async Task BuildAsync_MultiDayAllDay_RendersRangeWithMinusOneDayConvention()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e1", Title: "Schulung Köln",
            Start: new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        // End=30.5. exklusiv → Anzeige-Ende 29.5.
        block.Should().Contain("- Mi 27.5.–Fr 29.5.: Schulung Köln");
    }

    [Fact]
    public async Task BuildAsync_PastAllDay_IsFilteredOut()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e-past", Title: "Gestern-Urlaub",
            Start: new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 21, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_RegularEvents_DoNotAppearInBlock()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e-meet", Title: "Meeting",
            Start: new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 27, 11, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: false));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        block.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_SortsAllDayByStartAscending()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(
            new CalendarEvent("e2", "Urlaub",
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.FromHours(2)),
                null, null, IsAllDay: true),
            new CalendarEvent("e1", "Schulung",
                new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)),
                null, null, IsAllDay: true));

        var block = await BuildBuilder(provider).BuildAsync(
            SnapshotForMittwoch_21_5_2026_14h32(), CancellationToken.None);

        var schulungIdx = block.IndexOf("Schulung", StringComparison.Ordinal);
        var urlaubIdx = block.IndexOf("Urlaub", StringComparison.Ordinal);
        schulungIdx.Should().BeGreaterThan(-1);
        urlaubIdx.Should().BeGreaterThan(-1);
        schulungIdx.Should().BeLessThan(urlaubIdx);
    }
}
```

- [ ] **Step 2: Tests laufen lassen — sollen wegen fehlender Klasse scheitern**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CalendarContextBuilderTests"`
Expected: Build error — `CalendarContextBuilder` existiert nicht.

- [ ] **Step 3: `CalendarContextBuilder` implementieren**

Datei `src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs`:

```csharp
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Features.Calendar.CalendarContext;

public sealed class CalendarContextBuilder
{
    private static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    private readonly ICalendarProvider _provider;
    private readonly CalendarOptions _options;
    private readonly TimeZoneInfo _zone;

    public CalendarContextBuilder(
        ICalendarProvider provider,
        IOptions<CalendarOptions> options,
        TimeZoneInfo zone)
    {
        _provider = provider;
        _options = options.Value;
        _zone = zone;
    }

    public async Task<string> BuildAsync(TimeSnapshot now, CancellationToken ct)
    {
        var startLocal = now.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(startLocal, _zone.GetUtcOffset(startLocal));
        var to = from.AddDays(_options.SearchHorizonDays);

        var events = await _provider.GetEventsAsync(from, to, ct);

        var allDay = events
            .Where(e => e.IsAllDay && e.End > now.NowLocal)
            .OrderBy(e => e.Start)
            .ToList();

        if (allDay.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"[Längerfristiger Kontext — All-Day-Termine im {_options.SearchHorizonDays}-Tage-Horizont]");
        foreach (var e in allDay)
        {
            sb.AppendLine($"- {FormatRange(e.Start, e.End)}: {e.Title}");
        }
        sb.AppendLine();
        sb.Append(
            "Diese Termine sind ganztägig und blockieren keinen Slot. Bevor du Vorschläge machst, " +
            "prüfe, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — und frage bei " +
            "Kollision nach.");
        return sb.ToString();
    }

    private static string FormatRange(DateTimeOffset start, DateTimeOffset endExclusive)
    {
        var startDate = DateOnly.FromDateTime(start.DateTime);
        var lastDate = DateOnly.FromDateTime(endExclusive.AddDays(-1).DateTime);

        if (startDate == lastDate)
        {
            return $"{ShortDay(startDate.DayOfWeek)} {startDate.Day}.{startDate.Month}.";
        }

        return $"{ShortDay(startDate.DayOfWeek)} {startDate.Day}.{startDate.Month}." +
               $"–{ShortDay(lastDate.DayOfWeek)} {lastDate.Day}.{lastDate.Month}.";
    }

    private static string ShortDay(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Di",
        DayOfWeek.Wednesday => "Mi",
        DayOfWeek.Thursday => "Do",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        DayOfWeek.Sunday => "So",
        _ => "?",
    };
}
```

- [ ] **Step 4: Tests laufen lassen — alle grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CalendarContextBuilderTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Calendar/CalendarContext/CalendarContextBuilder.cs \
        src/Backend.Tests/Features/Calendar/CalendarContextBuilderTests.cs
git commit -m "Plan G Task 5: CalendarContextBuilder rendert All-Day-Markdown-Block"
```

---

## Task 6: `AgentRunner` hängt All-Day-Block an + DI-Wiring (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Agent/AgentRunnerCalendarContextTests.cs`
- Modify: `src/Backend/Features/Agent/AgentRunner.cs`
- Modify: `src/Backend/Program.cs`

- [ ] **Step 1: Failing test schreiben**

Datei `src/Backend.Tests/Features/Agent/AgentRunnerCalendarContextTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentRunnerCalendarContextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static (AgentRunner runner, FakeLlmClient llm) Build(FakeCalendarProvider provider)
    {
        var local = new DateTime(2026, 5, 21, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        var builder = new CalendarContextBuilder(
            provider, Options.Create(new CalendarOptions { SearchHorizonDays = 14 }), Berlin);

        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk("Ok."));

        var runner = new AgentRunner(
            llm,
            tools: Array.Empty<NauAssist.Backend.Features.Agent.ITool>(),
            options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            logger: NullLogger<AgentRunner>.Instance,
            clockContext: clock,
            calendarContext: builder);

        return (runner, llm);
    }

    [Fact]
    public async Task HandleAsync_NoAllDayEvents_PrependsOnlyTimeContext()
    {
        var (runner, llm) = Build(new FakeCalendarProvider());
        var history = new[] { new LlmMessage("user", "Hi") };

        await foreach (var _ in runner.HandleAsync(history, default)) { }

        var msgs = llm.CapturedCalls[0].Messages;
        msgs[0].Role.Should().Be("system");
        msgs[0].Content.Should().Contain("[Zeit-Kontext");
        msgs[1].Role.Should().Be("user");
    }

    [Fact]
    public async Task HandleAsync_WithAllDayEvent_AppendsSecondSystemMessage()
    {
        var provider = new FakeCalendarProvider();
        provider.Seed(new CalendarEvent(
            Id: "e1", Title: "Schulung Köln",
            Start: new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.FromHours(2)),
            End:   new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.FromHours(2)),
            Description: null, Location: null, IsAllDay: true));

        var (runner, llm) = Build(provider);
        var history = new[] { new LlmMessage("user", "Was steht nächste Woche an?") };

        await foreach (var _ in runner.HandleAsync(history, default)) { }

        var msgs = llm.CapturedCalls[0].Messages;
        msgs[0].Role.Should().Be("system");
        msgs[0].Content.Should().Contain("[Zeit-Kontext");
        msgs[1].Role.Should().Be("system");
        msgs[1].Content.Should().Contain("Schulung Köln");
        msgs[1].Content.Should().Contain("Mi 27.5.–Fr 29.5.");
        msgs[2].Role.Should().Be("user");
    }
}
```

- [ ] **Step 2: Test laufen lassen — soll wegen Konstruktor-Signatur scheitern**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AgentRunnerCalendarContextTests"`
Expected: Build error — `AgentRunner`-Konstruktor hat keinen `calendarContext`-Parameter.

- [ ] **Step 3: `AgentRunner` erweitern**

In `src/Backend/Features/Agent/AgentRunner.cs`:

(a) `using` ergänzen (zu den bestehenden):

```csharp
using NauAssist.Backend.Features.Calendar.CalendarContext;
```

(b) Feld + Konstruktor-Parameter ergänzen — Z.17 (Felder) und Z.19–31 (Konstruktor):

Alt:
```csharp
private readonly ClockContext _clockContext;

public AgentRunner(
    ILlmClient llm,
    IEnumerable<ITool> tools,
    IOptions<AgentOptions> options,
    ILogger<AgentRunner> logger,
    ClockContext clockContext)
{
    _llm = llm;
    _tools = tools.ToDictionary(t => t.Name);
    _options = options.Value;
    _logger = logger;
    _clockContext = clockContext;
}
```

Neu:
```csharp
private readonly ClockContext _clockContext;
private readonly CalendarContextBuilder _calendarContext;

public AgentRunner(
    ILlmClient llm,
    IEnumerable<ITool> tools,
    IOptions<AgentOptions> options,
    ILogger<AgentRunner> logger,
    ClockContext clockContext,
    CalendarContextBuilder calendarContext)
{
    _llm = llm;
    _tools = tools.ToDictionary(t => t.Name);
    _options = options.Value;
    _logger = logger;
    _clockContext = clockContext;
    _calendarContext = calendarContext;
}
```

(c) In `HandleAsync` direkt nach dem bestehenden `conversation`-Setup (Z.38–40) den All-Day-Block einschieben.

Alt:
```csharp
var snapshot = _clockContext.Build();
var conversation = new List<LlmMessage> { new LlmMessage("system", BuildTimeContextBlock(snapshot)) };
conversation.AddRange(history);
```

Neu:
```csharp
var snapshot = _clockContext.Build();
var conversation = new List<LlmMessage> { new LlmMessage("system", BuildTimeContextBlock(snapshot)) };

var calendarBlock = await _calendarContext.BuildAsync(snapshot, ct);
if (!string.IsNullOrWhiteSpace(calendarBlock))
{
    conversation.Add(new LlmMessage("system", calendarBlock));
}

conversation.AddRange(history);
```

- [ ] **Step 4: `Program.cs` — `CalendarContextBuilder` registrieren**

In `src/Backend/Program.cs` direkt nach der `FreeSlotCalculator`-Singleton-Registrierung (Z.48–56) eine Zeile ergänzen:

```csharp
builder.Services.AddScoped<CalendarContextBuilder>();
```

`using` ergänzen, falls noch nicht vorhanden:
```csharp
using NauAssist.Backend.Features.Calendar.CalendarContext;
```

- [ ] **Step 5: Build + Tests laufen lassen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded.

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alles grün, inkl. der zwei neuen `AgentRunnerCalendarContextTests` und der unveränderten `AgentRunnerTimeContextTests` (letztere brauchen evtl. einen `calendarContext`-Param — siehe nächster Step).

- [ ] **Step 6: `AgentRunnerTimeContextTests` an neue Signatur anpassen**

Falls `AgentRunnerTimeContextTests.cs` jetzt nicht mehr kompiliert: dort den `AgentRunner`-Konstruktor-Aufruf ergänzen.

In `src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs` den `runner`-Build-Block erweitern:

Alt (vermutlich Z.560–565):
```csharp
var runner = new AgentRunner(
    llm,
    tools: Array.Empty<NauAssist.Backend.Features.Agent.ITool>(),
    options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
    logger: NullLogger<AgentRunner>.Instance,
    clockContext: clock);
```

Neu:
```csharp
var provider = new FakeCalendarProvider();
var calendarContext = new CalendarContextBuilder(
    provider,
    Options.Create(new CalendarOptions { SearchHorizonDays = 14 }),
    Berlin);

var runner = new AgentRunner(
    llm,
    tools: Array.Empty<NauAssist.Backend.Features.Agent.ITool>(),
    options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
    logger: NullLogger<AgentRunner>.Instance,
    clockContext: clock,
    calendarContext: calendarContext);
```

`using`-Statements ggf. ergänzen:
```csharp
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
```

Falls `AgentRunnerTests.cs` (der größere Integrationstest) ebenfalls den Konstruktor direkt aufruft, analog ergänzen.

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alles grün.

- [ ] **Step 7: Commit**

```bash
git add src/Backend/Features/Agent/AgentRunner.cs \
        src/Backend/Program.cs \
        src/Backend.Tests/Features/Agent/AgentRunnerCalendarContextTests.cs \
        src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs \
        src/Backend.Tests/Features/Agent/AgentRunnerTests.cs
git commit -m "Plan G Task 6: AgentRunner injiziert All-Day-Block + DI-Wiring"
```

(Falls `AgentRunnerTests.cs` nicht geändert wurde, weil sie über die DI-Factory laufen, einfach weglassen.)

---

## Task 7: `CreateEventHandler` reicht `IsAllDay` durch (TDD)

**Files:**
- Modify: `src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs`
- Modify: `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs`

- [ ] **Step 1: Failing test ergänzen**

In `src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs` vor der `private static CreateEventHandler BuildHandler(...)`-Methode anhängen:

```csharp
[Fact]
public async Task Handle_AllDayRequest_ForwardsIsAllDayToProvider()
{
    using var db = new TempSqliteDb();
    var provider = new FakeCalendarProvider();
    var audit = new AuditLogRepository(db.AppDb);
    var handler = BuildHandler(provider, audit);

    await handler.Handle(new CreateEventRequest(
        Title: "Urlaub",
        Start: DateTimeOffset.Parse("2026-06-01T00:00:00+02:00"),
        End:   DateTimeOffset.Parse("2026-06-08T00:00:00+02:00"),
        Description: null,
        Location: null,
        IsAllDay: true), CancellationToken.None);

    provider.CreatedEvents.Should().HaveCount(1);
    provider.CreatedEvents[0].IsAllDay.Should().BeTrue();
    provider.CreatedEvents[0].Title.Should().Be("Urlaub");
}
```

- [ ] **Step 2: Test laufen lassen — soll fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CreateEventHandlerTests"`
Expected: Der neue Test schlägt fehl — `CreatedEvents[0].IsAllDay` ist `false`, weil der Handler das Flag noch nicht durchreicht.

- [ ] **Step 3: Handler anpassen**

In `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs` die `NewEvent`-Konstruktion (Z.44–49) ersetzen.

Alt:
```csharp
var newEvent = new NewEvent(
    Title: request.Title.Trim(),
    Start: request.Start,
    End: request.End,
    Description: request.Description,
    Location: request.Location);
```

Neu:
```csharp
var newEvent = new NewEvent(
    Title: request.Title.Trim(),
    Start: request.Start,
    End: request.End,
    Description: request.Description,
    Location: request.Location,
    IsAllDay: request.IsAllDay);
```

- [ ] **Step 4: Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CreateEventHandlerTests"`
Expected: 5 passed (4 bestehende + 1 neue).

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs \
        src/Backend.Tests/Features/Calendar/CreateEventHandlerTests.cs
git commit -m "Plan G Task 7: CreateEventHandler reicht IsAllDay an NewEvent durch"
```

---

## Task 8: `CreateEventTool` lernt `is_all_day` (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Agent/Tools/CreateEventToolTests.cs`
- Modify: `src/Backend/Features/Agent/Tools/CreateEventTool.cs`

Das Tool bekommt einen optionalen `is_all_day`-Bool. Bei `true` werden `start`/`end` als `yyyy-MM-dd` geparst und in lokaler TZ zu 00:00 aufgelöst. Dafür braucht das Tool eine `TimeZoneInfo`-Dependency aus DI (DI-Container löst das automatisch über die in `Program.cs:29` registrierte Singleton-Registrierung).

- [ ] **Step 1: Failing tests schreiben**

Datei `src/Backend.Tests/Features/Agent/Tools/CreateEventToolTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class CreateEventToolTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Execute_RegularEvent_PassesDateTimeOffsetThrough()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<CreateEventRequest, CreateEventResponse>(
            new CreateEventResponse("fake-1"));

        var tool = new CreateEventTool(mediator, Berlin);

        var args = JsonDocument.Parse("""
            {
              "title": "Meeting",
              "start": "2026-05-27T10:00:00+02:00",
              "end":   "2026-05-27T11:00:00+02:00"
            }
            """).RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        var sent = mediator.SentRequests.OfType<CreateEventRequest>().Last();
        sent.IsAllDay.Should().BeFalse();
        sent.Start.Should().Be(DateTimeOffset.Parse("2026-05-27T10:00:00+02:00"));
        sent.End.Should().Be(DateTimeOffset.Parse("2026-05-27T11:00:00+02:00"));
    }

    [Fact]
    public async Task Execute_AllDay_ParsesDateAndUsesLocalMidnight()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<CreateEventRequest, CreateEventResponse>(
            new CreateEventResponse("fake-1"));

        var tool = new CreateEventTool(mediator, Berlin);

        var args = JsonDocument.Parse("""
            {
              "title": "Urlaub",
              "start": "2026-06-01",
              "end":   "2026-06-08",
              "is_all_day": true
            }
            """).RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        var sent = mediator.SentRequests.OfType<CreateEventRequest>().Last();
        sent.IsAllDay.Should().BeTrue();
        sent.Start.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        sent.End.Should().Be(new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.FromHours(2)));
    }
}
```

- [ ] **Step 2: Tests laufen lassen — sollen fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CreateEventToolTests"`
Expected: Build error — `CreateEventTool`-Konstruktor hat keinen `TimeZoneInfo`-Parameter; `is_all_day` wird ignoriert.

- [ ] **Step 3: `CreateEventTool` umbauen**

Datei `src/Backend/Features/Agent/Tools/CreateEventTool.cs` ersetzen durch:

```csharp
using System.Globalization;
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.CreateEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class CreateEventTool : ITool
{
    public string Name => "create_event";
    public string Description =>
        "Legt einen neuen Termin im Kalender an, nachdem der User bestätigt hat. " +
        "Setze is_all_day=true für ganztägige Einträge (Urlaub, Schulung); dann müssen " +
        "start und end im Format yyyy-MM-dd angegeben werden und end ist exklusiv " +
        "(1-Tages-Urlaub am 1.6. → start=2026-06-01, end=2026-06-02).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Titel des Termins" },
            "start": { "type": "string" },
            "end":   { "type": "string" },
            "description": { "type": ["string", "null"] },
            "location":    { "type": ["string", "null"] },
            "is_all_day":  { "type": "boolean", "default": false }
          },
          "required": ["title", "start", "end"]
        }
        """).RootElement;

    private readonly IMediator _mediator;
    private readonly TimeZoneInfo _zone;

    public CreateEventTool(IMediator mediator, TimeZoneInfo zone)
    {
        _mediator = mediator;
        _zone = zone;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var isAllDay =
            args.TryGetProperty("is_all_day", out var allDayEl) &&
            allDayEl.ValueKind == JsonValueKind.True;

        var startRaw = args.GetProperty("start").GetString()!;
        var endRaw = args.GetProperty("end").GetString()!;

        DateTimeOffset start, end;
        if (isAllDay)
        {
            start = ParseDateOnly(startRaw);
            end = ParseDateOnly(endRaw);
        }
        else
        {
            start = DateTimeOffset.Parse(startRaw);
            end = DateTimeOffset.Parse(endRaw);
        }

        var request = new CreateEventRequest(
            Title: args.GetProperty("title").GetString()!,
            Start: start,
            End: end,
            Description: args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null,
            Location: args.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.String ? locEl.GetString() : null,
            IsAllDay: isAllDay);

        var response = await _mediator.Send(request, ct);
        return JsonSerializer.SerializeToElement(new { event_id = response.EventId, status = "created" });
    }

    private DateTimeOffset ParseDateOnly(string raw)
    {
        var date = DateOnly.ParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }
}
```

- [ ] **Step 4: Tests laufen lassen — grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~CreateEventToolTests"`
Expected: 2 passed.

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded — DI löst `TimeZoneInfo`-Param automatisch über die Singleton-Registrierung in `Program.cs:29` auf.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Agent/Tools/CreateEventTool.cs \
        src/Backend.Tests/Features/Agent/Tools/CreateEventToolTests.cs
git commit -m "Plan G Task 8: CreateEventTool akzeptiert is_all_day mit yyyy-MM-dd"
```

---

## Task 9: `GetCalendarRangeTool` zeigt `is_all_day` im DTO

**Files:**
- Modify: `src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs`

Damit Nau bei expliziten `get_calendar_range`-Calls konsistente Info zum Kontext-Block bekommt.

- [ ] **Step 1: Tool-Serialisierung erweitern**

In `src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs` Z.36–48 ersetzen.

Alt:
```csharp
var resultObj = new
{
    events = response.Events.Select(e => new
    {
        id = e.Id,
        title = e.Title,
        start = e.Start.ToString("O"),
        end = e.End.ToString("O"),
        description = e.Description,
        location = e.Location,
    }),
};
```

Neu:
```csharp
var resultObj = new
{
    events = response.Events.Select(e => new
    {
        id = e.Id,
        title = e.Title,
        start = e.Start.ToString("O"),
        end = e.End.ToString("O"),
        description = e.Description,
        location = e.Location,
        is_all_day = e.IsAllDay,
    }),
};
```

- [ ] **Step 2: Build + Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alles grün.

- [ ] **Step 3: Commit**

```bash
git add src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs
git commit -m "Plan G Task 9: GetCalendarRangeTool exponiert is_all_day im DTO"
```

---

## Task 10: System-Prompt-Hinweis in `appsettings.json`

**Files:**
- Modify: `src/Backend/appsettings.json`

- [ ] **Step 1: `Ollama.SystemPrompt` erweitern**

In `src/Backend/appsettings.json` den vorhandenen `SystemPrompt`-String öffnen und am Ende den folgenden Satz anhängen (ein Leerzeichen davor):

```
Wenn ein Block 'Längerfristiger Kontext — All-Day-Termine' erscheint, sind das ganztägige Einträge (Urlaub, Schulung, Reise) im Lookahead. Sie blockieren keinen Slot, aber prüfe vor Vorschlägen, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — frage bei Kollision aktiv nach. Zum Anlegen ganztägiger Einträge nutze create_event mit is_all_day=true und start/end im Format yyyy-MM-dd (end exklusiv).
```

- [ ] **Step 2: Backend startet sauber**

Run: `dotnet run --project src/Backend/Backend.csproj`
Expected: kein Konfig-Fehler in den Startup-Logs (JSON valide). Mit Ctrl+C beenden.

- [ ] **Step 3: Commit**

```bash
git add src/Backend/appsettings.json
git commit -m "Plan G Task 10: System-Prompt erweitert um All-Day-Kontext-Hinweis"
```

---

## Task 11: Manueller Smoke-Test

**Files:**
- (keine)

End-to-end-Verifikation gegen den echten Google-Kalender. Setup-Bedingung: ein 3-Tages-All-Day "Schulung Köln" 27.–29.5.2026 + ein 1-Tages-All-Day "Urlaub" am 1.6.2026 im verknüpften Google-Kalender (manuell anlegen, falls nicht vorhanden).

- [ ] **Step 1: Backend starten**

Run: `dotnet run --project src/Backend/Backend.csproj`
Expected: Backend lauscht (`http://localhost:5182`).

- [ ] **Step 2: Frontend starten (separates Terminal)**

Run: `cd frontend && npm run dev`
Expected: Vite-Dev-Server (`http://localhost:5173`).

- [ ] **Step 3: Awareness prüfen**

Chat: "Hast du nächste Woche schon was geplant?"
Expected: Nau erwähnt die Schulung (Mi–Fr) und den Urlaub am 1.6.

- [ ] **Step 4: Slot-Vorschlag mit Kollision prüfen**

Chat: "Kannst du Do 14 Uhr ein 60-Min-Slot vorschlagen?"
Expected: Nau warnt aktiv vor der Schulung, schlägt aber dennoch den Slot vor (kein hartes Blocking).

- [ ] **Step 5: All-Day Create prüfen**

Chat: "Trage mir Urlaub vom 8. bis 12.6. ein."
Expected:
- Backend-Log zeigt `Tool-Call create_event args=...is_all_day:true,start:2026-06-08,end:2026-06-13...`.
- Im Google-Kalender erscheint der Eintrag als ganztägig.
- `sqlite3 src/Backend/data/nauassist.db "SELECT tool_args_json FROM audit_log ORDER BY id DESC LIMIT 1;"` enthält `is_all_day":true`.

- [ ] **Step 6: Backend stoppen**

Ctrl+C im Backend-Terminal.

- [ ] **Step 7: (Kein Commit — reine Verifikation)**

---

## Self-Review-Checkliste (für den ausführenden Agent)

Vor Abschluss prüfen:

- [ ] `dotnet test src/Backend.Tests/Backend.Tests.csproj` zeigt 100% grün.
- [ ] Im Logfile beim Chat-Start tauchen zwei (oder bei leerem Kalender eine) `system`-Messages vor der User-Message auf.
- [ ] `audit_log` enthält für All-Day-Create-Aufrufe `is_all_day":true` im `tool_args_json`.
- [ ] Smoke-Test zeigt: Nau erwähnt Schulung und Urlaub von selbst; Slots am Schulungs-Tag werden noch vorgeschlagen, aber mit Warnhinweis; All-Day-Create funktioniert.
- [ ] Keine direkten `TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")`-Calls außerhalb von `Program.cs` und Test-Helpers.
