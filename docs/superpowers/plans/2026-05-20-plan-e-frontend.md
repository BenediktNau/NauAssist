# Plan E — Frontend (Chat-UI) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein lokales React-Frontend bauen, das den Backend-MVP aus Plan A–D bedient: Eingabefeld zum Pasten einer Terminanfrage, live-gestreamte Agent-Antworten via SSE, klickbare Slot-Vorschläge zur Bestätigung, History-Load nach Page-Reload. Damit ist der MVP-Workflow „User pastet → Agent schlägt vor → User bestätigt → Termin gebucht" end-to-end durchspielbar.

**Architecture:** Vite + React 18 + TypeScript-strict mit Tailwind und shadcn/ui-Primitives. Single-Page, ein einziges Chat-View. Datenfluss läuft durch einen `useChat`-Hook, der (a) beim Mount per `GET /api/chat/history` die letzten 50 Nachrichten lädt und (b) bei jedem Submit eine SSE-Verbindung zu `POST /api/chat` aufmacht und die Events in den lokalen State faltet. Da `EventSource` kein POST kann, nutzen wir `@microsoft/fetch-event-source`. CORS wird nicht im Backend konfiguriert — stattdessen leitet ein Vite-Dev-Proxy `/api` auf das Backend (Dev-Port 5182) weiter.

**Tech Stack:** Vite 5+ · React 18 · TypeScript strict · Tailwind CSS 3 · shadcn/ui (`button`, `card`, `textarea`, `scroll-area`) · `@microsoft/fetch-event-source` für SSE-POST · `Intl.DateTimeFormat` für deutsche Datumsformatierung.

**Bezug zur Spec:** `docs/superpowers/specs/2026-05-19-kalender-agent-mvp-design.md`, Abschnitte 2 (MVP-Scope), 4 (Frontend-Stack), 5 (Solution-Struktur), 6.1 (Chat-Surface + SSE-Event-Protokoll), 9 (keine automatisierten Frontend-Tests, manuelles Browser-Testing).

**Was am Ende dieses Plans steht:**
- Neues Top-Level-Verzeichnis `frontend/` mit eigenständigem `package.json`
- `npm run dev` öffnet ein Chat-UI auf `http://localhost:5173`, das gegen das Backend auf `http://localhost:5182` redet
- Page-Load lädt History und rendert alle Bubbles inkl. ggf. persistierter Slot-Vorschläge
- Senden eines Texts streamt Tokens live in eine wachsende Agent-Bubble
- `tool_started`/`tool_finished` werden als dezenter Status unter der laufenden Bubble angezeigt
- `proposals`-Event rendert klickbare Slot-Cards; ein Klick sendet die Slot-Bestätigung als neue User-Message
- `done`-Event finalisiert die Bubble und speichert die echte `messageId`
- `error`-Event zeigt einen Inline-Hinweis und beendet den Stream
- Backend-MVP + Frontend-MVP zusammen erfüllen Spec §2 vollständig

---

## Datei-Übersicht (für diesen Plan)

**Neu anzulegen:**

| Pfad | Verantwortung |
|---|---|
| `frontend/package.json` | npm-Manifest, Scripts (`dev`, `build`, `preview`, `typecheck`, `lint`) |
| `frontend/tsconfig.json`, `tsconfig.app.json`, `tsconfig.node.json` | Vite-Template-Defaults, strict aktiviert |
| `frontend/vite.config.ts` | React-Plugin + `/api`-Proxy auf `http://localhost:5182` |
| `frontend/index.html` | Root-HTML, Titel „NauAssist" |
| `frontend/tailwind.config.js`, `postcss.config.js` | Tailwind-Setup |
| `frontend/components.json` | shadcn/ui-Konfiguration |
| `frontend/src/main.tsx` | React-Root-Mount |
| `frontend/src/App.tsx` | Top-Level-Komponente, hängt `useChat` und `ChatView` zusammen |
| `frontend/src/index.css` | Tailwind-Direktiven + shadcn-CSS-Vars |
| `frontend/src/lib/utils.ts` | shadcn-`cn`-Helper |
| `frontend/src/api/types.ts` | TS-Typen: `MessageDto`, `SlotInfo`, `SseEvent`-Union, `RuleDto` |
| `frontend/src/api/client.ts` | `getHistory()`, `listRules()`, `deleteRule()` (REST-Wrapper) |
| `frontend/src/api/chatStream.ts` | `sendMessage(text, onEvent, signal)` — wickelt `fetch-event-source` ab |
| `frontend/src/hooks/useChat.ts` | State + History-Load + Stream-Anbindung |
| `frontend/src/components/ChatView.tsx` | Layout: ScrollArea + Bubble-Liste + Eingabe |
| `frontend/src/components/ChatBubble.tsx` | Eine Nachricht (User oder Assistant), rendert Text + optional Slot-Cards |
| `frontend/src/components/SlotCard.tsx` | Eine klickbare Slot-Karte |
| `frontend/src/components/MessageInput.tsx` | Textarea + Send-Button, Enter-zum-Senden |
| `frontend/src/components/ui/*` | von shadcn generierte Primitives (button, card, textarea, scroll-area) |
| `frontend/.gitignore` | `node_modules`, `dist`, `.vite` |

