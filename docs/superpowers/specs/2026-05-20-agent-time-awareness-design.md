# Agent Time Awareness — Design

**Status:** Approved
**Datum:** 2026-05-20
**Autor:** Benedikt (mit Claude Code)

## Problem

Der NauAssist-Agent (gemma4 via Ollama) weiß die aktuelle Zeit nicht. Er rät anhand seines Trainingsstands und der US-Wochenkonvention (Sonntag = erster Wochentag). Symptome:

- Fragt der User nach "Terminen nächste Woche", beginnt der Agent mit Sonntag statt Montag.
- Datumsangaben sind um einen Tag verschoben.
- Anfragen mit relativen Zeitbezügen ("nächstes Wochenende", "morgen", "in drei Wochen") liefern falsche absolute Daten an die Tools (`lookup_free_slots`, `get_calendar_range`, `create_event`).

Das ist nicht das Modell-Verschulden — wir geben ihm schlicht keine verbindliche Zeitreferenz.

## Goals

- Der Agent kennt bei jedem Turn die aktuelle Zeit in `Europe/Berlin` ohne Tool-Roundtrip.
- "Diese/nächste Woche" und "dieses/nächstes Wochenende" sind als absolute Daten verfügbar — keine Interpretation nötig.
- Wochenkonvention: ISO 8601 (Montag = erster Tag der Woche). Verbindlich dokumentiert im Prompt.
- Für ungewöhnliche Zeitbezüge ("in drei Wochen am Donnerstag") hat der Agent ein Tool, das einen reichen Zeit-Snapshot liefert.
- Eine Single Source of Truth für Zeit-Berechnungen — Kontextblock und Tool dürfen nicht auseinanderdriften.
- Die existierende `Europe/Berlin`-Hardcodierung wird auf eine Konfig-Stelle konsolidiert.

## Non-Goals

- Kein Natural-Language-Datums-Parser server-seitig (z.B. "next saturday"-Resolver). Symptom-Bekämpfung statt Ursache.
- Kein eigenes `DateMathTool` mit Operationen wie `add_days` in dieser Iteration. **Notiert für später** — falls sich nach Rollout zeigt, dass der reiche Snapshot + `get_current_time` für komplexere Bezüge nicht reichen, gerne nachziehen.
- Keine Multi-User-Timezone-Logik. Single-User, `Europe/Berlin` fix in Config.
- Keine History-Rewrite-Logik. Alte User-Nachrichten werden weiterhin gegen den aktuellen Zeit-Kontext interpretiert (siehe Edge Cases).

## Architektur

### Neue Komponenten

**`Features/Infrastructure/Time/TimeOptions.cs`**

```csharp
public sealed class TimeOptions
{
    public string Zone { get; set; } = "Europe/Berlin";
}
```

Registriert in `Program.cs` über `builder.Configuration.GetSection("Time")`. Eintrag in `appsettings.json`:

```json
"Time": { "Zone": "Europe/Berlin" }
```

**`Features/Infrastructure/Time/TimeSnapshot.cs`** — immutables Record:

```csharp
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

public sealed record DateRange(DateOnly Start, DateOnly End);
```

**`Features/Infrastructure/Time/ClockContext.cs`** — der Builder. Konstruktor nimmt `Func<DateTimeOffset>` (bestehender Clock-DI) + `TimeZoneInfo`. Eine Methode `Build() : TimeSnapshot`. Reine Funktion, keine Side Effects.

```csharp
public sealed class ClockContext
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;

    public ClockContext(Func<DateTimeOffset> clock, TimeZoneInfo zone) { ... }

    public TimeSnapshot Build() { ... }
}
```

**`Features/Agent/Tools/GetCurrentTimeTool.cs`** — `ITool`, kein Parameter-Schema (leeres Objekt). Ruft `ClockContext.Build()` und serialisiert den Snapshot ins unten festgelegte JSON-Format.

### Geänderte Komponenten

**`Program.cs`** — drei Eingriffe:

1. `TimeOptions` aus Config laden.
2. `ClockContext` registrieren — `TimeZoneInfo.FindSystemTimeZoneById(timeOptions.Zone)` einmal hier auflösen.
3. `GetCurrentTimeTool` als `ITool` registrieren.
4. Die zwei bestehenden `TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")`-Calls in `Program.cs` (für `RuleApplicator` und `FreeSlotCalculator`) auf den zentral aufgelösten `TimeZoneInfo` aus `TimeOptions` umstellen.

**`Features/Agent/AgentRunner.cs`** — Konstruktor nimmt zusätzlich `ClockContext`. In `HandleAsync` am Anfang:

```csharp
var snapshot = _clockContext.Build();
var timeContextMessage = new LlmMessage("system", BuildTimeContextBlock(snapshot));
var conversation = new List<LlmMessage> { timeContextMessage };
conversation.AddRange(history);
```

