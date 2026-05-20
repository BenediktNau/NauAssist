# Plan F: Agent Time Awareness — Implementierungsplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dem NauAssist-Agent zuverlässige Zeit- und Datumsinfos geben — über einen pro Turn injizierten Zeit-Kontextblock plus ein `get_current_time`-Tool, beide aus einer Single Source of Truth (`ClockContext`).

**Architecture:** Neuer Service `ClockContext` in `Features/Infrastructure/Time/` baut deterministisch einen `TimeSnapshot` (jetzt, heute, morgen, diese/nächste Woche, dieses/nächstes Wochenende) in `Europe/Berlin`. `AgentRunner` prepended am Start jedes Turns eine zweite `system`-Message mit dem Snapshot als Markdown-Block. Ein neues `GetCurrentTimeTool` gibt denselben Snapshot als JSON zurück. `TimeOptions` ersetzt die hardcoded TimeZone-Strings in `Program.cs`.

**Tech Stack:** .NET 10, xUnit + FluentAssertions, Ollama-LLM-Client (gemma4), Mediator-basierte Tools.

**Referenz-Spec:** `docs/superpowers/specs/2026-05-20-agent-time-awareness-design.md`

---

## File Structure

**Neu erstellt:**
- `src/Backend/Features/Infrastructure/Time/TimeOptions.cs` — Konfig-Objekt (`Zone`)
- `src/Backend/Features/Infrastructure/Time/DateRange.cs` — immutables Record (Start, End)
- `src/Backend/Features/Infrastructure/Time/TimeSnapshot.cs` — immutables Record (alle Snapshot-Felder)
- `src/Backend/Features/Infrastructure/Time/ClockContext.cs` — Builder-Service (Clock + Zone → TimeSnapshot)
- `src/Backend/Features/Agent/Tools/GetCurrentTimeTool.cs` — ITool, gibt Snapshot als JSON

**Modifiziert:**
- `src/Backend/Features/Agent/AgentRunner.cs` — Konstruktor + Build-Methode für Zeit-Kontext-System-Message; prepended am Start von `HandleAsync`
- `src/Backend/Program.cs` — `TimeOptions` registrieren; `ClockContext` registrieren; `GetCurrentTimeTool` registrieren; zwei `FindSystemTimeZoneById`-Calls auf zentralen `TimeZoneInfo` umstellen
- `src/Backend/appsettings.json` — Section `Time`, plus Ergänzungssatz im `Ollama.SystemPrompt`

**Tests neu:**
- `src/Backend.Tests/Features/Infrastructure/Time/ClockContextTests.cs`
- `src/Backend.Tests/Features/Agent/Tools/GetCurrentTimeToolTests.cs`
- `src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs`

---

## Task 1: TimeOptions + DateRange + TimeSnapshot (Datentypen)

**Files:**
- Create: `src/Backend/Features/Infrastructure/Time/TimeOptions.cs`
- Create: `src/Backend/Features/Infrastructure/Time/DateRange.cs`
- Create: `src/Backend/Features/Infrastructure/Time/TimeSnapshot.cs`

Diese Task hat keine Logik — nur Daten-Records. Tests folgen in Task 2 zusammen mit `ClockContext`.

- [ ] **Step 1: `TimeOptions` schreiben**

Datei `src/Backend/Features/Infrastructure/Time/TimeOptions.cs`:

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Time;

public sealed class TimeOptions
{
    public string Zone { get; set; } = "Europe/Berlin";
}
```

- [ ] **Step 2: `DateRange` schreiben**

Datei `src/Backend/Features/Infrastructure/Time/DateRange.cs`:

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Time;

public sealed record DateRange(DateOnly Start, DateOnly End);
```

- [ ] **Step 3: `TimeSnapshot` schreiben**

Datei `src/Backend/Features/Infrastructure/Time/TimeSnapshot.cs`:

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Time;

public sealed record TimeSnapshot(
    DateTimeOffset NowUtc,
    DateTimeOffset NowLocal,
    string Timezone,
    DateOnly Today,
    DateOnly Tomorrow,
    string WeekdayDe,
    int IsoWeek,
    DateRange ThisWeek,
    DateRange NextWeek,
    DateRange ThisWeekend,
    DateRange NextWeekend);