**Zu modifizieren:**

| Pfad | Änderung |
|---|---|
| (kein Backend-Code) | — Plan E ist reines Frontend, das Backend bleibt unverändert |
| `/.gitignore` (Repo-Root) | (optional) `frontend/node_modules` ergänzen, falls nicht durch Wildcard abgedeckt |

---

## Annahmen, die für diesen Plan fixiert sind

- **Backend-Dev-Port:** `5182` (siehe `src/Backend/Properties/launchSettings.json`). Der Vite-Proxy zeigt fest auf diesen Port. Wer das Backend auf einem anderen Port startet, ändert nur `vite.config.ts`.
- **Frontend-Port:** Vite-Default `5173`. Kein Override.
- **SSE-POST:** Wir nutzen `@microsoft/fetch-event-source` statt der nativen `EventSource`, weil der Chat-Endpoint POST ist. Reconnect bleibt aus (`openWhenHidden: true`, kein Auto-Retry — wir wollen keine Doppel-Sends).
- **Keine automatisierten Tests** (Spec §9). Stattdessen `tsc --noEmit` als Build-Sanity-Check und manuelles Browser-Testing am Ende.
- **Locale:** Datums-/Zeitformatierung über `Intl.DateTimeFormat("de-DE", …)`. Keine externe Datums-Bibliothek.
- **Single Session:** das Frontend kennt keine Session-IDs — der Server hält die fest auf `"default"`. Das passt zu Spec §6.1.

---

## Task 1: Vite-Projekt bootstrappen

**Files:**
- Create: `frontend/package.json`
- Create: `frontend/tsconfig.json`, `frontend/tsconfig.app.json`, `frontend/tsconfig.node.json`
- Create: `frontend/index.html`
- Create: `frontend/vite.config.ts`
- Create: `frontend/src/main.tsx`
- Create: `frontend/src/App.tsx`
- Create: `frontend/src/index.css`
- Create: `frontend/.gitignore`
- Create: `frontend/eslint.config.js` (aus Template übernommen)

- [ ] **Step 1: Vite-Template ausrollen**

Im Repo-Root:

```bash
npm create vite@latest frontend -- --template react-ts
```

Das legt das komplette `frontend/`-Skelett an (React 18 + TS, Vite 5+).

- [ ] **Step 2: Dependencies installieren**

```bash
cd frontend && npm install
```

Erwartet: `node_modules/` und `package-lock.json` werden angelegt. Keine Vulnerability-Warnings über Moderate.

- [ ] **Step 3: TypeScript-Strict aktivieren**

In `frontend/tsconfig.app.json` sicherstellen, dass folgende Felder gesetzt sind (Template hat das meiste schon):

```json
{
  "compilerOptions": {
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "noUncheckedSideEffectImports": true
  }
}
```

- [ ] **Step 4: Vite-Plain-Template ausräumen**

- `frontend/src/App.css` löschen
- `frontend/src/assets/react.svg` löschen
- `frontend/public/vite.svg` löschen
- `frontend/src/index.css` leeren (Tailwind-Direktiven kommen in Task 2)
- `frontend/src/App.tsx` ersetzen durch:

```tsx
export default function App() {
  return (
    <main className="min-h-screen">
      <h1>NauAssist</h1>
    </main>
  );
}
```

- `frontend/src/main.tsx` so lassen wie vom Template generiert (StrictMode + ReactDOM-Mount).

- [ ] **Step 5: `index.html`-Titel anpassen**

In `frontend/index.html` den `<title>`-Tag auf `<title>NauAssist</title>` setzen und `<link rel="icon">` rauswerfen, solange wir kein Favicon haben.

- [ ] **Step 6: Prettier installieren (Spec §9 verlangt es)**

```bash
cd frontend && npm install -D prettier
```

Dann eine minimale `frontend/.prettierrc.json` anlegen:

```json
{
  "semi": true,
  "singleQuote": false,
  "printWidth": 100,
  "trailingComma": "all"
}
```

- [ ] **Step 7: `npm-Scripts` ergänzen**

In `frontend/package.json` sicherstellen, dass folgende Scripts vorhanden sind:

