# Loading-Gate, Daten-Caching & Tab-Wechsel-Animation

**Datum:** 2026-06-12
**Status:** Design abgenommen

## Problem

Beim Tab-Wechsel (z. B. Chat → Kalender) rendert das Frontend sofort den
Default-/Leerzustand und tauscht ihn erst nach der Server-Antwort gegen den
echten Inhalt aus. Das wirkt flackernd und unprofessionell. Konkret:

- **Kalender** (`CalendarBoard`): Settings- und Event-Fetch laufen sequenziell,
  die „Heute“-Sidebar (`WhatsNext`) lädt separat — Inhalte ploppen stückweise rein.
- **Empfehlungen** (`RecommendationsPage`): `loading` startet mit `false`,
  im ersten Frame ist kurz der Leerzustand sichtbar, bevor das Laden beginnt.
- **Settings** (`SettingsPage`): Sektionen erscheinen erst, wenn ihre Daten da sind.
- **Chat** (`useChat`): Verlauf lädt nach dem Mount, Bubbles ploppen rein.

Zusätzlich werden bei jedem Tab-Wechsel alle Daten neu geladen (kein Cache),
und der Wechsel selbst ist ein harter Cut ohne Übergang.

## Entscheidungen (mit Benedikt abgestimmt)

1. **Umfang:** alle Tabs (Chat, Kalender, Empfehlungen, Settings).
2. **Loader-Stil:** zentraler Text-Loader `// LADE …` im Mono-Stil der App;
   Header und Tab-Leiste bleiben sichtbar. Kein Skeleton.
3. **Ansatz:** TanStack Query für Fetching + Caching (statt Per-Page-Eigenbau).
4. **Animation:** CSS-basierter Fade + leichtes Slide-up beim Seitenwechsel,
   kein framer-motion.

## Design

### 1. Caching mit TanStack Query

Neue Dependency `@tanstack/react-query`. `QueryClientProvider` wird in
`frontend/src/main.tsx` um die App gelegt.

Query-Keys:

| Key | Daten | Genutzt von |
|---|---|---|
| `["calendar-settings"]` | `getCalendarSettings()` | CalendarBoard, SettingsPage |
| `["calendar-range", fromISO, toISO]` | `getCalendarRange()` | CalendarBoard |
| `["calendar-today"]` | `getCalendarRange(heute)` | WhatsNext |
| `["suggestions", filter]` | `listSuggestions()` | RecommendationsPage |
| `["llm-settings"]` | `getLlmSettings()` | SettingsPage |
| `["ollama-settings"]` | `getOllamaSettings()` | SettingsPage |
| `["capabilities"]` | `getCapabilities()` | SettingsPage |
| `["chat-history"]` | `getHistory()` | useChat |

Defaults: `staleTime: 30_000`, `retry: 1`. Verhalten:

- **Erstbesuch eines Tabs** (kein Cache): PageLoader, Inhalt erscheint erst,
  wenn alle für die Seite nötigen Queries da sind.
- **Zurückwechseln** (Cache vorhanden): Inhalt sofort, stiller Hintergrund-Refetch.
- **Mutationen** invalidieren betroffene Queries via `queryClient.invalidateQueries`
  (Slot wählen / Suggestion dismiss → `suggestions`; Kalender-Mutationen &
  abgeschlossene Chat-Antworten → `calendar-range` + `calendar-today`;
  Settings speichern → jeweilige Settings-Keys). Ersetzt den bisherigen
  `reloadKey`-Mechanismus (`CalendarBoard.reloadKey`, `WhatsNext.reloadKey`).
- `NotConnectedError` bleibt erhalten: Kalender-Queries mappen ihn weiter auf
  den `NotConnected`-Zustand, kein Retry darauf.

### 2. PageLoader-Komponente

`frontend/src/components/nau/PageLoader.tsx`: füllt den Content-Bereich
(`flex-1`, zentriert), zeigt sanft pulsierendes `// LADE …` in Mono-Font
(`text-nau-fg-dim`). Optionaler `label`-Prop für abweichenden Text.
Die bisherigen Inline-Platzhalter (`// LADE EVENTS …`, `// LADE …`) entfallen
als Erstlade-Zustand; Fehlerzustände bleiben wie gehabt.

### 3. Gating pro Seite

- **CalendarPage/CalendarBoard:** PageLoader solange `calendar-settings` oder
  der erste `calendar-range`-Fetch pending ist (`isPending`). Bei Navigation
  (Woche/Monat/Jahr, vor/zurück) bleibt das alte Raster via
  `placeholderData: keepPreviousData` stehen — kein Flackern, kein Loader.
  `WhatsNext` zählt zum Initial-Gate der Vollansicht dazu.
- **RecommendationsPage:** PageLoader bei der ersten Ladung; Filterwechsel mit
  `keepPreviousData`. Behebt nebenbei den `loading`-startet-`false`-Bug.
- **SettingsPage:** PageLoader bis `llm-settings`, `ollama-settings`,
  `calendar-settings` und `capabilities` da sind (heute teils `null`-gated).
- **ChatView/useChat:** Initial-History wird zur `chat-history`-Query;
  PageLoader bis sie da ist. Streaming/Senden bleibt unangetastet: Bubbles
  bleiben lokaler State, der nur aus der Query initialisiert/re-synchronisiert
  wird, wenn gerade nicht gesendet/gestreamt wird. Nach Abschluss einer
  Antwort bzw. `/clear` wird `chat-history` invalidiert.
  Das auf Desktop eingebettete kompakte CalendarBoard gehört nicht zum
  Seiten-Gate — es zeigt beim allerersten Laden seinen eigenen Platzhalter,
  teilt sich aber die Queries mit dem Kalender-Tab, sodass der spätere
  Wechsel dorthin sofort gefüllt ist.

### 4. Tab-Wechsel-Animation

In `App.tsx` bekommt der Seiten-Container `key={page}` und eine
Tailwind-Keyframe-Animation `page-in`: Fade (opacity 0→1) + Slide-up
(~8 px, ~180 ms, ease-out). Gilt für Mobile-Bottom-Tabs und Desktop-Navigation.
`prefers-reduced-motion: reduce` deaktiviert die Animation (`motion-safe:`).

## Fehlerbehandlung

- Query-Fehler beim Erstladen: statt PageLoader die bestehende
  Fehlerdarstellung der jeweiligen Seite (Mono-Stil, `text-nau-danger`).
- Fehler beim Hintergrund-Refetch: gecachter Inhalt bleibt sichtbar,
  kein destruktives Überschreiben.

## Testing

- `npm run build` (tsc + vite) und `npm run lint` müssen grün sein.
- Manuelle Verifikation im Browser: Erstbesuch jedes Tabs zeigt PageLoader,
  Zurückwechseln zeigt Inhalt sofort, Wochen-Navigation flackert nicht,
  Chat-Streaming funktioniert unverändert, Animation beim Tab-Wechsel sichtbar.

## Nicht-Ziele

- Kein Skeleton-Loader, kein richtungsabhängiges Slide pro Tab-Position.
- Kein Offline-Cache/Persistenz über Reloads hinweg.
- Keine Änderung am Backend.
