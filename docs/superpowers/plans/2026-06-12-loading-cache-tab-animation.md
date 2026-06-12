# Loading-Gate, Query-Caching & Tab-Animation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tab-Wechsel ohne Default-Flackern — erster Besuch zeigt einen zentralen `// LADE …`-Loader, Rückkehr zeigt gecachten Inhalt sofort; dazu eine sanfte Fade/Slide-Animation beim Seitenwechsel.

**Architecture:** TanStack Query v5 übernimmt Fetching + Caching aller Initial-Daten (Query-Hooks in `src/hooks/queries.ts`). Jede Seite gated ihr Rendering auf `isPending` ihrer Queries mit einer gemeinsamen `PageLoader`-Komponente. Mutationen invalidieren Queries statt des bisherigen `reloadKey`-Zählers. Die Tab-Animation ist eine reine CSS-Keyframe-Animation über `key={page}` in `App.tsx`.

**Tech Stack:** React 19, TypeScript, Vite, Tailwind 3, `@tanstack/react-query` v5 (neu).

**Spec:** `docs/superpowers/specs/2026-06-12-loading-cache-tab-animation-design.md`

**Wichtig — Verifikation statt TDD:** Das Frontend hat keine Test-Infrastruktur (kein vitest/jest im `frontend/package.json`). Verifikation pro Task: `npm run typecheck` (und am Ende `npm run lint` + `npm run build`). Alle npm-Befehle laufen in `/home/bnau/workspace/NauAssist/frontend`. Manuelle Browser-Verifikation ist Task 9.

**Commit-Konvention:** Reine deutsche Subject-Lines, **kein** `Co-Authored-By:`-Trailer, kein Body (siehe `git log`).

**Branch:** Vor Task 1 anlegen: `git checkout -b feat/loading-cache-tab-animation` (von `main`).

---

## Datei-Übersicht

| Datei | Änderung |
|---|---|
| `frontend/package.json` | `@tanstack/react-query` Dependency |
| `frontend/src/main.tsx` | `QueryClientProvider` |
| `frontend/src/components/nau/PageLoader.tsx` | **neu** — zentraler Mono-Loader |
| `frontend/tailwind.config.js` | Keyframes/Animation `page-in` |
| `frontend/src/App.tsx` | Animations-Wrapper `key={page}` |
| `frontend/src/hooks/queries.ts` | **neu** — Query-Keys + Hooks |
| `frontend/src/components/calendar/CalendarBoard.tsx` | Queries + Gate, `reloadKey` raus |
| `frontend/src/components/calendar/WhatsNext.tsx` | Query statt Eigen-Fetch |
| `frontend/src/components/WeekViewModal.tsx` | `reloadKey`-Prop raus |
| `frontend/src/hooks/useChat.ts` | History-Query, Invalidierung statt `calendarReloadKey` |
| `frontend/src/components/ChatView.tsx` | Gate + Props-Anpassung |
| `frontend/src/components/pages/RecommendationsPage.tsx` | Query + Gate |
| `frontend/src/components/pages/SettingsPage.tsx` | Queries + Gate |

---

### Task 1: TanStack Query installieren + Provider

**Files:**
- Modify: `frontend/package.json` (via npm install)
- Modify: `frontend/src/main.tsx`

- [ ] **Step 1: Branch anlegen**

```bash
cd /home/bnau/workspace/NauAssist && git checkout -b feat/loading-cache-tab-animation
```

- [ ] **Step 2: Dependency installieren**

```bash
cd /home/bnau/workspace/NauAssist/frontend && npm install @tanstack/react-query
```

Expected: `package.json` enthält `"@tanstack/react-query": "^5.x"`.

- [ ] **Step 3: Provider in `main.tsx` einbauen**

Komplette neue Datei `frontend/src/main.tsx`:

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import "./index.css";
import App from "./App.tsx";
import { AuthGate } from "./lib/auth.tsx";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthGate>
        <App />
      </AuthGate>
    </QueryClientProvider>
  </StrictMode>,
);