```json
"scripts": {
  "dev": "vite",
  "build": "tsc --noEmit && vite build",
  "preview": "vite preview",
  "typecheck": "tsc --noEmit",
  "lint": "eslint .",
  "format": "prettier --write \"src/**/*.{ts,tsx,css,json}\"",
  "format:check": "prettier --check \"src/**/*.{ts,tsx,css,json}\""
}
```

(Das Template setzt das meiste; `typecheck`, `format`, `format:check` sind neu.)

- [ ] **Step 8: Build verifizieren**

```bash
cd frontend && npm run typecheck && npm run build && npm run format:check
```

Erwartet: `typecheck`/`build` grün. `format:check` darf erstmal Findings haben — dann einmal `npm run format` ausführen und nochmal `format:check`. Jetzt grün.

- [ ] **Step 9: Commit**

```bash
git add frontend/ .gitignore
git commit -m "Plan E Task 1: Vite + React + TS-strict Frontend-Skelett"
```

---

## Task 2: Tailwind CSS einrichten

**Files:**
- Create: `frontend/tailwind.config.js`
- Create: `frontend/postcss.config.js`
- Modify: `frontend/src/index.css`

- [ ] **Step 1: Tailwind + PostCSS + Autoprefixer installieren**

```bash
cd frontend && npm install -D tailwindcss@^3 postcss autoprefixer
npx tailwindcss init -p
```

Erwartet: `tailwind.config.js` und `postcss.config.js` werden generiert.

> **Wichtig:** Wir bleiben bei Tailwind v3, weil shadcn/ui-Defaults sich darauf stützen. Wer Tailwind v4 nutzen will, müsste shadcn-Components manuell anpassen — Out-of-Scope für MVP.

- [ ] **Step 2: `tailwind.config.js` konfigurieren**

```js
/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{ts,tsx}",
  ],
  theme: {
    extend: {},
  },
  plugins: [],
};
```

- [ ] **Step 3: Tailwind-Direktiven in `src/index.css` setzen**

```css
@tailwind base;
@tailwind components;
@tailwind utilities;
```

- [ ] **Step 4: Sanity-Check im Browser**

In `App.tsx` der `<h1>` testweise `className="text-3xl font-bold p-4"` geben, dann:

```bash
npm run dev
```

Erwartet: Im Browser auf `http://localhost:5173` ist „NauAssist" groß und fett. Danach Dev-Server stoppen.

- [ ] **Step 5: Build prüfen**

```bash
npm run typecheck && npm run build
```

Grün.

- [ ] **Step 6: Commit**

```bash
git add frontend/tailwind.config.js frontend/postcss.config.js frontend/package.json frontend/package-lock.json frontend/src/index.css frontend/src/App.tsx
git commit -m "Plan E Task 2: Tailwind CSS Setup"
```

---

## Task 3: shadcn/ui initialisieren + Basis-Components

**Files:**
- Create: `frontend/components.json`
- Create: `frontend/src/lib/utils.ts`
- Create: `frontend/src/components/ui/button.tsx`
- Create: `frontend/src/components/ui/card.tsx`
- Create: `frontend/src/components/ui/textarea.tsx`
- Create: `frontend/src/components/ui/scroll-area.tsx`
- Modify: `frontend/tsconfig.json`, `frontend/tsconfig.app.json`, `frontend/vite.config.ts` (Path-Alias `@/*`)
- Modify: `frontend/src/index.css` (shadcn CSS-Variablen)
- Modify: `frontend/tailwind.config.js` (shadcn-Theme-Erweiterungen)

- [ ] **Step 1: Path-Alias `@/*` einrichten**

In `frontend/tsconfig.json`:

```json
{
  "compilerOptions": {
    "baseUrl": ".",
    "paths": { "@/*": ["./src/*"] }
  }
}
```

In `frontend/tsconfig.app.json` dasselbe `paths`-Feld unter `compilerOptions` ergänzen.

In `frontend/vite.config.ts`:

```ts
import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
});
```

- [ ] **Step 2: Node-Types installieren (für `path`)**

```bash
cd frontend && npm install -D @types/node
```

- [ ] **Step 3: shadcn/ui initialisieren**

```bash
npx shadcn@latest init
```

Interaktive Antworten (in dieser Reihenfolge):
- Style: **Default**
- Base color: **Slate**
- CSS variables: **Yes**

shadcn legt `components.json`, schreibt `src/lib/utils.ts`, fügt CSS-Variablen in `src/index.css` ein und erweitert `tailwind.config.js`.

> Falls die CLI nach `tailwindcss-animate` fragt: zustimmen, sie installiert es automatisch.