```

- [ ] **Step 4: Build prüfen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Infrastructure/Time/TimeOptions.cs \
        src/Backend/Features/Infrastructure/Time/DateRange.cs \
        src/Backend/Features/Infrastructure/Time/TimeSnapshot.cs
git commit -m "Plan F Task 1: TimeOptions + DateRange + TimeSnapshot"
```

---

## Task 2: ClockContext — Standardfall Mittwoch (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Infrastructure/Time/ClockContextTests.cs`
- Create: `src/Backend/Features/Infrastructure/Time/ClockContext.cs`

- [ ] **Step 1: Failing test für Mittwoch-Standardfall schreiben**

Datei `src/Backend.Tests/Features/Infrastructure/Time/ClockContextTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Tests.Features.Infrastructure.Time;

public sealed class ClockContextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static ClockContext BuildClockAt(int year, int month, int day, int hour, int minute, TimeZoneInfo zone)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        var nowLocal = new DateTimeOffset(local, offset);
        return new ClockContext(() => nowLocal.ToUniversalTime(), zone);
    }

    [Fact]
    public void Build_Mittwoch_14h32_StandardSnapshot()
    {
        // 2026-05-20 ist ein Mittwoch, KW 21.
        var clock = BuildClockAt(2026, 5, 20, 14, 32, Berlin);

        var snap = clock.Build();

        snap.Timezone.Should().Be("Europe/Berlin");
        snap.Today.Should().Be(new DateOnly(2026, 5, 20));
        snap.Tomorrow.Should().Be(new DateOnly(2026, 5, 21));
        snap.WeekdayDe.Should().Be("Mittwoch");
        snap.IsoWeek.Should().Be(21);
        snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 24)));
        snap.NextWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 31)));
        snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24)));
        snap.NextWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31)));
    }
}
```

- [ ] **Step 2: Test laufen lassen — soll wegen fehlender Klasse fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~ClockContextTests"`
Expected: Build error — `ClockContext` existiert nicht.

- [ ] **Step 3: `ClockContext` implementieren**

Datei `src/Backend/Features/Infrastructure/Time/ClockContext.cs`:

```csharp
using System.Globalization;

namespace NauAssist.Backend.Features.Infrastructure.Time;

public sealed class ClockContext
{
    private static readonly string[] WeekdaysDe =
    {
        "Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag",
    };

    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;

    public ClockContext(Func<DateTimeOffset> clock, TimeZoneInfo zone)
    {
        _clock = clock;
        _zone = zone;
    }

    public TimeSnapshot Build()
    {
        var nowUtc = _clock().ToUniversalTime();
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, _zone);

        var today = DateOnly.FromDateTime(nowLocal.DateTime);
        var tomorrow = today.AddDays(1);

        var weekday = WeekdaysDe[(int)today.DayOfWeek];

        var isoWeek = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));

        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var thisSunday = thisMonday.AddDays(6);
        var nextMonday = thisMonday.AddDays(7);
        var nextSunday = thisSunday.AddDays(7);

        var (thisWeSat, thisWeSun, nextWeSat, nextWeSun) = ComputeWeekends(today);

        return new TimeSnapshot(
            NowUtc: nowUtc,
            NowLocal: nowLocal,
            Timezone: _zone.Id,
            Today: today,
            Tomorrow: tomorrow,
            WeekdayDe: weekday,
            IsoWeek: isoWeek,
            ThisWeek: new DateRange(thisMonday, thisSunday),
            NextWeek: new DateRange(nextMonday, nextSunday),
            ThisWeekend: new DateRange(thisWeSat, thisWeSun),
            NextWeekend: new DateRange(nextWeSat, nextWeSun));
    }

    private static (DateOnly thisSat, DateOnly thisSun, DateOnly nextSat, DateOnly nextSun) ComputeWeekends(DateOnly today)
    {
        switch (today.DayOfWeek)
        {
            case DayOfWeek.Saturday:
            {
                var sat = today;
                var sun = today.AddDays(1);
                return (sat, sun, sat.AddDays(7), sun.AddDays(7));
            }
            case DayOfWeek.Sunday:
            {
                var sat = today.AddDays(-1);
                var sun = today;
                return (sat, sun, today.AddDays(6), today.AddDays(7));
            }
            default:
            {
                var daysToSat = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
                var sat = today.AddDays(daysToSat);
                var sun = sat.AddDays(1);
                return (sat, sun, sat.AddDays(7), sun.AddDays(7));
            }
        }
    }
}
```