if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker
      .register("/sw.js")
      .catch((err) => console.warn("Service-Worker-Registrierung fehlgeschlagen:", err));
  });
}
```

- [ ] **Step 4: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0, keine Fehler.

- [ ] **Step 5: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/package.json frontend/package-lock.json frontend/src/main.tsx && git commit -m "TanStack Query: Dependency + QueryClientProvider"
```

---

### Task 2: PageLoader-Komponente

**Files:**
- Create: `frontend/src/components/nau/PageLoader.tsx`

- [ ] **Step 1: Komponente anlegen**

```tsx
/**
 * Zentraler Ladezustand für ganze Seiten-Inhalte. Header/Tab-Leiste bleiben
 * außerhalb sichtbar; der Loader füllt den Content-Bereich (flex-1).
 */
export function PageLoader({ label = "LADE" }: { label?: string }) {
  return (
    <div className="flex flex-1 items-center justify-center py-24">
      <span className="animate-pulse font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
        {`// ${label} …`}
      </span>
    </div>
  );
}
```

- [ ] **Step 2: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0. (Die Komponente ist noch unbenutzt — `noUnusedLocals` betrifft nur lokale Variablen, kein Fehler.)

- [ ] **Step 3: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/src/components/nau/PageLoader.tsx && git commit -m "PageLoader-Komponente im Mono-Stil"
```

---

### Task 3: Tab-Wechsel-Animation

**Files:**
- Modify: `frontend/tailwind.config.js` (Abschnitt `theme.extend.keyframes` / `theme.extend.animation`)
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Keyframes + Animation in Tailwind-Config ergänzen**

In `frontend/tailwind.config.js` innerhalb `theme.extend.keyframes` (neben den bestehenden Einträgen wie `accordion-down`) ergänzen:

```js
"page-in": {
  from: { opacity: "0", transform: "translateY(8px)" },
  to: { opacity: "1", transform: "translateY(0)" },
},
```

Und innerhalb `theme.extend.animation`:

```js
"page-in": "page-in 180ms ease-out",
```

- [ ] **Step 2: App.tsx — Seiten in animierten Wrapper packen**

Die vier `if`-Early-Returns in `frontend/src/App.tsx` (Zeilen 31–46) durch einen einzigen Return mit Wrapper ersetzen. `key={page}` lässt React den Wrapper bei jedem Tab-Wechsel neu mounten, wodurch die Animation erneut läuft; `motion-safe:` respektiert `prefers-reduced-motion`:

```tsx
  return (
    <div key={page} className="motion-safe:animate-page-in">
      {page === "settings" ? (
        <SettingsPage onNavigate={setPage} />
      ) : page === "calendar" ? (
        <CalendarPage onNavigate={setPage} />
      ) : page === "recommendations" ? (
        <RecommendationsPage
          onNavigate={setPage}
          focusSuggestionId={focusSuggestionId}
          onFocusHandled={() => setFocusSuggestionId(null)}
        />
      ) : (
        <ChatView onNavigate={setPage} />
      )}
    </div>
  );
```

- [ ] **Step 3: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0.

- [ ] **Step 4: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/tailwind.config.js frontend/src/App.tsx && git commit -m "Tab-Wechsel: Fade/Slide-Animation beim Seitenwechsel"
```

---

### Task 4: Query-Keys + Query-Hooks

**Files:**
- Create: `frontend/src/hooks/queries.ts`

- [ ] **Step 1: Datei anlegen**

```ts
import { useCallback } from "react";
import { endOfDay, format, startOfDay } from "date-fns";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { getCalendarRange, NotConnectedError } from "@/api/calendar";
import { getCalendarSettings } from "@/api/calendar-settings";
import type { SuggestionStatus } from "@/api/suggestions";