- [ ] **Step 4: Basis-Komponenten installieren**

```bash
npx shadcn@latest add button card textarea scroll-area
```

Erwartet: Vier Dateien unter `frontend/src/components/ui/` werden angelegt.

- [ ] **Step 5: Build prüfen**

```bash
npm run typecheck && npm run build
```

Grün. Bei einem TS-Fehler in `utils.ts` (`clsx`/`tailwind-merge` fehlt): `npm install clsx tailwind-merge` und nochmal builden.

- [ ] **Step 6: Sanity-Check im Browser**

In `App.tsx` testweise einen shadcn-Button rendern:

```tsx
import { Button } from "@/components/ui/button";

export default function App() {
  return (
    <main className="min-h-screen p-4">
      <h1 className="text-2xl font-bold mb-4">NauAssist</h1>
      <Button>Test</Button>
    </main>
  );
}
```

`npm run dev` → der Button erscheint im shadcn-Default-Stil. Danach `App.tsx` zurück auf die schlanke Version (Button raus, Heading drin) und Dev-Server stoppen.

- [ ] **Step 7: Commit**

```bash
git add frontend/
git commit -m "Plan E Task 3: shadcn/ui Init + Basis-Components (button, card, textarea, scroll-area)"
```

---

## Task 4: Vite-Proxy auf das Backend

**Files:**
- Modify: `frontend/vite.config.ts`

- [ ] **Step 1: Proxy ergänzen**

`frontend/vite.config.ts`:

```ts
import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5182",
        changeOrigin: true,
      },
    },
  },
});
```

- [ ] **Step 2: Smoke-Test mit laufendem Backend**

In einem Terminal:

```bash
dotnet run --project src/Backend
```

In einem zweiten Terminal:

```bash
cd frontend && npm run dev
```

Im Browser auf `http://localhost:5173/api/chat/history` navigieren.

Erwartet: JSON-Response mit `{ "messages": [...] }` (kein CORS-Fehler, kein 404). Falls noch leer: `{ "messages": [] }`.

Beide Dev-Server wieder stoppen.

- [ ] **Step 3: Commit**

```bash
git add frontend/vite.config.ts
git commit -m "Plan E Task 4: Vite-Proxy für /api auf Backend-Port 5182"
```

---

## Task 5: API-Typen + REST-Client

**Files:**
- Create: `frontend/src/api/types.ts`
- Create: `frontend/src/api/client.ts`

- [ ] **Step 1: `api/types.ts` anlegen**

```ts
// Spiegelt die Backend-DTOs aus ChatEndpoints.cs und RulesEndpoints.cs.

export interface SlotInfo {
  start: string; // ISO-8601 mit Offset
  end: string;
  note: string | null;
}

export type MessageRole = "user" | "assistant";

export interface MessageDto {
  id: number;
  sessionId: string;
  role: MessageRole;
  content: string;
  proposalsJson: string | null;
  incomplete: boolean;
  createdAt: string;
}

export interface ChatHistoryDto {
  messages: MessageDto[];
}

export interface RuleDto {
  id: number;
  text: string;
  daysOfWeek: number;
  timeRangeStart: string | null;
  timeRangeEnd: string | null;
  hardness: "hard" | "soft";
  createdAt: string;
}

// SSE-Event-Union (Wire-Format laut SseWriter.cs)

export type SseEventName =
  | "token"
  | "tool_started"
  | "tool_finished"
  | "proposals"
  | "done"
  | "error";

export type SseEventData =
  | { event: "token"; data: { text: string } }
  | { event: "tool_started"; data: { name: string } }
  | { event: "tool_finished"; data: { name: string; ok: boolean } }
  | { event: "proposals"; data: SlotInfo[] }
  | { event: "done"; data: { messageId: number } }
  | { event: "error"; data: { message: string; correlationId: string | null } };
```

- [ ] **Step 2: `api/client.ts` anlegen**

```ts
import type { ChatHistoryDto, RuleDto } from "./types";

const HEADERS_JSON = { "Content-Type": "application/json" } as const;

export async function getHistory(): Promise<ChatHistoryDto> {
  const res = await fetch("/api/chat/history");
  if (!res.ok) {
    throw new Error(`History-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as ChatHistoryDto;
}

export async function listRules(): Promise<RuleDto[]> {
  const res = await fetch("/api/rules/");
  if (!res.ok) {
    throw new Error(`Rules-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as RuleDto[];
}

export async function deleteRule(id: number): Promise<void> {
  const res = await fetch(`/api/rules/${id}`, { method: "DELETE" });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Rule-Delete fehlgeschlagen: HTTP ${res.status}`);
  }
}