- [ ] **Step 4: Test laufen lassen — soll grün sein**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~ClockContextTests"`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Infrastructure/Time/ClockContext.cs \
        src/Backend.Tests/Features/Infrastructure/Time/ClockContextTests.cs
git commit -m "Plan F Task 2: ClockContext (Standardfall Mittwoch, TDD)"
```

---

## Task 3: ClockContext — Wochenend-Edge-Cases (Sa, So) + DST + UTC-Tageswechsel

**Files:**
- Modify: `src/Backend.Tests/Features/Infrastructure/Time/ClockContextTests.cs`

Tests für die heiklen Fälle. Die `ClockContext`-Implementierung aus Task 2 sollte bereits alle abdecken — falls nicht, hier nachschärfen.

- [ ] **Step 1: Test für Samstag-Fall ergänzen**

In `ClockContextTests.cs` als weitere `[Fact]`-Methode hinzufügen:

```csharp
[Fact]
public void Build_Samstag_thisWeekendIstHeuteUndMorgen()
{
    // 2026-05-23 ist ein Samstag.
    var clock = BuildClockAt(2026, 5, 23, 10, 0, Berlin);

    var snap = clock.Build();

    snap.WeekdayDe.Should().Be("Samstag");
    snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24)));
    snap.NextWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31)));
    // Sa gehört noch zur laufenden Mo–So-KW.
    snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 24)));
}
```

- [ ] **Step 2: Test für Sonntag-Fall ergänzen**

```csharp
[Fact]
public void Build_Sonntag_thisWeekEndetHeute_thisWeekendIstGesternUndHeute()
{
    // 2026-05-24 ist ein Sonntag.
    var clock = BuildClockAt(2026, 5, 24, 10, 0, Berlin);

    var snap = clock.Build();

    snap.WeekdayDe.Should().Be("Sonntag");
    snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 18), new DateOnly(2026, 5, 24)));
    snap.NextWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 31)));
    snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24)));
    snap.NextWeekend.Should().Be(new DateRange(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31)));
}
```

- [ ] **Step 3: Test für UTC-Tageswechsel (lokal schon Montag, UTC noch Sonntag) ergänzen**

```csharp
[Fact]
public void Build_Sonntag_23h30_UTC_Sommerzeit_LokalIstMontag_TodayIstMontag()
{
    // Sommerzeit Europe/Berlin = UTC+2. Sonntag 23:30 UTC → Montag 01:30 Berlin.
    var nowUtc = new DateTimeOffset(2026, 5, 24, 23, 30, 0, TimeSpan.Zero);
    var clock = new ClockContext(() => nowUtc, Berlin);

    var snap = clock.Build();

    snap.NowLocal.Year.Should().Be(2026);
    snap.NowLocal.Month.Should().Be(5);
    snap.NowLocal.Day.Should().Be(25);
    snap.NowLocal.Hour.Should().Be(1);
    snap.Today.Should().Be(new DateOnly(2026, 5, 25));
    snap.WeekdayDe.Should().Be("Montag");
    // Lokal Mo 25.05. — diese Woche beginnt heute.
    snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 31)));
}
```

- [ ] **Step 4: Test für DST-Frühjahrsübergang ergänzen**

```csharp
[Fact]
public void Build_DST_Fruehjahrsuebergang_KeinOffByOne()
{
    // 2026: letzter Sonntag im März = 29.03.2026 (DST start).
    // Wir testen Montag 30.03., damit die DST-Stunde sicher hinter uns liegt.
    var clock = BuildClockAt(2026, 3, 30, 10, 0, Berlin);

    var snap = clock.Build();

    snap.Today.Should().Be(new DateOnly(2026, 3, 30));
    snap.WeekdayDe.Should().Be("Montag");
    snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 5)));
    snap.ThisWeekend.Should().Be(new DateRange(new DateOnly(2026, 4, 4), new DateOnly(2026, 4, 5)));
}
```

- [ ] **Step 5: Test für DST-Herbstübergang ergänzen**

```csharp
[Fact]
public void Build_DST_Herbstuebergang_KeinOffByOne()
{
    // 2026: letzter Sonntag im Oktober = 25.10.2026 (DST end).
    var clock = BuildClockAt(2026, 10, 26, 10, 0, Berlin);

    var snap = clock.Build();

    snap.Today.Should().Be(new DateOnly(2026, 10, 26));
    snap.WeekdayDe.Should().Be("Montag");
    snap.ThisWeek.Should().Be(new DateRange(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1)));
}
```

- [ ] **Step 6: Alle Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~ClockContextTests"`
Expected: 6 passed.