/** Zentrale Query-Keys — Prefixe für partielle Invalidierung. */
export const queryKeys = {
  calendarSettings: ["calendar-settings"] as const,
  calendarRangePrefix: ["calendar-range"] as const,
  calendarRange: (fromIso: string, toIso: string) =>
    ["calendar-range", fromIso, toIso] as const,
  calendarTodayPrefix: ["calendar-today"] as const,
  calendarToday: (day: string) => ["calendar-today", day] as const,
  suggestionsPrefix: ["suggestions"] as const,
  suggestions: (filter: SuggestionStatus | "all") => ["suggestions", filter] as const,
  llmSettings: ["llm-settings"] as const,
  ollamaSettings: ["ollama-settings"] as const,
  capabilities: ["capabilities"] as const,
  chatHistory: ["chat-history"] as const,
};

/** NotConnected (409) ändert sich nicht von allein — kein Retry darauf. */
function retryUnlessNotConnected(failureCount: number, error: Error): boolean {
  return !(error instanceof NotConnectedError) && failureCount < 1;
}

export function useCalendarSettingsQuery() {
  return useQuery({
    queryKey: queryKeys.calendarSettings,
    queryFn: getCalendarSettings,
  });
}

export function useCalendarRangeQuery(from: Date, to: Date, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.calendarRange(from.toISOString(), to.toISOString()),
    queryFn: () => getCalendarRange(from, to),
    enabled,
    placeholderData: keepPreviousData,
    retry: retryUnlessNotConnected,
  });
}

export function useTodayEventsQuery() {
  const day = format(new Date(), "yyyy-MM-dd");
  return useQuery({
    queryKey: queryKeys.calendarToday(day),
    queryFn: () => getCalendarRange(startOfDay(new Date()), endOfDay(new Date())),
    retry: retryUnlessNotConnected,
  });
}

/** Invalidiert Kalender-Raster + Heute-Sidebar — Ersatz für den alten reloadKey. */
export function useInvalidateCalendar() {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.calendarRangePrefix });
    void queryClient.invalidateQueries({ queryKey: queryKeys.calendarTodayPrefix });
  }, [queryClient]);
}
```

- [ ] **Step 2: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0.

- [ ] **Step 3: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/src/hooks/queries.ts && git commit -m "Query-Hooks und zentrale Query-Keys"
```

---

### Task 5: Kalender auf Queries umstellen (CalendarBoard, WhatsNext, reloadKey raus)

**Files:**
- Modify: `frontend/src/components/calendar/CalendarBoard.tsx`
- Modify: `frontend/src/components/calendar/WhatsNext.tsx`
- Modify: `frontend/src/components/WeekViewModal.tsx`
- Modify: `frontend/src/components/ChatView.tsx` (nur Props der CalendarBoard/WeekViewModal-Aufrufe)
- Modify: `frontend/src/hooks/useChat.ts` (nur `bumpCalendarReload`/`calendarReloadKey`)

- [ ] **Step 1: `WhatsNext.tsx` auf Query umstellen**

Den kompletten State-/Fetch-Block (Zeilen 23–46: `useState raw/loading/error` + `useEffect`) und die Props `reloadKey` entfernen. Neuer Kopf der Komponente:

```tsx
import { useMemo } from "react";
import { format } from "date-fns";
import { de } from "date-fns/locale";
import { NotConnectedError } from "@/api/calendar";
import { useTodayEventsQuery } from "@/hooks/queries";
import { parseEvents, type ParsedEvent } from "./utils";
import type { PopoverState } from "./EventPopover";

interface WhatsNextProps {
  onHoverEvent: (state: PopoverState | null) => void;
  onClickEvent: (state: PopoverState) => void;
}

export function WhatsNext({ onHoverEvent, onClickEvent }: WhatsNextProps) {
  const query = useTodayEventsQuery();
  // Nicht verbunden → wie "keine Termine" behandeln (Board zeigt den Hinweis).
  const notConnected = query.error instanceof NotConnectedError;
  const raw = useMemo(
    () => (notConnected ? [] : (query.data ?? [])),
    [notConnected, query.data],
  );
  const errorMessage =
    query.error && !notConnected ? query.error.message : null;
```