void HEADERS_JSON; // wird in chatStream.ts genutzt; hier nur exportiert
```

> `listRules`/`deleteRule` werden im UI v.a. erst über den Chat selbst genutzt (Tool-Calls). Wir stellen sie trotzdem bereit, damit ein späterer Rules-Manager direkt anbinden kann. YAGNI: `addRule` lassen wir bewusst weg — der Agent erledigt das via Chat.

- [ ] **Step 3: Build prüfen**

```bash
cd frontend && npm run typecheck && npm run build
```

Grün.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/
git commit -m "Plan E Task 5: API-Typen + REST-Client (history, rules)"
```

---

## Task 6: SSE-Stream-Client

**Files:**
- Create: `frontend/src/api/chatStream.ts`
- Modify: `frontend/package.json` (Dep: `@microsoft/fetch-event-source`)

- [ ] **Step 1: Dependency installieren**

```bash
cd frontend && npm install @microsoft/fetch-event-source
```

- [ ] **Step 2: `api/chatStream.ts` anlegen**

```ts
import { fetchEventSource } from "@microsoft/fetch-event-source";
import type { SseEventData, SseEventName } from "./types";

export type ChatStreamEvent = SseEventData;

export interface SendMessageOptions {
  message: string;
  onEvent: (ev: ChatStreamEvent) => void;
  signal: AbortSignal;
}

const KNOWN_EVENTS: ReadonlySet<SseEventName> = new Set([
  "token",
  "tool_started",
  "tool_finished",
  "proposals",
  "done",
  "error",
]);

function isKnownEvent(name: string): name is SseEventName {
  return KNOWN_EVENTS.has(name as SseEventName);
}

export async function sendMessage({ message, onEvent, signal }: SendMessageOptions): Promise<void> {
  await fetchEventSource("/api/chat", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ message }),
    signal,
    openWhenHidden: true, // kein Auto-Pause im Hintergrund-Tab
    async onopen(response) {
      if (!response.ok || !response.headers.get("content-type")?.includes("text/event-stream")) {
        throw new Error(`SSE-Open fehlgeschlagen: HTTP ${response.status}`);
      }
    },
    onmessage(msg) {
      if (!isKnownEvent(msg.event)) {
        return; // unbekannte Events schlucken statt zu crashen
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(msg.data);
      } catch {
        return;
      }
      // Type-narrowing pro Event-Name
      onEvent({ event: msg.event, data: parsed } as ChatStreamEvent);
    },
    onerror(err) {
      // Werfen → fetch-event-source beendet den Stream und propagiert nach außen.
      throw err;
    },
  });
}
```

> **Hinweis zur Retry-Disziplin:** `fetch-event-source` retried per Default bei Verbindungsabbrüchen. Indem wir `onerror` werfen, wird der Stream sofort beendet — das ist gewollt: bei Stream-Abbruch wollen wir keinen Doppel-Send.

- [ ] **Step 3: Build prüfen**

```bash
cd frontend && npm run typecheck && npm run build
```

Grün.

- [ ] **Step 4: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/api/chatStream.ts
git commit -m "Plan E Task 6: SSE-Stream-Client mit @microsoft/fetch-event-source"
```

---

## Task 7: `useChat`-Hook (State + History + Streaming)

**Files:**
- Create: `frontend/src/hooks/useChat.ts`

- [ ] **Step 1: Hook anlegen**

```ts
import { useCallback, useEffect, useRef, useState } from "react";
import { getHistory } from "@/api/client";
import { sendMessage } from "@/api/chatStream";
import type { MessageDto, SlotInfo } from "@/api/types";

export interface ChatBubble {
  /**
   * Server-ID, sobald bekannt (nach `done`). Bis dahin temporäre Negativ-IDs.
   */
  id: number;
  role: "user" | "assistant";
  content: string;
  proposals: SlotInfo[] | null;
  incomplete: boolean;
  /** true, solange die Antwort live reinkommt. */
  streaming: boolean;
}

export interface ToolStatus {
  name: string;
  state: "started" | "finished";
  ok?: boolean;
}

export interface ChatState {
  bubbles: ChatBubble[];
  toolStatus: ToolStatus | null;
  error: string | null;
  sending: boolean;
}

const TEMP_ID_OFFSET = -1; // temporäre IDs sind negativ, dann gibt es keine Kollision mit DB-IDs
let nextTempId = TEMP_ID_OFFSET;
const freshTempId = () => nextTempId--;

function parseProposals(json: string | null): SlotInfo[] | null {
  if (!json) return null;
  try {
    return JSON.parse(json) as SlotInfo[];
  } catch {
    return null;
  }
}