Falls Tests fehlschlagen, in `ClockContext.ComputeWeekends`/`Build` nachschärfen, dann erneut laufen lassen.

- [ ] **Step 7: Commit**

```bash
git add src/Backend.Tests/Features/Infrastructure/Time/ClockContextTests.cs
git commit -m "Plan F Task 3: ClockContext-Tests (Sa, So, UTC-Tageswechsel, DST)"
```

---

## Task 4: GetCurrentTimeTool (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Agent/Tools/GetCurrentTimeToolTests.cs`
- Create: `src/Backend/Features/Agent/Tools/GetCurrentTimeTool.cs`

- [ ] **Step 1: Failing test schreiben**

Datei `src/Backend.Tests/Features/Agent/Tools/GetCurrentTimeToolTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class GetCurrentTimeToolTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task Execute_ReturnsAllSnapshotFields()
    {
        var local = new DateTime(2026, 5, 20, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        var tool = new GetCurrentTimeTool(clock);

        var result = await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, default);

        result.GetProperty("now").GetString().Should().Be("2026-05-20T14:32:00+02:00");
        result.GetProperty("timezone").GetString().Should().Be("Europe/Berlin");
        result.GetProperty("today").GetString().Should().Be("2026-05-20");
        result.GetProperty("tomorrow").GetString().Should().Be("2026-05-21");
        result.GetProperty("weekday").GetString().Should().Be("Mittwoch");
        result.GetProperty("iso_week").GetInt32().Should().Be(21);

        var thisWeek = result.GetProperty("this_week");
        thisWeek.GetProperty("start").GetString().Should().Be("2026-05-18");
        thisWeek.GetProperty("end").GetString().Should().Be("2026-05-24");

        var nextWeek = result.GetProperty("next_week");
        nextWeek.GetProperty("start").GetString().Should().Be("2026-05-25");
        nextWeek.GetProperty("end").GetString().Should().Be("2026-05-31");

        var thisWeekend = result.GetProperty("this_weekend");
        thisWeekend.GetProperty("start").GetString().Should().Be("2026-05-23");
        thisWeekend.GetProperty("end").GetString().Should().Be("2026-05-24");

        var nextWeekend = result.GetProperty("next_weekend");
        nextWeekend.GetProperty("start").GetString().Should().Be("2026-05-30");
        nextWeekend.GetProperty("end").GetString().Should().Be("2026-05-31");
    }

    [Fact]
    public void ToolMetadata_NameAndDescription()
    {
        var local = new DateTime(2026, 5, 20, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);
        var tool = new GetCurrentTimeTool(clock);

        tool.Name.Should().Be("get_current_time");
        tool.Description.Should().NotBeNullOrWhiteSpace();
        tool.ParameterSchema.GetProperty("type").GetString().Should().Be("object");
    }
}
```

- [ ] **Step 2: Test laufen lassen — soll fehlschlagen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~GetCurrentTimeToolTests"`
Expected: Build error — `GetCurrentTimeTool` existiert nicht.

- [ ] **Step 3: `GetCurrentTimeTool` implementieren**

Datei `src/Backend/Features/Agent/Tools/GetCurrentTimeTool.cs`:

```csharp
using System.Text.Json;
using NauAssist.Backend.Features.Infrastructure.Time;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class GetCurrentTimeTool : ITool
{
    public string Name => "get_current_time";
    public string Description =>
        "Liefert die aktuelle Zeit in Europe/Berlin plus die exakten Daten für heute, morgen, " +
        "diese/nächste Woche (Mo–So) und dieses/nächstes Wochenende (Sa–So). Aufrufen, wenn der " +
        "Zeit-Kontext-Block für die Anfrage nicht reicht (z. B. 'in drei Wochen am Donnerstag').";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    private readonly ClockContext _clock;

    public GetCurrentTimeTool(ClockContext clock)
    {
        _clock = clock;
    }

    public Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var s = _clock.Build();

        var resultObj = new
        {
            now = s.NowLocal.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            timezone = s.Timezone,
            today = s.Today.ToString("yyyy-MM-dd"),
            tomorrow = s.Tomorrow.ToString("yyyy-MM-dd"),
            weekday = s.WeekdayDe,
            iso_week = s.IsoWeek,
            this_week = new { start = s.ThisWeek.Start.ToString("yyyy-MM-dd"), end = s.ThisWeek.End.ToString("yyyy-MM-dd") },
            next_week = new { start = s.NextWeek.Start.ToString("yyyy-MM-dd"), end = s.NextWeek.End.ToString("yyyy-MM-dd") },
            this_weekend = new { start = s.ThisWeekend.Start.ToString("yyyy-MM-dd"), end = s.ThisWeekend.End.ToString("yyyy-MM-dd") },
            next_weekend = new { start = s.NextWeekend.Start.ToString("yyyy-MM-dd"), end = s.NextWeekend.End.ToString("yyyy-MM-dd") },
        };

        return Task.FromResult(JsonSerializer.SerializeToElement(resultObj));
    }
}
```

- [ ] **Step 4: Tests laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~GetCurrentTimeToolTests"`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Agent/Tools/GetCurrentTimeTool.cs \
        src/Backend.Tests/Features/Agent/Tools/GetCurrentTimeToolTests.cs
git commit -m "Plan F Task 4: GetCurrentTimeTool (Snapshot als JSON)"
```

---

## Task 5: AgentRunner injiziert Zeit-Kontext-System-Message (TDD)

**Files:**
- Create: `src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs`
- Modify: `src/Backend/Features/Agent/AgentRunner.cs`

- [ ] **Step 1: Failing test schreiben**

Datei `src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentRunnerTimeContextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public async Task HandleAsync_PrependsTimeContextSystemMessage_BeforeHistory()
    {
        var local = new DateTime(2026, 5, 20, 14, 32, 0, DateTimeKind.Unspecified);
        var nowLocal = new DateTimeOffset(local, Berlin.GetUtcOffset(local));
        var clock = new ClockContext(() => nowLocal.ToUniversalTime(), Berlin);

        var llm = new FakeLlmClient();
        llm.QueueResponse(new TextDeltaChunk("Alles klar."));

        var runner = new AgentRunner(
            llm,
            tools: Array.Empty<NauAssist.Backend.Features.Agent.ITool>(),
            options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            logger: NullLogger<AgentRunner>.Instance,
            clockContext: clock);

        var history = new[]
        {
            new LlmMessage("user", "Was steht nächste Woche an?"),
        };

        await foreach (var _ in runner.HandleAsync(history, default)) { }

        llm.CapturedCalls.Should().HaveCount(1);
        var msgs = llm.CapturedCalls[0].Messages;

        // Erste Message ist die Zeit-Kontext-System-Message, dann folgt die History.
        msgs[0].Role.Should().Be("system");
        msgs[0].Content.Should().Contain("[Zeit-Kontext");
        msgs[0].Content.Should().Contain("2026-05-20");
        msgs[0].Content.Should().Contain("Mittwoch");
        msgs[0].Content.Should().Contain("Nächste Woche:  2026-05-25 (Mo) bis 2026-05-31 (So)");
        msgs[0].Content.Should().Contain("Nächstes WE:    2026-05-30 (Sa) bis 2026-05-31 (So)");

        msgs[1].Role.Should().Be("user");
        msgs[1].Content.Should().Be("Was steht nächste Woche an?");
    }
}
```

- [ ] **Step 2: Test laufen lassen — soll wegen Konstruktor-Signatur scheitern**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AgentRunnerTimeContextTests"`
Expected: Build error — `AgentRunner`-Konstruktor hat noch keinen `ClockContext`-Parameter.