`BuildTimeContextBlock(TimeSnapshot)` ist eine private statische Methode in `AgentRunner`, die das unten festgelegte Markdown-Format produziert.

Der existierende statische `SystemPrompt` aus `OllamaOptions` bleibt unverändert — `OllamaLlmClient` prepended ihn weiterhin als erste System-Message. Das LLM sieht also: `[Persönlichkeit] → [Zeit-Kontext] → [User/Assistant-Verlauf]`.

**`appsettings.json` → `Ollama.SystemPrompt`** wird um einen Schlusssatz ergänzt:

> "Aktuelle Zeit, Wochentag und die exakten Daten für 'heute', 'morgen', 'diese/nächste Woche' und 'dieses/nächstes Wochenende' stehen im Zeit-Kontext-Block. Verwende immer diese Daten — nie eigene Schätzungen. Für ungewöhnliche Bezüge ('in drei Wochen am Donnerstag') rufe `get_current_time`."

## Datenfluss

1. User-Message landet via `SendMessageHandler` → `AgentRunner.HandleAsync(history, ct)`.
2. `AgentRunner` ruft `_clockContext.Build()` → `TimeSnapshot`.
3. `AgentRunner` baut die Zeit-Kontext-System-Message und prepended sie an `conversation`.
4. Tool-Loop läuft. `conversation` bleibt über alle Iterationen erhalten — die Zeit-Kontext-Message also ebenfalls.
5. Ruft das Modell `get_current_time`, baut `GetCurrentTimeTool` einen frischen Snapshot über denselben `ClockContext.Build()` und gibt JSON zurück.

**Single source of truth:** Beide Outputs (Markdown-Kontextblock und Tool-JSON) leiten sich aus genau einem `Build()`-Call ab. Drift unmöglich.

## Datenformate

### Markdown-Kontextblock (System-Message)

```
[Zeit-Kontext — verbindlich, alle Daten in Europe/Berlin]
Jetzt:          2026-05-20T14:32:00+02:00 (Mittwoch, KW 21)
Heute:          2026-05-20 (Mi)
Morgen:         2026-05-21 (Do)
Diese Woche:    2026-05-18 (Mo) bis 2026-05-24 (So)
Nächste Woche:  2026-05-25 (Mo) bis 2026-05-31 (So)
Dieses WE:      2026-05-23 (Sa) bis 2026-05-24 (So)
Nächstes WE:    2026-05-30 (Sa) bis 2026-05-31 (So)

Wochenkonvention: Montag ist der erste Tag der Woche (ISO 8601).
"Nächste Woche" = Mo–So der KW nach der aktuellen.
"Dieses Wochenende" = der Sa+So in der aktuellen KW.
"Nächstes Wochenende" = der Sa+So in der nächsten KW.
Wenn heute Sa/So ist, ist "dieses Wochenende" das laufende; "nächstes Wochenende" ist 7 Tage später.
```

Format-Hinweis: Ausrichtung mit Spaces (nicht Tabs), damit gemma4 die Spalten zuverlässig parst.

### JSON (Tool-Return)

```json
{
  "now": "2026-05-20T14:32:00+02:00",
  "timezone": "Europe/Berlin",
  "today": "2026-05-20",
  "tomorrow": "2026-05-21",
  "weekday": "Mittwoch",
  "iso_week": 21,
  "this_week":     { "start": "2026-05-18", "end": "2026-05-24" },
  "next_week":     { "start": "2026-05-25", "end": "2026-05-31" },
  "this_weekend":  { "start": "2026-05-23", "end": "2026-05-24" },
  "next_weekend":  { "start": "2026-05-30", "end": "2026-05-31" }
}
```

Alle `*_week`/`*_weekend`-Ranges sind inklusiv: `start` und `end` sind beide Tage, die zum Bereich gehören.

## Berechnungsregeln

**Tagesgrenzen** werden in lokaler Zeit (`Europe/Berlin`) bestimmt, nicht in UTC. `today = DateOnly.FromDateTime(nowLocal.DateTime)`. Sonst rutscht das Datum nachts um 23:00 lokal fälschlich auf den nächsten Tag.

**`thisWeek` / `nextWeek` (ISO 8601, Mo–So):**

- `thisWeek.Start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7))` — Mo der aktuellen Woche.
- `thisWeek.End = thisWeek.Start.AddDays(6)` — So.
- `nextWeek.Start = thisWeek.Start.AddDays(7)`, `nextWeek.End = thisWeek.End.AddDays(7)`.

**`thisWeekend` / `nextWeekend`:**