function mapHistory(msgs: MessageDto[]): ChatBubble[] {
  return msgs.map((m) => ({
    id: m.id,
    role: m.role,
    content: m.content,
    proposals: parseProposals(m.proposalsJson),
    incomplete: m.incomplete,
    streaming: false,
  }));
}

export function useChat(): ChatState & {
  send: (text: string) => void;
} {
  const [bubbles, setBubbles] = useState<ChatBubble[]>([]);
  const [toolStatus, setToolStatus] = useState<ToolStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  const abortRef = useRef<AbortController | null>(null);

  // Initial-History laden
  useEffect(() => {
    let cancelled = false;
    getHistory()
      .then((dto) => {
        if (!cancelled) setBubbles(mapHistory(dto.messages));
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "History-Load fehlgeschlagen");
      });
    return () => {
      cancelled = true;
      abortRef.current?.abort();
    };
  }, []);

  const send = useCallback((text: string) => {
    if (sending || !text.trim()) return;

    const userBubble: ChatBubble = {
      id: freshTempId(),
      role: "user",
      content: text,
      proposals: null,
      incomplete: false,
      streaming: false,
    };
    const agentBubble: ChatBubble = {
      id: freshTempId(),
      role: "assistant",
      content: "",
      proposals: null,
      incomplete: false,
      streaming: true,
    };
    const agentTempId = agentBubble.id;

    setBubbles((prev) => [...prev, userBubble, agentBubble]);
    setError(null);
    setToolStatus(null);
    setSending(true);

    const ctrl = new AbortController();
    abortRef.current = ctrl;

    sendMessage({
      message: text,
      signal: ctrl.signal,
      onEvent: (ev) => {
        switch (ev.event) {
          case "token":
            setBubbles((prev) =>
              prev.map((b) =>
                b.id === agentTempId ? { ...b, content: b.content + ev.data.text } : b
              )
            );
            break;
          case "tool_started":
            setToolStatus({ name: ev.data.name, state: "started" });
            break;
          case "tool_finished":
            setToolStatus({ name: ev.data.name, state: "finished", ok: ev.data.ok });
            break;
          case "proposals":
            setBubbles((prev) =>
              prev.map((b) => (b.id === agentTempId ? { ...b, proposals: ev.data } : b))
            );
            break;
          case "done":
            setBubbles((prev) =>
              prev.map((b) =>
                b.id === agentTempId ? { ...b, id: ev.data.messageId, streaming: false } : b
              )
            );
            setToolStatus(null);
            break;
          case "error":
            setError(ev.data.message);
            setBubbles((prev) =>
              prev.map((b) =>
                b.id === agentTempId ? { ...b, streaming: false, incomplete: true } : b
              )
            );
            break;
        }
      },
    })
      .catch((e: unknown) => {
        setError(e instanceof Error ? e.message : "Stream-Fehler");
        setBubbles((prev) =>
          prev.map((b) => (b.id === agentTempId ? { ...b, streaming: false, incomplete: true } : b))
        );
      })
      .finally(() => {
        setSending(false);
        abortRef.current = null;
      });
  }, [sending]);

  return { bubbles, toolStatus, error, sending, send };
}
```

- [ ] **Step 2: Build prüfen**

```bash
cd frontend && npm run typecheck && npm run build
```

Grün.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/hooks/
git commit -m "Plan E Task 7: useChat-Hook (State + History + SSE-Stream-Faltung)"
```

---

## Task 8: SlotCard + ChatBubble

**Files:**
- Create: `frontend/src/components/SlotCard.tsx`
- Create: `frontend/src/components/ChatBubble.tsx`

- [ ] **Step 1: `SlotCard.tsx`**

```tsx
import { Card } from "@/components/ui/card";
import type { SlotInfo } from "@/api/types";

const FORMATTER = new Intl.DateTimeFormat("de-DE", {
  weekday: "short",
  day: "2-digit",
  month: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

const TIME_ONLY = new Intl.DateTimeFormat("de-DE", {
  hour: "2-digit",
  minute: "2-digit",
});

function formatSlot(slot: SlotInfo): string {
  const startDate = new Date(slot.start);
  const endDate = new Date(slot.end);
  return `${FORMATTER.format(startDate)}–${TIME_ONLY.format(endDate)}`;
}

interface SlotCardProps {
  slot: SlotInfo;
  onPick: (slot: SlotInfo) => void;
  disabled?: boolean;
}

export function SlotCard({ slot, onPick, disabled }: SlotCardProps) {
  return (
    <Card
      role="button"
      tabIndex={0}
      aria-disabled={disabled}
      className={
        "p-3 cursor-pointer hover:bg-slate-100 transition-colors " +
        (disabled ? "opacity-50 cursor-not-allowed" : "")
      }
      onClick={() => !disabled && onPick(slot)}
      onKeyDown={(e) => {
        if (!disabled && (e.key === "Enter" || e.key === " ")) {
          e.preventDefault();
          onPick(slot);
        }
      }}
    >
      <div className="font-medium">{formatSlot(slot)}</div>
      {slot.note && <div className="text-sm text-slate-500 mt-1">{slot.note}</div>}
    </Card>
  );
}

export { formatSlot };
```