- [ ] **Step 3: `AgentRunner` anpassen — Konstruktor + Block-Builder + Prepend**

In `src/Backend/Features/Agent/AgentRunner.cs`:

(a) `using` ergänzen:

```csharp
using NauAssist.Backend.Features.Infrastructure.Time;
```

(b) Feld + Konstruktor erweitern (das `ClockContext`-Feld neben den existierenden):

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

(c) In `HandleAsync` direkt nach `var toolDefs = ...` und vor `var conversation = history.ToList();` die Konversation neu zusammenbauen:

Alte Zeile (entfernen):
```csharp
var conversation = history.ToList();
```

Neue Zeilen:
```csharp
var snapshot = _clockContext.Build();
var conversation = new List<LlmMessage> { new LlmMessage("system", BuildTimeContextBlock(snapshot)) };
conversation.AddRange(history);
```

(d) Am Ende der Klasse (vor der schließenden `}`) die private Methode hinzufügen:

```csharp
private static string BuildTimeContextBlock(TimeSnapshot s)
{
    static string ShortDay(DayOfWeek d) => d switch
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

    var nowIso = s.NowLocal.ToString("yyyy-MM-ddTHH:mm:sszzz");

    return
        $"[Zeit-Kontext — verbindlich, alle Daten in {s.Timezone}]\n" +
        $"Jetzt:          {nowIso} ({s.WeekdayDe}, KW {s.IsoWeek})\n" +
        $"Heute:          {s.Today:yyyy-MM-dd} ({ShortDay(s.Today.DayOfWeek)})\n" +
        $"Morgen:         {s.Tomorrow:yyyy-MM-dd} ({ShortDay(s.Tomorrow.DayOfWeek)})\n" +
        $"Diese Woche:    {s.ThisWeek.Start:yyyy-MM-dd} (Mo) bis {s.ThisWeek.End:yyyy-MM-dd} (So)\n" +
        $"Nächste Woche:  {s.NextWeek.Start:yyyy-MM-dd} (Mo) bis {s.NextWeek.End:yyyy-MM-dd} (So)\n" +
        $"Dieses WE:      {s.ThisWeekend.Start:yyyy-MM-dd} (Sa) bis {s.ThisWeekend.End:yyyy-MM-dd} (So)\n" +
        $"Nächstes WE:    {s.NextWeekend.Start:yyyy-MM-dd} (Sa) bis {s.NextWeekend.End:yyyy-MM-dd} (So)\n" +
        "\n" +
        "Wochenkonvention: Montag ist der erste Tag der Woche (ISO 8601).\n" +
        "\"Nächste Woche\" = Mo–So der KW nach der aktuellen.\n" +
        "\"Dieses Wochenende\" = der Sa+So in der aktuellen KW.\n" +
        "\"Nächstes Wochenende\" = der Sa+So in der nächsten KW.\n" +
        "Wenn heute Sa/So ist, ist \"dieses Wochenende\" das laufende; \"nächstes Wochenende\" ist 7 Tage später.";
}
```

- [ ] **Step 4: Test laufen lassen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "FullyQualifiedName~AgentRunnerTimeContextTests"`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Agent/AgentRunner.cs \
        src/Backend.Tests/Features/Agent/AgentRunnerTimeContextTests.cs
git commit -m "Plan F Task 5: AgentRunner prepended Zeit-Kontext-System-Message"
```

---

## Task 6: DI-Wiring in Program.cs + appsettings.json

**Files:**
- Modify: `src/Backend/Program.cs`
- Modify: `src/Backend/appsettings.json`

- [ ] **Step 1: `TimeOptions` als Section in `appsettings.json` ergänzen**

In `src/Backend/appsettings.json` vor der `"Calendar"`-Section (oder wo gerade Platz ist) einfügen:

```json
  "Time": {
    "Zone": "Europe/Berlin"
  },
```

Vollständiger Diff-Kontext: nach `"Persistence": { ... },` einfügen, vor `"Calendar": { ... }`.

- [ ] **Step 2: `Ollama.SystemPrompt` in `appsettings.json` um Zeit-Hinweis ergänzen**

In derselben Datei den `SystemPrompt`-String ersetzen:

Alt:
```json
"SystemPrompt": "Du bist NauAssist, ein persönlicher Kalender-Agent für Benedikt. Antworte präzise und auf Deutsch. Wenn der User eine Terminanfrage paste-t, rufe lookup_free_slots, wähle 2-3 passende Slots, rufe present_proposals damit, und formuliere danach eine kurze Antwort. Bestätigt der User einen Slot, rufe create_event. Bei Regel-Eingaben rufe add_rule mit strukturierten Args."
```

Neu (gleicher Text + Satz angehängt):
```json
"SystemPrompt": "Du bist NauAssist, ein persönlicher Kalender-Agent für Benedikt. Antworte präzise und auf Deutsch. Wenn der User eine Terminanfrage paste-t, rufe lookup_free_slots, wähle 2-3 passende Slots, rufe present_proposals damit, und formuliere danach eine kurze Antwort. Bestätigt der User einen Slot, rufe create_event. Bei Regel-Eingaben rufe add_rule mit strukturierten Args. Aktuelle Zeit, Wochentag und die exakten Daten für 'heute', 'morgen', 'diese/nächste Woche' und 'dieses/nächstes Wochenende' stehen im Zeit-Kontext-Block. Verwende immer diese Daten — nie eigene Schätzungen. Für ungewöhnliche Bezüge ('in drei Wochen am Donnerstag') rufe get_current_time."
```

- [ ] **Step 3: `Program.cs` — `using` für `Time`-Namespace ergänzen**

In `src/Backend/Program.cs` zu den `using`-Statements ergänzen:

```csharp
using NauAssist.Backend.Features.Infrastructure.Time;
```

- [ ] **Step 4: `Program.cs` — `TimeOptions` registrieren**

Direkt nach den anderen `builder.Services.Configure<...>(...)`-Calls (also nach `Configure<AgentOptions>`):

```csharp
builder.Services.Configure<TimeOptions>(builder.Configuration.GetSection("Time"));
```

- [ ] **Step 5: `Program.cs` — zentralen `TimeZoneInfo` + `ClockContext` registrieren**

Direkt nach der `Func<DateTimeOffset>`-Registrierung (Zeile mit `DateTimeOffset.UtcNow`):

```csharp
builder.Services.AddSingleton<TimeZoneInfo>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TimeOptions>>().Value;
    return TimeZoneInfo.FindSystemTimeZoneById(opts.Zone);
});

builder.Services.AddSingleton<ClockContext>(sp =>
{
    var clock = sp.GetRequiredService<Func<DateTimeOffset>>();
    var zone = sp.GetRequiredService<TimeZoneInfo>();
    return new ClockContext(clock, zone);
});
```

Wenn `IOptions` noch nicht importiert ist, oben ergänzen:

```csharp
using Microsoft.Extensions.Options;
```

- [ ] **Step 6: `Program.cs` — die zwei hardcoded `FindSystemTimeZoneById`-Calls ersetzen**

Aktuell (Zeile ~28):
```csharp
builder.Services.AddSingleton(_ =>
    new RuleApplicator(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
```

Ersetzen durch:
```csharp
builder.Services.AddSingleton(sp =>
    new RuleApplicator(sp.GetRequiredService<TimeZoneInfo>()));
```

Aktuell (Zeile ~34–42, `FreeSlotCalculator`-Factory):
```csharp
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CalendarOptions>>().Value;
    return new FreeSlotCalculator(
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"),
        TimeOnly.Parse(opts.WorkingHoursStart),
        TimeOnly.Parse(opts.WorkingHoursEnd),
        DayOfWeekFlags.WeekdaysOnly);
});
```