Der `items`-`useMemo` arbeitet weiter auf `raw` (statt des alten States gleichen Namens); `startOfDay`/`endOfDay` werden nicht mehr gebraucht (leben jetzt in `useTodayEventsQuery`). Die Render-Verzweigung (Zeilen 68 ff.) anpassen:

```tsx
      {query.isPending ? (
        <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
          // LADE …
        </div>
      ) : errorMessage ? (
        <div className="font-mono text-[10px] tracking-mono text-nau-danger">
          // {errorMessage}
        </div>
      ) : items.length === 0 ? (
```

(Rest der Datei unverändert.)

- [ ] **Step 2: `CalendarBoard.tsx` auf Queries umstellen**

a) Imports: `NotConnectedError` bleibt; `getCalendarRange`, `getCalendarSettings`, `type CalendarSettings`, `type CalendarEvent` werden nicht mehr direkt gebraucht und fliegen raus. Neu dazu:

```tsx
import { PageLoader } from "@/components/nau/PageLoader";
import {
  useCalendarRangeQuery,
  useCalendarSettingsQuery,
  useTodayEventsQuery,
} from "@/hooks/queries";
```

b) Props: `reloadKey?: number;` aus `CalendarBoardProps` und aus der Destrukturierung entfernen.

c) Die States `settings`, `rawEvents`, `loading`, `error`, `notConnected` (Zeilen 59–63) und die beiden Fetch-`useEffect`s (Zeilen 105–134) ersetzen durch (nach der `range`-useMemo-Zeile platzieren, da `eventsQuery` `range` braucht):

```tsx
  const settingsQuery = useCalendarSettingsQuery();
  const settings = settingsQuery.data ?? null;
  const connected = settings?.isConnected === true;

  const eventsQuery = useCalendarRangeQuery(range.from, range.to, connected);
  const todayQuery = useTodayEventsQuery();

  const rawEvents = useMemo(() => eventsQuery.data ?? [], [eventsQuery.data]);
  const notConnected =
    (settings !== null && !settings.isConnected) ||
    eventsQuery.error instanceof NotConnectedError;
  const initialPending =
    settingsQuery.isPending ||
    (connected && (eventsQuery.isPending || todayQuery.isPending));
  const errorMessage = settingsQuery.error
    ? settingsQuery.error.message
    : eventsQuery.error && !(eventsQuery.error instanceof NotConnectedError)
      ? eventsQuery.error.message
      : null;
```

d) Vor dem bestehenden `if (notConnected)`-Return das Initial-Gate einfügen (compact = eingebettet im Chat → kleiner Platzhalter statt PageLoader, siehe Spec):

```tsx
  if (initialPending) {
    if (compact) {
      return (
        <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-10 text-center font-mono text-[11px] tracking-mono text-nau-fg-dim">
          // LADE KALENDER …
        </div>
      );
    }
    return <PageLoader label="LADE KALENDER" />;
  }
```

Achtung: `const compact = variant === "compact";` steht aktuell erst in Zeile 178 — diese Zeile vor das Gate hochziehen.

e) Im `grid`-Ausdruck: `error` → `errorMessage`, und den Zweig `loading && rawEvents.length === 0 ? (… // LADE EVENTS …)` komplett entfernen (das Gate übernimmt den Erstladefall; bei Wochen-Navigation hält `keepPreviousData` das alte Raster).

f) `whatsNext`-Element: Prop `reloadKey={rawEvents.length}` entfernen.

- [ ] **Step 3: `WeekViewModal.tsx` — reloadKey entfernen**

`reloadKey?: number;` aus den Props, aus der Destrukturierung und aus dem `<CalendarBoard …/>`-Aufruf (Zeile 53) entfernen.

- [ ] **Step 4: `useChat.ts` — `bumpCalendarReload` wird Invalidierung**

a) Import ergänzen: `import { useInvalidateCalendar } from "@/hooks/queries";`
b) `calendarReloadKey: number;` aus `ChatState` (Zeile 59) entfernen; State-Zeile `const [calendarReloadKey, setCalendarReloadKey] = useState(0);` (Zeile 141) entfernen.
c) Zeile 349 ersetzen:

```ts
  const bumpCalendarReload = useInvalidateCalendar();
```

d) `calendarReloadKey,` aus dem Return-Objekt (Zeile 370) entfernen. (`bumpCalendarReload` bleibt im Return — die Modals nutzen es weiter über `onCreated`/`onMutated`.)

- [ ] **Step 5: `ChatView.tsx` — Props anpassen**

`calendarReloadKey,` aus der `useChat()`-Destrukturierung (Zeile 54) entfernen; `reloadKey={calendarReloadKey}` aus dem `<CalendarBoard …/>` (Zeile 127) und dem `<WeekViewModal …/>` (Zeile 143) entfernen.

- [ ] **Step 6: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0. (Häufige Stolperstelle: verwaiste Imports in `CalendarBoard.tsx`/`WhatsNext.tsx` — `noUnusedLocals` schlägt an; betroffene Imports entfernen.)

- [ ] **Step 7: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/src && git commit -m "Kalender: Query-Caching + Loading-Gate statt reloadKey"
```

---

### Task 6: Chat-History aus dem Query-Cache + Gate

**Files:**
- Modify: `frontend/src/hooks/useChat.ts`
- Modify: `frontend/src/components/ChatView.tsx`

- [ ] **Step 1: `useChat.ts` — History-Query statt Initial-Effect**

a) Imports ergänzen/ändern:

```ts
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys, useInvalidateCalendar } from "@/hooks/queries";
```

b) In `ChatState` ergänzen: `historyPending: boolean;`

c) Im Hook-Körper nach den States:

```ts
  const queryClient = useQueryClient();
  const invalidateCalendar = useInvalidateCalendar();
  const historyQuery = useQuery({
    queryKey: queryKeys.chatHistory,
    queryFn: getHistory,
  });

  // Bubbles aus dem Cache übernehmen — aber nie während eines laufenden
  // Streams (sonst würde die live wachsende Antwort überschrieben).
  // dataUpdatedAt-Guard: nach Stream-Ende nicht mit altem Cache-Stand
  // zurückspringen, sondern erst beim nächsten frischen Fetch syncen.
  const lastSyncedAtRef = useRef(0);
  useEffect(() => {
    if (sending) return;
    if (!historyQuery.data) return;
    if (historyQuery.dataUpdatedAt === lastSyncedAtRef.current) return;
    lastSyncedAtRef.current = historyQuery.dataUpdatedAt;
    setBubbles(mapHistory(historyQuery.data.messages, historyQuery.data.markers));
  }, [sending, historyQuery.data, historyQuery.dataUpdatedAt]);
```

d) `reloadHistory` (Zeilen 145–148) ersetzen durch:

```ts
  const reloadHistory = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.chatHistory });
  }, [queryClient]);
```

e) Den alten Initial-History-`useEffect` (Zeilen 150–164) ersetzen durch reinen Abort-Cleanup:

```ts
  useEffect(() => {
    return () => {
      abortRef.current?.abort();
    };
  }, []);
```

f) Im `send`-Callback, im `.finally(...)` des `sendMessage`-Aufrufs (Zeile 322 ff.) ergänzen — der Agent kann Termine angelegt haben, und der Cache soll die finalen Server-IDs kennen:

```ts
        .finally(() => {
          setSending(false);
          setToolStatus(null);
          abortRef.current = null;
          invalidateCalendar();
          void queryClient.invalidateQueries({ queryKey: queryKeys.chatHistory });
        });
```

g) Dependency-Array des `send`-`useCallback` (Zeile 328) erweitern: `[sending, reloadHistory, invalidateCalendar, queryClient]`.

h) Return-Objekt: `error` durch Merge ersetzen und `historyPending` ergänzen:

```ts
    error: error ?? (historyQuery.error ? historyQuery.error.message : null),
    historyPending: historyQuery.isPending,