- [ ] **Step 2: `ChatBubble.tsx`**

```tsx
import type { SlotInfo } from "@/api/types";
import type { ChatBubble as ChatBubbleData } from "@/hooks/useChat";
import { SlotCard, formatSlot } from "./SlotCard";

interface ChatBubbleProps {
  bubble: ChatBubbleData;
  onPickSlot: (slot: SlotInfo) => void;
  pickDisabled: boolean;
}

export function ChatBubble({ bubble, onPickSlot, pickDisabled }: ChatBubbleProps) {
  const isUser = bubble.role === "user";
  const align = isUser ? "items-end" : "items-start";
  const bg = isUser ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-900";

  return (
    <div className={`flex flex-col gap-2 ${align}`}>
      <div className={`max-w-[80%] rounded-2xl px-4 py-2 ${bg}`}>
        {bubble.content || (bubble.streaming ? <span className="opacity-50">…</span> : null)}
        {bubble.incomplete && (
          <div className="text-xs italic opacity-70 mt-1">
            (Antwort unvollständig)
          </div>
        )}
      </div>
      {bubble.proposals && bubble.proposals.length > 0 && (
        <div className="flex flex-col gap-2 max-w-[80%]">
          {bubble.proposals.map((slot, idx) => (
            <SlotCard
              key={`${bubble.id}-${idx}-${slot.start}`}
              slot={slot}
              onPick={onPickSlot}
              disabled={pickDisabled}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export { formatSlot };
```

- [ ] **Step 3: Build prüfen**

```bash
cd frontend && npm run typecheck && npm run build
```

Grün.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/SlotCard.tsx frontend/src/components/ChatBubble.tsx
git commit -m "Plan E Task 8: SlotCard + ChatBubble Komponenten"
```

---

## Task 9: MessageInput + ChatView

**Files:**
- Create: `frontend/src/components/MessageInput.tsx`
- Create: `frontend/src/components/ChatView.tsx`

- [ ] **Step 1: `MessageInput.tsx`**

```tsx
import { useState, type KeyboardEvent } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";

interface MessageInputProps {
  onSend: (text: string) => void;
  disabled: boolean;
}

export function MessageInput({ onSend, disabled }: MessageInputProps) {
  const [value, setValue] = useState("");

  const submit = () => {
    const trimmed = value.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    setValue("");
  };

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  return (
    <div className="flex gap-2 items-end p-4 border-t bg-white">
      <Textarea
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={onKeyDown}
        placeholder="Termin-Anfrage hier paste-n oder Nachricht eingeben… (Enter zum Senden, Shift+Enter für Zeilenumbruch)"
        rows={2}
        disabled={disabled}
        className="resize-none"
      />
      <Button onClick={submit} disabled={disabled || !value.trim()}>
        Senden
      </Button>
    </div>
  );
}
```

- [ ] **Step 2: `ChatView.tsx`**

```tsx
import { useEffect, useRef } from "react";
import { ScrollArea } from "@/components/ui/scroll-area";
import type { SlotInfo } from "@/api/types";
import { useChat } from "@/hooks/useChat";
import { ChatBubble } from "./ChatBubble";
import { MessageInput } from "./MessageInput";
import { formatSlot } from "./SlotCard";

const TOOL_STATUS_LABEL: Record<string, string> = {
  lookup_free_slots: "Kalender wird durchsucht…",
  get_calendar_range: "Kalender wird gelesen…",
  create_event: "Termin wird angelegt…",
  add_rule: "Regel wird gespeichert…",
  delete_rule: "Regel wird gelöscht…",
  list_rules: "Regeln werden geladen…",
  present_proposals: "Vorschläge werden vorbereitet…",
};