Ersetzen durch:
```csharp
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CalendarOptions>>().Value;
    return new FreeSlotCalculator(
        sp.GetRequiredService<TimeZoneInfo>(),
        TimeOnly.Parse(opts.WorkingHoursStart),
        TimeOnly.Parse(opts.WorkingHoursEnd),
        DayOfWeekFlags.WeekdaysOnly);
});
```

- [ ] **Step 7: `Program.cs` — `GetCurrentTimeTool` registrieren**

In den Block mit den anderen `AddScoped<ITool, ...>`-Calls eine Zeile ergänzen:

```csharp
builder.Services.AddScoped<ITool, GetCurrentTimeTool>();
```

- [ ] **Step 8: Build + alle Tests laufen lassen**

Run: `dotnet build src/Backend/Backend.csproj`
Expected: Build succeeded, 0 errors.

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: alle Tests grün (inkl. neuer ClockContext-, GetCurrentTimeTool- und AgentRunnerTimeContext-Tests).

- [ ] **Step 9: Commit**

```bash
git add src/Backend/Program.cs src/Backend/appsettings.json
git commit -m "Plan F Task 6: DI-Wiring (TimeOptions, ClockContext, GetCurrentTimeTool) + System-Prompt-Hinweis"
```

---

## Task 7: Manueller Smoke-Test

**Files:**
- (keine)

Verifiziert end-to-end, dass gemma4 die Zeit-Info auch tatsächlich nutzt.

- [ ] **Step 1: Backend starten**

Run: `dotnet run --project src/Backend/Backend.csproj`
Expected: Backend lauscht (typisch auf `http://localhost:5182`).

- [ ] **Step 2: Frontend starten (separates Terminal)**

Run: `cd frontend && npm run dev`
Expected: Vite-Dev-Server auf `http://localhost:5173`.

- [ ] **Step 3: Im Browser testen**

Drei Anfragen über die Chat-UI durchspielen:

1. "Was ist heute für ein Tag und welches Datum?" — Antwort muss heute + Wochentag aus dem Zeit-Kontext nennen.
2. "Welche Termine habe ich nächste Woche?" — Backend-Logs prüfen: `lookup_free_slots`/`get_calendar_range` muss mit `from = Montag der nächsten KW` und `to = Sonntag` (oder Mo der übernächsten als exklusiv) aufgerufen werden, NICHT mit Sonntag-Start.
3. "Hast du dieses Wochenende was im Kalender?" — Range muss Sa+So der aktuellen Woche sein.

- [ ] **Step 4: Backend stoppen**

Ctrl+C im Backend-Terminal.

- [ ] **Step 5: (Falls etwas nicht passt) gezielter Folge-Plan**

Wenn ein Datum noch falsch ist:
- Backend-Logs prüfen: Mit welchen `from`/`to` ruft der Agent die Tools?
- Falls die Tool-Argumente korrekt sind, aber die finale Antwort falsch: Reformulierung im System-Prompt erwägen.
- Falls die Tool-Argumente schon falsch sind: Zeit-Kontextblock-Format auf gemma4-Verständlichkeit prüfen (z. B. Wochenangabe expliziter).

Diese Iteration wäre ggf. eine eigene kleine Folge-Task, kein Rollback.

- [ ] **Step 6: (Kein Commit nötig — reine Verifikation)**

---

## Self-Review-Checkliste (für den ausführenden Agent)

Vor Abschluss prüfen:

- [ ] `dotnet test src/Backend.Tests/Backend.Tests.csproj` zeigt 100% grün.
- [ ] Im laufenden Backend werden zwei System-Messages an das LLM geschickt (eine "Persönlichkeit", eine "Zeit-Kontext").
- [ ] Keine `TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")`-Strings mehr außerhalb von `Program.cs` (zentralisiert auf `TimeOptions`). Test-Helpers in `Backend.Tests` dürfen es weiter direkt verwenden — das ist erwartet.
- [ ] `appsettings.json` enthält `Time.Zone` und der `SystemPrompt` hat den Zeit-Hinweis-Satz.
- [ ] Smoke-Test (Task 7) zeigt, dass "nächste Woche" mit Montag startet, nicht mit Sonntag.