```

(Die bisherige Zeile `error,` ersetzen; `historyPending,` direkt danach einfügen.)

- [ ] **Step 2: `ChatView.tsx` — Gate einbauen**

a) `historyPending,` in der `useChat()`-Destrukturierung ergänzen; Import `import { PageLoader } from "./nau/PageLoader";` hinzufügen.

b) Nach den Hooks/Callbacks (nach `onPickSlot`, vor `const lastBubble = …`) das Gate einfügen — wichtig: **nach** allen Hook-Aufrufen, sonst bricht die Rules-of-Hooks:

```tsx
  if (historyPending) {
    return (
      <div className="flex h-screen flex-col bg-nau-bg text-nau-fg pb-[calc(3.5rem+env(safe-area-inset-bottom))] lg:pb-0">
        <Header onOpenSettings={() => onNavigate("settings")} />
        <main className="flex min-h-0 flex-1 flex-col">
          <PageLoader />
        </main>
        <MobileTabBar current="chat" onSelect={onNavigate} />
      </div>
    );
  }
```

- [ ] **Step 3: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0.

- [ ] **Step 4: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/src/hooks/useChat.ts frontend/src/components/ChatView.tsx && git commit -m "Chat: History aus Query-Cache + Loading-Gate"
```

---

### Task 7: Empfehlungen auf Query umstellen

**Files:**
- Modify: `frontend/src/components/pages/RecommendationsPage.tsx`

- [ ] **Step 1: Query statt Eigen-Fetch**

a) Imports:

```tsx
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/hooks/queries";
import { PageLoader } from "@/components/nau/PageLoader";
```

(`useCallback` wird danach nicht mehr gebraucht — aus dem React-Import entfernen.)

b) Die States `items`, `loading` und den `reload`-Callback samt `useEffect(() => { void reload(); }, [reload])` (Zeilen 36, 38, 51–66) ersetzen durch:

```tsx
  const queryClient = useQueryClient();
  const suggestionsQuery = useQuery({
    queryKey: queryKeys.suggestions(filter),
    queryFn: () => listSuggestions(filter === "all" ? undefined : filter),
    placeholderData: keepPreviousData,
  });
  const items = suggestionsQuery.data ?? [];
  const reloadSuggestions = () =>
    queryClient.invalidateQueries({ queryKey: queryKeys.suggestionsPrefix });
```

(`filter`, `polling`, `error`, `pollMessage` und der Deep-Link-`useEffect` bleiben unverändert — letzterer arbeitet weiter auf `items`.)

c) `replaceItem` ersetzen:

```tsx
  const replaceItem = (updated: SuggestionDto) =>
    queryClient.setQueryData<SuggestionDto[]>(queryKeys.suggestions(filter), (prev) =>
      prev?.map((s) => (s.id === updated.id ? updated : s)),
    );
```

d) In `onPollNow`: `await reload();` → `await reloadSuggestions();`
   In `onDismiss`: `await reload();` → `await reloadSuggestions();`

e) Fehler-Anzeige zusammenführen — vor dem `return`:

```tsx
  const displayError =
    error ?? (suggestionsQuery.error ? suggestionsQuery.error.message : null);
```

Im JSX `{error && (` → `{displayError && (` und `[ ! ] {error}` → `[ ! ] {displayError}`.

f) Gate + Listen-Zweig: den Zweig `loading && items.length === 0 ? <EmptyState text="Lade …" /> :` entfernen. Stattdessen den gesamten Content-Block unterhalb des Headers gaten. Konkret: das `<div className="mx-auto w-full max-w-[1100px] flex-1 …">`-Element bekommt als Inhalt bei Erstladung nur den Loader:

```tsx
      <div className="mx-auto w-full max-w-[1100px] flex-1 px-4 py-6 lg:px-8 lg:py-10">
        {suggestionsQuery.isPending ? (
          <PageLoader label="LADE EMPFEHLUNGEN" />
        ) : (
          <>
            {/* bisheriger Inhalt: Titel-Zeile, Status-Tabs, pollMessage,
                displayError, Liste/EmptyState — unverändert einrücken */}
          </>
        )}
      </div>
```