export function ChatView() {
  const { bubbles, toolStatus, error, sending, send } = useChat();
  const bottomRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [bubbles, toolStatus]);

  const onPickSlot = (slot: SlotInfo) => {
    send(`Ich nehme den Slot ${formatSlot(slot)}.`);
  };

  const liveStatus =
    toolStatus && toolStatus.state === "started"
      ? TOOL_STATUS_LABEL[toolStatus.name] ?? `Werkzeug läuft: ${toolStatus.name}`
      : null;

  return (
    <div className="flex flex-col h-screen max-w-3xl mx-auto bg-white">
      <header className="p-4 border-b">
        <h1 className="text-xl font-semibold">NauAssist</h1>
      </header>

      <ScrollArea className="flex-1 px-4 py-4">
        <div className="flex flex-col gap-4">
          {bubbles.map((b) => (
            <ChatBubble
              key={b.id}
              bubble={b}
              onPickSlot={onPickSlot}
              pickDisabled={sending}
            />
          ))}
          {liveStatus && (
            <div className="text-sm text-slate-500 italic">{liveStatus}</div>
          )}
          {error && (
            <div className="text-sm text-red-600 border border-red-200 rounded p-2 bg-red-50">
              {error}
            </div>
          )}
          <div ref={bottomRef} />
        </div>
      </ScrollArea>

      <MessageInput onSend={send} disabled={sending} />
    </div>
  );
}
```

- [ ] **Step 3: Build prüfen**

```bash
cd frontend && npm run typecheck && npm run build
```

Grün.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/MessageInput.tsx frontend/src/components/ChatView.tsx
git commit -m "Plan E Task 9: MessageInput + ChatView Layout"
```

---

## Task 10: App-Integration + Smoke-Test

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/index.css` (Body-Fallback-Hintergrund, falls von shadcn nicht gesetzt)

- [ ] **Step 1: `App.tsx` auf `ChatView` umstellen**

```tsx
import { ChatView } from "@/components/ChatView";

export default function App() {
  return <ChatView />;
}
```

- [ ] **Step 2: Body-Hintergrund (optional)**

In `frontend/src/index.css` nach den Tailwind-Direktiven sicherstellen:

```css
html, body, #root {
  height: 100%;
  background-color: rgb(248 250 252); /* slate-50 */
}
```

Falls shadcn das bereits über CSS-Variablen abdeckt, kann der Block weggelassen werden.

- [ ] **Step 3: Build + Lint prüfen**

```bash
cd frontend && npm run typecheck && npm run build && npm run lint
```

Alles grün. Falls `lint` Findings hat: beheben oder per Inline-Disable rechtfertigen.

- [ ] **Step 4: Manueller Smoke-Test gegen echtes Backend**

Voraussetzungen:
- Ollama läuft lokal mit dem in `appsettings.json` konfigurierten Modell.
- Google-OAuth wurde mindestens einmal durchlaufen (`dotnet run --project src/Backend -- auth`).

Backend starten:

```bash
dotnet run --project src/Backend
```

Frontend starten:

```bash
cd frontend && npm run dev
```

Im Browser auf `http://localhost:5173`.

**Test-Drehbuch:**

1. Seite lädt — leerer Chat (oder bisherige History, falls vorhanden).
2. „Hallo, kannst du morgen 14 Uhr einen Termin mit Anna für 30 Minuten vorschlagen?" senden.
3. Erwartet: Status „Kalender wird durchsucht…" erscheint, dann „Vorschläge werden vorbereitet…", dann werden Slot-Cards gerendert, dann Tokens trudeln in die Agent-Bubble.
4. Auf eine Slot-Card klicken.
5. Erwartet: Eine neue User-Bubble „Ich nehme den Slot …" wird gesendet, der Agent ruft `create_event`, bestätigt mit Text.
6. Im Google Kalender prüfen: Termin ist tatsächlich angelegt.
7. Seite refreshen (F5) — die komplette Konversation muss aus der History wiederkommen, inkl. der Slot-Cards der älteren Vorschlags-Antwort.

Bei Problemen Backend-Logs (stdout) konsultieren.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/App.tsx frontend/src/index.css
git commit -m "Plan E Task 10: App-Integration, ChatView als Root, Smoke-Test"
```

---

## Was nach Plan E steht

**MVP komplett** nach Spec §2: Der User pastet eine Anfrage ins React-UI, der Agent schlägt Slots vor, der User bestätigt, der Termin landet im Google Kalender — alles end-to-end durchspielbar lokal.

Mögliche Folge-Pläne (jeweils Out-of-Scope für MVP, siehe Spec §11):

1. **Frontend-Polish / a11y / Mobile-Layout** — Plan E ist bewusst nüchtern und Desktop-zentriert.
2. **Rules-Manager-Panel** — separate Ansicht zum Verwalten von Regeln neben dem Chat (statt nur via Chat-Tool-Calls).
3. **Implizites Lernen** — Vorschlag „du lehnst Slots immer zwischen 17–18 ab — Regel daraus machen?"
4. **Inbound-Provider** (E-Mail/IMAP, später WhatsApp via Matrix-Bridge).
5. **Container-Setup** — Dockerfile + Compose, damit Backend+Frontend als Daemon laufen können.
