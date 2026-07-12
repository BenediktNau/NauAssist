# Slot-Auswahl v2 — Tageszeit, Kontext, Soft-Hold & manueller Slot

## Ziel

Die Slot-Vorschläge der autonomen Suggestions sind heute zu starr. Realer Fall (2026-07-09):
zwei WhatsApp-Anfragen („können auch morgen @work" von Peter, „Morgen Abend geht auch" von
Emre) bekamen **beide denselben einzigen Slot** (Fr 09:00–10:00) — obwohl Emre explizit
abends wollte.

Drei verifizierte Ursachen:

1. **Nur 1 Slot bei Ein-Tages-Anfragen:** `PickSpreadSlots` wählt bewusst einen Slot pro
   Tag (max 3, über Tage gestreut). Bei `date_hint: tomorrow` bleibt strukturell nur ein
   Slot übrig.
2. **Immer derselbe früheste Slot:** `FreeSlotCalculator` emittiert chronologisch ab
   `WorkingHoursStart` (09:00); `PickSpreadSlots` nimmt den ersten „Passes"-Slot. Offene
   Suggestions kennen einander nicht → zwei Personen bekommen denselben Slot angeboten
   (Doppelbuchungsrisiko).
3. **Tageszeit geht verloren:** Das Classifier-Schema kennt kein Tageszeit-Feld
   (`date_hint` ist tages-granular), und der Calculator generiert nur innerhalb der
   Arbeitszeiten (Default 09:00–18:00) an Werktagen — für „abends" existieren keine
   Kandidaten. Latenter Bonus-Bug: `DayOfWeekFlags.WeekdaysOnly` ist in `Program.cs`
   hartkodiert → Wochenend-Anfragen („dieses Wochenende") erzeugen still **keine**
   Suggestion.

Zusätzliche Anforderung: Slots sollen **manuell anpassbar** sein — vorgeschlagene Slots
feinjustieren und komplett eigene Slots eintragen.

## Entscheidungen (mit Benedikt geklärt)

| Frage | Entscheidung |
|---|---|
| Suchfenster | **Immer erweitert** (Default 08:00–22:00, konfigurierbar), alle 7 Tage |
| Geltungsbereich | **Überall einheitlich** — autonome Suggestions und Chat-Agent (`lookup_free_slots`) |
| Arbeitszeiten | Kein automatischer Malus — Termine außerhalb der Arbeitszeit sind normal (privat). Working Hours bleiben als „bevorzugtes Fenster für berufliche Termine" |
| Ohne Tageszeit-Hint | **Kontext-Präferenz:** Classifier schätzt `work`/`private`; work → Arbeitszeiten, privat → Abend/Wochenende |
| Soft-Hold | **Meiden mit Fallback:** bereits angebotene Slots überspringen, solange Alternativen existieren; sonst anbieten mit Warn-Note |
| Manueller Slot | **Beides:** Vorschläge feinjustieren + eigenen Slot eintragen; Konflikt-Prüfung warnend, nicht blockierend |

## Betrachtete Ansätze

1. **Präferenzlogik im Reasoner (Post-Filter)** — Calculator bleibt dumm (nur Fenster
   erweitert), die gesamte Auswahl-Intelligenz lebt in einer neuen, pur testbaren
   `SlotPicker`-Klasse. **Gewählt:** kleinste Änderungsfläche, kein LLM-Tool-Schema-Umbau
   (gemma-Zuverlässigkeit), eine Stelle für die Heuristik.
2. Präferenz als Parameter der Slot-Suche (Request/Handler/Tool-Schema erweitern) —
   sauber wiederverwendbar, aber deutlich größerer Umbau; spätere Erweiterung bleibt möglich.
3. Tagesteile als First-Class-Konzept durch alle Schichten — konzeptionell elegant,
   aber unnötiges neues Konzept in Calculator/Response/Frontend.

## Architektur

### 1. Settings (`AppSettingsRepository`, Settings-UI)

- Neue Keys `calendar.slot_search_start` / `calendar.slot_search_end`,
  Defaults **08:00 / 22:00** (Code-Default via `GetValueOrDefault`, keine SQL-Migration).
- `CalendarUserSettings` um `SlotSearchStart`/`SlotSearchEnd` erweitert; Get/Set-Handler
  und Settings-Endpoint durchgereicht, Validierung `start < end` (400).
- Frontend: zwei Zeitfelder in der Kalender-Sektion neben den Working Hours.
- Working Hours behalten Feld + UI; neue Semantik: bevorzugtes Fenster für `work`-Kontext.

### 2. `FreeSlotCalculator` + DI (`Program.cs`)

- Konstruktor erhält `SlotSearchStart`/`SlotSearchEnd` statt Working Hours.
- `DayOfWeekFlags.WeekdaysOnly` → alle 7 Tage (fixt den Wochenend-Bug).
- Sonst unverändert: chronologische Kandidaten, Events blocken, Rules annotieren.

### 3. `IntentClassifier` + `ClassificationResult`

Schema-Erweiterung (Prompt + Parsing):

```json
"time_of_day": "morning" | "afternoon" | "evening" | null,
"context": "work" | "private" | null
```

- `time_of_day` nur bei **explizit genannter** Tageszeit („abends", „nach der Arbeit",
  „vormittags").
- `context`: beste Schätzung beruflich/privat aus Inhalt + Quelle; unsicher → null.
- Parsing defensiv: unbekannte Werte → null (kein Fehler, Debug-Log).

### 4. `SuggestionRepository`

- Neu: `ListOpenSlotsAsync(from, to, ct)` → alle Slots offener (`pending`) Suggestions
  des Users, deren Zeitraum sich mit `[from, to)` überlappt. Datenbasis für Soft-Hold.
- Neu: `AppendSlotAndPickAsync(id, slot, now, ct)` → hängt einen Slot atomar an die
  Slot-Liste an und setzt `picked_slot` auf dessen Index; nur bei `status = 'pending'`.

### 5. `SlotPicker` (neue Klasse, ersetzt `AutonomousReasoner.PickSpreadSlots`)

Pure Funktion, kein I/O. Input: Slot-Annotationen (aus `LookupFreeSlots`), `timeOfDay`,
`context`, Working Hours, Soft-Hold-Slots, `max` (3). Output: bis zu 3 `SuggestionSlot`
mit Notes.

**Tagesteile** (fixe Konstanten): `morning` = Fensterstart–12:00, `afternoon` =
12:00–17:00, `evening` = 17:00–Fensterende.

**Bevorzugtes Fenster** (erste zutreffende Regel):
1. `time_of_day` gesetzt → genau dieses Tagesteil
2. `context = work` → Working Hours ∩ Werktage
3. `context = private` → evening an Werktagen + ganztags am Wochenende
4. sonst → keine Präferenz

**Auswahl-Tiers** (von oben auffüllen bis `max` erreicht):

| Tier | Bedingung | Note |
|---|---|---|
| 1 | Passes ∧ bevorzugt ∧ nicht held | — |
| 2 | Passes ∧ nicht held | bei explizitem `time_of_day`: „Achtung: außerhalb der gewünschten Tageszeit" |
| 3 | SoftViolation ∧ bevorzugt ∧ nicht held | Rule-Text (wie bisher) |
| 4 | SoftViolation ∧ nicht held | Rule-Text |
| 5 | held (überlappt Slot einer anderen offenen Suggestion) | „Achtung: bereits in anderer Anfrage angeboten" |

**Diversität je Tier:** max ein Slot pro (Tag, Tagesteil); bei Mehr-Tages-Fenstern werden
erst Tage gestreut (round-robin), dann Tagesteile. Single-Day → bis zu 3 Slots über
Vormittag/Nachmittag/Abend.

### 6. `AutonomousReasoner`

- Lädt vor der Auswahl `ListOpenSlotsAsync(from, to)` (eigene, gerade aktualisierte
  Thread-Suggestion ausgenommen) und übergibt an `SlotPicker`.
- Reicht `time_of_day`/`context` aus der Klassifikation durch. Sonst unverändert
  (Thread-Update-Logik, Push, MapDateHint bleiben).

### 7. Manueller Slot (`SuggestionsEndpoints` + Frontend)

- **Neu:** `POST /api/suggestions/{id}/pick-custom` mit `{ start, end }`.
  - Validierung blockierend: `end > start`, `start` nicht in der Vergangenheit,
    Suggestion `pending` (sonst 409/400).
  - Konflikt-Prüfung **warnend**: Überlappung mit Kalender-Events („Achtung: kollidiert
    mit ‚…'") und Rule-Annotation → als Note am Slot gespeichert und in der Response.
  - Persistenz via `AppendSlotAndPickAsync`; anschließend dieselbe On-Demand-
    Draft-Verfeinerung wie beim bestehenden `pick`.
  - Der Custom-Slot zählt automatisch als Soft-Hold für andere Suggestions (er steht in
    der Slot-Liste einer offenen Suggestion).
- **Frontend:** Stift-Icon je Slot (Picker vorbefüllt, Dauer bleibt beim Verschieben
  erhalten) + Zeile „+ Eigener Slot" (Picker, vorbefüllt mit erstem Vorschlag). Beide
  rufen `pick-custom`; danach Refresh — Custom-Slot erscheint als gewählt inkl. Note.

## Fehlerbehandlung

- Ungültige Classifier-Werte in neuen Feldern → null → „keine Präferenz" (Debug-Log).
- Explizites `time_of_day` ohne freie Slots im Fenster → Tier-2-Fallback mit Warn-Note
  statt keiner Suggestion.
- Alle Kandidaten held → Tier 5 mit Note (kein Verhungern).
- Keine Kandidaten im gesamten Fenster → wie bisher keine Suggestion + Info-Log.
- `slot_search_start >= slot_search_end` → 400 im Settings-Endpoint; Repository fällt
  bei korrupten Werten auf Defaults zurück.
- Der autonome Tick darf durch nichts davon crashen (bestehendes Muster: catch + Warning).

## Tests (TDD)

- **`SlotPicker`** (Kern, pur): Single-Day-Streuung über Tagesteile; Evening-Hint;
  work-/private-Kontext; Wochenende-privat; Soft-Hold meiden + Tier-5-Fallback;
  Tier-Reihenfolge mit Rules; Tag-Round-Robin; deterministische Reihenfolge.
- **`IntentClassifier`**: Parsing `time_of_day`/`context`, ungültige Werte → null.
- **`FreeSlotCalculator`**: erweitertes Fenster, 7-Tage-Generierung (Wochenend-Fix).
- **`SuggestionRepository`**: `ListOpenSlotsAsync` (nur pending, Überlappung, user-scoped);
  `AppendSlotAndPickAsync` (atomar, nur pending).
- **`pick-custom`-Endpoint**: Happy Path, Konflikt-Warnung, 400/409, Draft-Verfeinerung.
- **Reasoner-Integration**: Peter/Emre-Szenario end-to-end mit Fake-LLM — zwei Signale,
  work vs. evening, disjunkte Slots.
- Bestehende Suite bleibt grün; Tests der alten Ein-Slot-pro-Tag-Heuristik werden bewusst
  auf das neue Verhalten angepasst.

## Rollout

- Keine SQL-Migration (Settings key-value mit Code-Defaults; Slots-JSON hat `Note` schon).
- Bestehende offene Suggestions unverändert; neue Ticks nutzen sofort die neue Logik.
- Verhaltensänderung für den Chat-Agenten (mehr Kandidaten inkl. Abend/Wochenende) im
  PR-Text dokumentieren.
- Umsetzung im eigenen Worktree, PR gegen `main`.

## Aus dem Scope

- Strukturierte Tageszeit-Parameter für das Chat-Tool `lookup_free_slots` (Ansatz 2) —
  saubere spätere Erweiterung.
- Konfigurierbare Tagesteil-Grenzen.
- Hard-Reservierung/Locking von Slots über Suggestions hinweg (Soft-Hold reicht).
- Mehrere Slots pro (Tag, Tagesteil)-Kombination.