Hinweis: Beim Filterwechsel greift `keepPreviousData` → `isPending` bleibt `false`, kein Loader, alte Liste steht bis die neue da ist.

- [ ] **Step 2: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0.

- [ ] **Step 3: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/src/components/pages/RecommendationsPage.tsx && git commit -m "Empfehlungen: Query-Caching + Loading-Gate, Leerzustand-Flackern behoben"
```

---

### Task 8: Settings auf Queries umstellen

**Files:**
- Modify: `frontend/src/components/pages/SettingsPage.tsx`

- [ ] **Step 1: Queries + abgeleitete Werte**

a) Imports:

```tsx
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys, useCalendarSettingsQuery } from "@/hooks/queries";
import { PageLoader } from "@/components/nau/PageLoader";
```

b) Die States `llm`, `ollama`, `calendar`, `caps`, `topError` (Zeilen 264–268) und die beiden Lade-`useEffect`s (Zeilen 270–284) ersetzen durch:

```tsx
  const queryClient = useQueryClient();
  const llmQuery = useQuery({ queryKey: queryKeys.llmSettings, queryFn: getLlmSettings });
  const ollamaQuery = useQuery({
    queryKey: queryKeys.ollamaSettings,
    queryFn: getOllamaSettings,
  });
  const calendarQuery = useCalendarSettingsQuery();
  const capsQuery = useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: getCapabilities,
  });

  // Lokale Kopien gewinnen nach Saves; Query-Daten füllen initial.
  // Die Setter spiegeln Saves in den Cache, damit andere Seiten
  // (z. B. CalendarBoard via calendar-settings) frische Werte sehen.
  const [llmLocal, setLlmLocal] = useState<LlmSettings | null>(null);
  const [ollamaLocal, setOllamaLocal] = useState<OllamaSettings | null>(null);
  const [calendarLocal, setCalendarLocal] = useState<CalendarSettings | null>(null);

  const llm = llmLocal ?? llmQuery.data ?? null;
  const ollama = ollamaLocal ?? ollamaQuery.data ?? null;
  const calendar = calendarLocal ?? calendarQuery.data ?? null;

  const setLlm = (l: LlmSettings) => {
    setLlmLocal(l);
    queryClient.setQueryData(queryKeys.llmSettings, l);
  };
  const setOllama = (o: OllamaSettings) => {
    setOllamaLocal(o);
    queryClient.setQueryData(queryKeys.ollamaSettings, o);
  };
  const setCalendar = (c: CalendarSettings) => {
    setCalendarLocal(c);
    queryClient.setQueryData(queryKeys.calendarSettings, c);
  };

  // Capabilities-Fehler → konservative Defaults (wie bisheriger catch-Fallback).
  const caps =
    capsQuery.data ??
    (capsQuery.isError
      ? { whatsApp: false, auth: { enabled: false, loginUrl: "/auth/login" } }
      : null);

  const topError =
    llmQuery.error?.message ??
    ollamaQuery.error?.message ??
    calendarQuery.error?.message ??
    null;

  const initialPending =
    llmQuery.isPending ||
    ollamaQuery.isPending ||
    calendarQuery.isPending ||
    capsQuery.isPending;
```

(Die Sektionen `LlmSection`/`CalendarSection` erhalten `setLlm`/`setOllama`/`setCalendar` unverändert als Props — Signaturen bleiben gleich.)

c) Gate im JSX: Der Inhalt von `<main …>` (ab dem Titel-`<div className="mb-9">`) wird bei Erstladung durch den Loader ersetzt:

```tsx
      <main className="flex max-w-[980px] flex-col px-4 pb-12 pt-6 lg:px-16 lg:pb-20 lg:pt-10">
        {initialPending ? (
          <PageLoader label="LADE EINSTELLUNGEN" />
        ) : (
          <>
            {/* bisheriger main-Inhalt unverändert:
                Titel, topError-Banner, LlmSection, CalendarSection,
                PersonaSection, PushSection, ImapSection, WhatsAppSection,
                AccountFooter, Zurück-Button */}
          </>
        )}
      </main>
