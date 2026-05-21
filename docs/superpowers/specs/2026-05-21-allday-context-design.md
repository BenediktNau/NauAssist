# All-Day Context — Design

**Status:** Approved
**Datum:** 2026-05-21
**Autor:** Benedikt (mit Claude Code)

## Problem

NauAssist übersieht ganztägige Google-Kalender-Einträge — Urlaub, mehrtägige Schulungen, Reisen. Beispiel-Symptom: Der User ist Mi–Fr in Köln auf Schulung, trägt das als ganztägigen Eintrag in seinen Google-Kalender ein, und Nau schlägt trotzdem mitten in der Schulung Termine vor.

**Root Cause.** `GoogleCalendarProvider.GetEventsAsync` filtert in `src/Backend/Features/Calendar/Google/GoogleCalendarProvider.cs:40–43` jedes Event aus, das keinen `DateTimeDateTimeOffset` hat. Die Google-API liefert für All-Day-Events aber nur das `Date`-Feld (String `yyyy-MM-dd`), kein `DateTime`. Folge: All-Day-Events erreichen das Domänenmodell nie und sind für sämtliche Tools, Slot-Berechnungen und den Agenten unsichtbar.

Behebung des reinen Bugs ist nicht genug. Wenn All-Day-Events plötzlich als normale Events durchlaufen, würden sie als 24-stündige Busy-Slots interpretiert und ganze Tage aus dem Kalender ausblenden. Tatsächlich sind sie aber **Kontext** (User ist „auf Schulung", aber technisch verfügbar) — keine harte Reservierung wie ein Meeting.

## Goals

- All-Day-Events werden vollständig aus dem Google-Kalender gelesen und ins Domänenmodell übernommen.
- All-Day-Events blockieren **keine** Slots (`FreeSlotCalculator` ignoriert sie beim Slot-Schneiden).
- Der Agent bekommt All-Day-Events im 14-Tage-Horizont als eigenständigen **System-Prompt-Block** ähnlich dem bestehenden Zeit-Kontext-Block. Damit kann er bei Terminanfragen darauf reagieren („Du bist Mi–Fr in Köln auf Schulung — soll ich trotzdem Do 14 Uhr planen?").
- Nau kann All-Day-Events selbst anlegen — z. B. „trage mir Urlaub vom 1. bis 7.6. ein" — über einen optionalen `is_all_day`-Parameter im `create_event`-Tool.
- Single Source of Truth: das `IsAllDay`-Flag wird **einmal** im Google-Provider-Mapping gesetzt und fließt von dort durch das gesamte Modell.

## Non-Goals

- Keine Frontend-Anzeige von All-Day-Events im `SlotCard`/`ChatBubble`. Vorerst nur Agent-Awareness.
- Keine eigene Persistenz für „Kontext-Perioden" in SQLite — Google-Kalender bleibt Single Source.
- Kein Filter für wiederkehrende Geburtstage. Google liefert mit `SingleEvents=true` bereits expandierte Instanzen; falls der Kontext-Block dadurch spammig wird, in späterer Iteration mitigieren.
- Keine Erweiterung der Rules-Pipeline um All-Day-spezifische `SlotAnnotation`-Hinweise. Der System-Prompt-Block reicht für die Awareness; Slots sollen nicht annotiert werden.
- Keine Begrenzung der Anzahl Einträge im Kontext-Block. Bei einem typischen Privatkalender unkritisch; Mitigation aufgehoben für später.

## Architektur

### Neue Komponenten

**`Features/Calendar/Google/GoogleEventMapper.cs`** — internal static, reine Funktion. Trennt die Mapping-Logik vom `GoogleCalendarProvider`, sodass sie ohne echten Google-Client unit-testbar ist.

```csharp
internal static class GoogleEventMapper
{
    public static CalendarEvent? Map(Event e, TimeZoneInfo zone);
}
```

Erkennt drei Fälle:

1. `e.Start.DateTimeDateTimeOffset` gesetzt ⇒ regulärer Event, `IsAllDay=false`, Start/End wie bisher.
2. `e.Start.Date` gesetzt (String `yyyy-MM-dd`) ⇒ All-Day-Event. Parse zu `DateOnly`, dann `DateTimeOffset` in `zone` zu 00:00 lokal. End-Datum analog. `IsAllDay=true`.
3. Beides fehlt ⇒ `null` (silent skip wie bisher).

**`Features/Calendar/CalendarContext/CalendarContextBuilder.cs`** — scoped Service. Liefert den All-Day-Context-Block für den AgentRunner.

```csharp
public sealed class CalendarContextBuilder
{
    public CalendarContextBuilder(
        ICalendarProvider provider,
        IOptions<CalendarOptions> options,
        TimeZoneInfo zone);

    public Task<string> BuildAsync(TimeSnapshot now, CancellationToken ct);
}
```

`BuildAsync` ruft `provider.GetEventsAsync(today, today + SearchHorizonDays, ct)`, filtert `e.IsAllDay && e.End > now.NowLocal`, rendert den Block (Format siehe unten). Leer ⇒ leerer String.

### Geänderte Komponenten

**`Features/Calendar/CalendarEvent.cs`** — Record bekommt `bool IsAllDay = false` als letzten optionalen Parameter:

```csharp
public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? Location,
    bool IsAllDay = false);
```

**`Features/Calendar/NewEvent.cs`** — analog `bool IsAllDay = false`.

**`Features/Calendar/CreateEvent/CreateEventRequest.cs`** — analog `bool IsAllDay = false`. `CreateEventHandler` reicht das Flag an `NewEvent` durch; Validierung `End > Start` bleibt (bei All-Day = mindestens 1 Tag Unterschied).

**`Features/Calendar/Google/GoogleCalendarProvider.cs`** — drei Eingriffe:

1. Konstruktor erhält `TimeZoneInfo` (aus DI, derselbe wie für `ClockContext`).
2. `GetEventsAsync`: Filter aus Z.40–43 ersetzen durch Aufruf des neuen Mappers, der `null` zurückgeben kann; `null` ausfiltern.
3. `CreateEventAsync`: wenn `ev.IsAllDay`, setze `googleEvent.Start = new EventDateTime { Date = ev.Start.ToString("yyyy-MM-dd") }` und `googleEvent.End = new EventDateTime { Date = ev.End.ToString("yyyy-MM-dd") }`. Sonst wie bisher.

**`Features/Calendar/FreeSlotCalculator.cs`** — Z.34 vor `.Select(e => (Start, End))` einen `.Where(e => !e.IsAllDay)` einschieben. Reihenfolge wichtig: nach dem Select ist die Flag-Info aus dem Tupel weg.

**`Features/Agent/AgentRunner.cs`** — Konstruktor nimmt zusätzlich `CalendarContextBuilder`. In `HandleAsync` direkt nach `var snapshot = _clockContext.Build();`:

```csharp
var snapshot = _clockContext.Build();
var conversation = new List<LlmMessage> { new LlmMessage("system", BuildTimeContextBlock(snapshot)) };

var calendarBlock = await _calendarContext.BuildAsync(snapshot, ct);
if (!string.IsNullOrWhiteSpace(calendarBlock))
    conversation.Add(new LlmMessage("system", calendarBlock));

conversation.AddRange(history);
```

Die Reihenfolge ist: `[Persona] → [Zeit-Kontext] → [All-Day-Kontext (optional)] → [History]`.

**`Features/Agent/Tools/CreateEventTool.cs`** — Schema-Erweiterung um optionalen `is_all_day`-Bool:

```json
"is_all_day": { "type": "boolean", "default": false }
```

Description präzisieren: bei `is_all_day=true` werden `start` und `end` als `yyyy-MM-dd` erwartet; `end` ist exklusiv (1-Tages-Urlaub am 1.6. ⇒ `start=2026-06-01, end=2026-06-02`). Im `ExecuteAsync` bei `is_all_day=true` mit `DateOnly.ParseExact` parsen und zu `DateTimeOffset` 00:00 in lokaler Zone konvertieren; `IsAllDay=true` an den `CreateEventRequest` durchreichen.

**`Features/Agent/Tools/GetCalendarRangeTool.cs`** — serialisiertes Event-DTO um `is_all_day = e.IsAllDay` ergänzen. Damit liefert das Tool konsistente Information zum Kontext-Block, falls Nau es explizit aufruft.

**`Program.cs`** — `builder.Services.AddScoped<CalendarContextBuilder>();`. `TimeZoneInfo` aus `TimeOptions` ist bereits zentral registriert; `GoogleCalendarProvider` bekommt es in seinem Konstruktor.

**`appsettings.json` → `Ollama.SystemPrompt`** ergänzt um Schlusssatz:

> „Wenn ein Block ‚Längerfristiger Kontext — All-Day-Termine' erscheint, sind das ganztägige Einträge (Urlaub, Schulung, Reise) im Lookahead. Sie blockieren keinen Slot, aber prüfe vor Vorschlägen, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — frage bei Kollision aktiv nach."

## Datenformat

### System-Prompt-Block (Markdown)

```
[Längerfristiger Kontext — All-Day-Termine im 14-Tage-Horizont]
- Mi 27.5.–Fr 29.5.: Schulung Köln
- Mo 1.6.: Urlaub

Diese Termine sind ganztägig und blockieren keinen festen Slot. Bevor du Vorschläge machst,
prüfe, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — und frage bei
Kollision nach.
```

**Render-Regeln:**

- Wochentags-Kürzel deutsch (Mo/Di/Mi/Do/Fr/Sa/So) — selbe Tabelle wie in `BuildTimeContextBlock`.
- Datum: `d.M.` ohne Jahr (kompakt; bei Bedarf später mit Jahr für Events nahe Jahreswechsel).
- Single-Day-Erkennung: `(End.Date - Start.Date).Days == 1` ⇒ nur Start-Tag rendern (`- Mo 1.6.: Urlaub`).
- Multi-Day: `End.AddDays(-1).Date` als Anzeige-Enddatum — invertiert die exklusive Google-End-Semantik.
- Sortierung: nach Start aufsteigend.
- Wenn keine All-Day-Events im Horizont ⇒ leerer String, keine system-Message wird angehängt.

### IsAllDay → (Start, End) Konvention

All-Day-Events werden im Domänenmodell mit folgender Semantik abgebildet:

- `Start`: erster Tag 00:00 in lokaler Zone, als `DateTimeOffset`.
- `End`: Tag NACH dem letzten Tag 00:00 in lokaler Zone, als `DateTimeOffset`. End ist also **exklusiv** — konsistent zur Google-API.

Beispiel Schulung 26.–28.5. Berlin (Sommerzeit, UTC+02:00):

- `Start = 2026-05-26T00:00+02:00`
- `End   = 2026-05-29T00:00+02:00`
- `IsAllDay = true`

Beispiel 1-Tages-Urlaub am 1.6.:

- `Start = 2026-06-01T00:00+02:00`
- `End   = 2026-06-02T00:00+02:00`

Begründung: (a) konsistent mit Google's exklusiver End-Konvention für `Date`-Events — Round-Trip-stabil bei `CreateEventAsync` → API → `GetEventsAsync`; (b) der `FreeSlotCalculator` schneidet pro Local-Day, also stimmen Tagesgrenzen ohne Mehraufwand; (c) Multi-Day-Erkennung im Builder trivial via `(End.Date - Start.Date).Days`.

## Datenfluss

1. User-Message landet via `SendMessageHandler` → `AgentRunner.HandleAsync(history, ct)`.
2. `AgentRunner.HandleAsync` ruft `_clockContext.Build()` → `TimeSnapshot`.
3. `AgentRunner` baut den Zeit-Kontext-Block (unverändert).
4. `AgentRunner` ruft `_calendarContext.BuildAsync(snapshot, ct)`. Der Builder:
   - bestimmt `from = snapshot.Today 00:00 lokal`, `to = from + SearchHorizonDays`,
   - holt Events via `ICalendarProvider.GetEventsAsync(from, to, ct)`,
   - filtert `e.IsAllDay && e.End > snapshot.NowLocal`,
   - rendert den Markdown-Block (oder leeren String).
5. Bei nicht-leerem Block wird er als zweite system-Message vor `history` eingefügt.
6. Tool-Loop läuft. Beim Aufruf von `lookup_free_slots` ignoriert `FreeSlotCalculator` All-Day-Events — sie blockieren keine Slots. Beim Aufruf von `get_calendar_range` werden sie aber mit `is_all_day=true` zurückgegeben.
7. Beim Aufruf von `create_event` mit `is_all_day=true` parst das Tool die Datumsangaben, der Provider schreibt `EventDateTime.Date` in die Google-API.

**Single source of truth.** `IsAllDay` wird genau einmal im `GoogleEventMapper` aus der Google-Repräsentation abgeleitet und fließt durch das ganze Modell. Drift zwischen Tools, Calculator und Context-Block ist konstruktionsbedingt ausgeschlossen.

## Edge Cases

**Vergangene All-Day-Events.** Ein Event, das heute 00:00 endet, wird vom Builder ausgefiltert (`End > now.NowLocal`). Bei heutigem `NowLocal = 2026-05-21T14:32` filtert das ein Event mit `End = 2026-05-21T00:00` korrekt aus.

**DST-Übergänge.** All-Day-Events sind in Google tz-los (reines Date). Wir mappen mit `new DateTimeOffset(localDateTime, zone.GetUtcOffset(localDateTime))` — der Offset wird zum konkreten Zeitpunkt aufgelöst, DST-Wechsel (28.3., 27.10.) werden korrekt behandelt. Test deckt beide Tage ab.

**Mehrtägige All-Day-Events, die in der Vergangenheit begonnen haben.** „Schulung 19.–22.5." bei `Today=21.5.`: `Start=19.5. 00:00`, `End=23.5. 00:00`, `End > NowLocal` ⇒ ja, wird angezeigt. Render-Konvention: `Di 19.5.–Fr 22.5.: Schulung` — auch wenn Start vor heute liegt. Gewollt: Nau soll den vollen Kontext sehen, nicht eine abgeschnittene Anzeige.

**Wiederkehrende Geburtstage.** Google expandiert mit `SingleEvents=true`. Risiko: Geburtstags-Spam im Kontext-Block. Mitigation in dieser Iteration ausdrücklich nicht vorgesehen (Non-Goal). Falls in der Praxis relevant, später z. B. Titel-Regex oder Calendar-Source-Filter.

**Bestehende Tests mit FakeCalendarProvider.** `CalendarEvent` ist Record mit Default `IsAllDay=false` — Konstruktoren ohne neuen Parameter kompilieren weiter. Bestehende Tests bleiben grün.

**Audit-Log.** `audit_log.tool_args_json` für `create_event` enthält bei All-Day-Aufrufen `is_all_day=true`. Keine Schema-Änderung, JSON ist freiform.

## Tests

Im bestehenden `Backend.Tests`-Projekt.

**`CalendarModelTests`** (neu):

- `CalendarEvent_Default_IsAllDay_ist_false`.
- `NewEvent_Default_IsAllDay_ist_false`.
- `CalendarEvent_mit_IsAllDay_true_konstruierbar`.

**`GoogleEventMapperTests`** (neu):

- `DateTime_Event_wird_zu_IsAllDay_false_gemappt`.
- `Date_only_Event_wird_zu_IsAllDay_true_mit_lokaler_Mitternacht`.
- `Multi_Day_Date_only_Event_korrekte_Endgrenze` (Schulung 26.–28.5. ⇒ End=29.5. 00:00).
- `DST_Frühjahrsübergang_28_3_2026_korrekter_Offset`.
- `DST_Herbstübergang_27_10_2026_korrekter_Offset`.
- `Beide_Felder_null_gibt_null_zurück` (silent skip).

**`FreeSlotCalculatorTests`** (Erweiterung):

- `Calculate_AllDayEvent_DoesNotBlockSlots` — All-Day 27.5. 00:00–28.5. 00:00, Slots am 27.5. 09–18 Uhr unverändert.
- `Calculate_AllDayPlusRegularSameDay_OnlyRegularBlocks` — All-Day + 12–13 Uhr regulär, Lücke nur 12–13 Uhr.
- `Calculate_MultiDayAllDay_PlusRegular_RegularStillBlocks` — All-Day 26.–29.5. + 12–13 Uhr am 27.5., 27.5. zeigt nur eine Lücke um 12 Uhr.

**`CalendarContextBuilderTests`** (neu):

- `Leer_wenn_keine_All_Day_Events`.
- `Single_Day_All_Day_wird_als_ein_Datum_gerendert`.
- `Multi_Day_All_Day_rendert_Bereich_mit_minus_1_Tag_Konvention`.
- `Vergangenes_All_Day_End_eq_now_wird_ausgefiltert`.
- `Reguläre_Events_tauchen_nicht_im_Block_auf`.
- `Sortierung_nach_Start_aufsteigend`.

**`CreateEventHandlerTests`** (Erweiterung):

- `Handle_mit_IsAllDay_true_reicht_Flag_an_Provider_durch`.
- `Handle_mit_IsAllDay_true_und_End_eq_Start_wirft` (Validierung).

**`CreateEventToolTests`** (neu falls noch nicht vorhanden):

- `Execute_mit_is_all_day_true_und_yyyy_MM_dd_baut_DateTimeOffset_00_00_lokal`.
- `Execute_ohne_is_all_day_unverändertes_Verhalten`.

**`AgentRunnerCalendarContextTests`** (neu oder Erweiterung von TimeContextTests):

- `Keine_All_Day_Events_nur_eine_system_message_prepended` — bestehender Test bleibt grün.
- `Mit_All_Day_Event_zweite_system_message_enthält_Titel_und_Datum`.

## Migration / Backward-Compatibility

- `CalendarEvent`, `NewEvent`, `CreateEventRequest` werden um optionalen Record-Parameter `IsAllDay = false` erweitert. Alle bestehenden Aufrufer (positional und named) kompilieren weiter.
- `GetCalendarRangeTool` JSON-Output erhält zusätzliches Feld `is_all_day` — kein Konsument matched das Schema strikt.
- `audit_log` und `messages` SQLite-Schema unverändert.
- Keine Datenmigration.

## Konfig-Diff (illustrativ)

```diff
   "Ollama": {
     ...
-    "SystemPrompt": "Du bist NauAssist ... Verwende immer diese Daten ..."
+    "SystemPrompt": "Du bist NauAssist ... Verwende immer diese Daten ... Wenn ein Block 'Längerfristiger Kontext — All-Day-Termine' erscheint, sind das ganztägige Einträge im Lookahead. Sie blockieren keinen Slot, aber prüfe vor Vorschlägen, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — frage bei Kollision aktiv nach."
   },
```

## Offene Punkte für später

- **Frontend-Anzeige** von All-Day-Events im SlotCard / Wochen-Übersicht.
- **Recurring-Event-Filter** für Geburtstags-Spam, falls in der Praxis störend.
- **Rules-Pipeline-Annotations** für All-Day-Kollision direkt im Slot (statt nur als Prompt-Hinweis).
- **Multi-Day-Spec für `create_event`-Tool** mit `duration_days` als Alternative zu explizitem Enddatum.