- Wenn heute **Mo–Fr** ist: `thisWeekend = (kommender Sa, kommender So)`; `nextWeekend = thisWeekend + 7 Tage`.
- Wenn heute **Sa** ist: `thisWeekend = (heute, morgen)`; `nextWeekend = +7 Tage`.
- Wenn heute **So** ist: `thisWeekend = (gestern, heute)`; `nextWeekend = (heute+6, heute+7)`.

**`isoWeek`** über `ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue))`.

**`weekdayDe`** über ein internes Mapping `DayOfWeek → "Montag"…"Sonntag"` (kein `CultureInfo`-Branching — explizit und testbar).

**DST:** `DateOnly`-Arithmetik ist immun gegen DST-Übergänge. `nowLocal` per `TimeZoneInfo.ConvertTime(nowUtc, zone)` — `TimeZoneInfo` handhabt Sommer-/Winterzeit korrekt.

## Edge Cases

**History-Replay:** Alte Chat-Nachrichten werden immer gegen den _aktuellen_ Zeit-Kontext interpretiert. Eine User-Frage "morgen 10 Uhr" von vor 3 Tagen meint heute aus Sicht des LLM, also +1 Tag ab heute. Das ist gewollt — Termine sind im Kalender mit absoluten Daten persistiert, der Chat-Verlauf dient nur als Konversations-Kontext.

**Mitternachts-Übergang während eines Tool-Loops:** `_clockContext.Build()` wird einmal pro Agent-Turn aufgerufen. Innerhalb eines Turns ist die Zeit stabil. Ruft das Modell `get_current_time` Sekunden nach dem ursprünglichen Build und es ist gerade Mitternacht, sieht es einen anderen Snapshot als der Kontextblock. Akzeptierter Trade-off — passiert selten und ein erneuter Tool-Call ist eher ein Korrektiv als ein Bug.

**Sommerzeit-Übergang in der angefragten Woche:** `DateRange.Start` und `DateRange.End` sind `DateOnly`. Stunden-Verschiebungen durch DST betreffen nur Slot-Berechnung im `FreeSlotCalculator` (bereits gelöst). Der Zeit-Kontext bleibt korrekt.

## Tests

Im bestehenden `Backend.Tests`-Projekt.

**`ClockContextTests`** — Unit, mit injizierter Fake-Clock:

- `Mittwoch_14h32_Berlin_StandardFall` — Snapshot matched die im Spec gezeigten Werte exakt.
- `Samstag_thisWeekend_ist_heute_und_morgen`
- `Sonntag_thisWeek_endet_heute_nextWeek_startet_morgen` — explizit der bug-auslösende Fall.
- `Sonntag_thisWeekend_ist_gestern_und_heute`
- `Sonntag_23h30_UTC_Sommer_nowLocal_ist_Montag_01h30_today_ist_Mo` — Tageswechsel über UTC-Grenze.
- `DST_Frühjahrsübergang_Mo_bis_So_korrekt_obwohl_eine_Stunde_fehlt` — z.B. clock auf 31. März 2026.
- `DST_Herbstübergang_korrekt` — analog.

**`GetCurrentTimeToolTests`** — Tool gibt JSON mit allen Feldern; Werte == `ClockContext.Build()` (gleicher injizierter Builder).

**`AgentRunnerTests` (Integration, Stil wie bestehende E2E-Tests):** mit Fake-Clock "Mittwoch 14:32" prüfen, dass die `conversation`, die an `_llm.ChatStreamAsync` geht, am Anfang die Zeit-Kontext-System-Message enthält und diese die erwarteten Daten ausweist. (Stub-LLM-Client kann die Messages für die Assertion abgreifen.)

## Konfig-Diff (illustrativ)

```diff
 {
   "Persistence": { ... },
   "Calendar": { ... },
+  "Time": { "Zone": "Europe/Berlin" },
   "Ollama": {
     ...
-    "SystemPrompt": "Du bist NauAssist, ein persönlicher Kalender-Agent ... add_rule mit strukturierten Args."
+    "SystemPrompt": "Du bist NauAssist, ein persönlicher Kalender-Agent ... add_rule mit strukturierten Args. Aktuelle Zeit, Wochentag und die exakten Daten für 'heute', 'morgen', 'diese/nächste Woche' und 'dieses/nächstes Wochenende' stehen im Zeit-Kontext-Block. Verwende immer diese Daten — nie eigene Schätzungen. Für ungewöhnliche Bezüge ('in drei Wochen am Donnerstag') rufe `get_current_time`."
   },
   "Agent": { ... }
 }
```

## Offene Punkte für später

- **`DateMathTool`** mit Operationen wie `add_days`, `weekday_of`, `nth_weekday_after` — wenn sich nach Rollout zeigt, dass der reiche Snapshot + `get_current_time` für unübliche Bezüge nicht reichen. Vorgemerkt, nicht in dieser Iteration.