```

(Das `flex flex-col` auf `main` ist nötig, damit `PageLoader` mit `flex-1` zentriert; der bisherige Inhalt verhält sich in einem Flex-Column-Container mit `<>…</>` identisch zu vorher.)

- [ ] **Step 2: Typecheck**

Run: `cd /home/bnau/workspace/NauAssist/frontend && npm run typecheck`
Expected: exit 0. (Stolperstelle: `useEffect` evtl. nicht mehr benutzt im Top-Level — Import-Liste prüfen; `LlmSettings`/`OllamaSettings`/`CalendarSettings`-Typen müssen importiert bleiben.)

- [ ] **Step 3: Commit**

```bash
cd /home/bnau/workspace/NauAssist && git add frontend/src/components/pages/SettingsPage.tsx && git commit -m "Settings: Query-Caching + Loading-Gate"
```

---

### Task 9: Gesamt-Verifikation + Doku

**Files:**
- Modify (außerhalb des Repos): `/home/bnau/workspace/BenediktsMind/1. Projects/NauAssist/Doings.md` + neue Notiz

- [ ] **Step 1: Lint + Build**

```bash
cd /home/bnau/workspace/NauAssist/frontend && npm run lint && npm run build
```

Expected: beide exit 0. Lint-Verstöße (z. B. ungenutzte Imports, react-hooks/exhaustive-deps) beheben und ggf. als Fixup in den jeweiligen Task-Commit amenden oder als eigener Commit `Lint-Fixes nach Query-Umbau`.

- [ ] **Step 2: Manuelle Verifikation (Browser)**

Dev-Server starten (`npm run dev` im Frontend; Backend muss laufen, sonst zeigen alle Tabs Fehlerzustände — das ist dann auch ein gültiger Teil-Test der Error-Pfade). Checkliste:

1. Erstbesuch Kalender-Tab → zentrierter `// LADE KALENDER …`, danach komplettes Raster + Heute-Sidebar gleichzeitig.
2. Zurück zu Chat, wieder zu Kalender → Inhalt sofort (Cache), kein Loader.
3. Wochen-Navigation (`<` / `>`) → altes Raster bleibt stehen, kein Flackern.
4. Empfehlungen-Erstbesuch → Loader, kein kurzes „Keine offenen Empfehlungen“.
5. Chat: Nachricht senden, Streaming läuft unverändert; währenddessen Tab wechseln ist nicht nötig zu testen (Seite unmountet beim Wechsel — bekanntes Verhalten, unverändert).
6. Settings-Erstbesuch → Loader, dann alle Sektionen gleichzeitig.
7. Tab-Wechsel Mobile (DevTools-Mobile-Ansicht) → Fade/Slide-Animation sichtbar.
8. Termin über `/termin`-Modal anlegen → Kalender-Raster + Heute-Sidebar aktualisieren sich (Invalidierung).

Wenn der Implementierer keinen Browser-Zugriff hat: Punkte 1–8 als offene Verifikationspunkte an Benedikt berichten.

- [ ] **Step 3: Obsidian-Doku (Pflicht, siehe Memory)**

Im Vault `/home/bnau/workspace/BenediktsMind`: vault-eigenes `CLAUDE.md` lesen und befolgen; unter `1. Projects/NauAssist/` (1) `Doings.md`-Kanban aktualisieren (Karte für dieses Feature in ✅ Completed bzw. 🧪 Testing, je nach Stand der manuellen Verifikation), (2) Notiz nach `Templates/Notiz.md` anlegen (Frontmatter `erstellt: 2026-06-12`, `projekt`, `tags`; Abschnitte Zusammenfassung/Details/Offene Punkte/Verweise; auf Spec + Plan im Repo verweisen). Deutsch, Daten als YYYY-MM-DD.

- [ ] **Step 4: Abschluss**

`superpowers:finishing-a-development-branch` verwenden (Merge in `main` oder PR — Benedikt entscheidet; bisherige Praxis im Repo: PRs, siehe Merge-Commits im Log).
