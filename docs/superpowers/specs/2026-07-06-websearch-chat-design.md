# WebSearch im Chat — `web_search` & `fetch_webpage` als Agent-Tools

> Status: **Design validiert** (autonom erarbeitet, Feature aus der Planning-Liste in Benedikts Mind).
> Branch: `feat/websearch-chat-tools`.

## Ziel

Der Chat-Agent soll aktuelle Informationen aus dem Web heranziehen können —
„Was sind die Öffnungszeiten von …?", „Wie ist das Wetter-/News-/Preis-Update zu …?" —
statt auf sein Trainingswissen beschränkt zu sein.

Watch-Jobs Phase 1 (PR #87) hat dafür bereits die komplette Infrastruktur geliefert:

- `IWebSearch` / `SearxngWebSearch` — Suche über eine self-hosted SearXNG-Instanz,
- `IWebFetch` / `HttpWebFetch` — SSRF-gehärteter Fetch (privater/interner IP-Block
  inkl. Redirect-Hops via `SsrfGuard`), Größen-/Timeout-Cap, HTML→Text-Reduktion.

Dieses Feature macht die Bausteine **dem Chat-Agenten als Tools zugänglich**. Es
entsteht kein neuer Web-Zugriffs-Code.

## Betrachtete Ansätze

- **(A) Zwei Tools auf vorhandenen Bausteinen + Modul-Move — gewählt.**
  `web_search` (SearXNG) und `fetch_webpage` (gehärteter Fetch) als `ITool`s; der
  geteilte Web-Code zieht von `Features/WatchJobs/Web` nach `Features/Web` um,
  Config-Bindung von `AutonomousAgent:WatchJobs:Web` nach top-level `Web`.
- **(B) Nur `web_search`, kein Fetch.** Schlanker, aber Such-Snippets sind für
  Folgefragen oft zu dünn — und der Fetch-Baustein liegt fertig da. Verworfen.
- **(C) Großer Umbau: Web als eigenständiges Feature mit per-Consumer-Optionen.**
  Überdimensioniert für den Single-User-MVP. Verworfen.

## Architektur

### 1. Modul-Move: `Features/WatchJobs/Web` → `Features/Web`

Web-Zugriff ist ab jetzt **geteilte Infrastruktur** (Chat-Tools *und* Watch-Jobs).
Der Config-Pfad `AutonomousAgent:WatchJobs:Web:SearxngBaseUrl` wäre irreführend,
wenn die Chat-Suche unabhängig von `WatchJobs:Enabled` aktivierbar sein soll.

- Dateien `IWebSearch`, `IWebFetch`, `WebOptions`, `SearxngWebSearch`,
  `HttpWebFetch`, `SsrfGuard` → Namespace `NauAssist.Backend.Features.Web`.
- Bindung: `builder.Configuration.GetSection("Web")`; appsettings-Sektion `Web`
  wird top-level (Werte unverändert).
- Default-`UserAgent` wird generisch: `NauAssist/1.0 (+https://github.com/BenediktNau/NauAssist)`.
- **Kein Deployment-Bruch:** In Prod ist noch keine SearXNG-URL gesetzt (Watch-Jobs-E2E
  steht noch aus); der alte Pfad ist nur in einem historischen Plan-Dokument erwähnt.

### 2. Neue Tools in `Features/Web/Tools/`

**`WebSearchTool`** (`web_search`)
- Args: `query` (string, Pflicht), `max_results` (int, optional, Default 5, geklemmt auf 1–8).
- Ruft `IWebSearch.SearchAsync`; Ergebnis `{ results: [{ title, url, snippet }] }`.
- Leere Treffer ⇒ `{ results: [], hint: "…" }` — das LLM soll ehrlich sagen können,
  dass es nichts gefunden hat (Baustein wirft designbedingt nicht).

**`FetchWebpageTool`** (`fetch_webpage`)
- Args: `url` (string, Pflicht, absolute http(s)-URL).
- Ruft `IWebFetch.FetchAsync(url, etag: null)`; kappt den Text auf **6 000 Zeichen**
  (Konstante im Tool — Schutz des LLM-Kontextfensters; `MaxFetchBytes` schützt nur
  den Download). Ergebnis `{ url, status, text, truncated }`.
- SSRF-Schutz greift unverändert über den gehärteten HttpClient — genau der Fall
  (LLM-gelieferte URL), für den `SsrfGuard` gebaut wurde.

Beide Tools sind dünne Adapter im Stil von `ListWatchJobsTool`: Args parsen,
Baustein rufen, kompaktes JSON zurück.

### 3. Registrierung & Gating (`Program.cs`)

```csharp
if (!string.IsNullOrEmpty(webOptions.SearxngBaseUrl))
{
    builder.Services.AddScoped<ITool, WebSearchTool>();
    builder.Services.AddScoped<ITool, FetchWebpageTool>();
}
```

- Aktivierung ⇔ SearXNG konfiguriert. Kein zusätzliches Feature-Flag (YAGNI);
  ohne Such-Backend ist auch `fetch_webpage` kaum sinnvoll nutzbar.
- Unabhängig von `WatchJobs:Enabled`.

### 4. Operating-Rules werden komponierbar

Ist-Zustand: `AgentOperatingRules.Text` ist eine Konstante und beschreibt auch
Watch-Job-Tools, wenn diese gar nicht registriert sind (bestehende Schwäche —
das LLM kann Tool-Calls auf nicht existente Tools halluzinieren).

Neu: `AgentOperatingRules.Compose(IReadOnlyCollection<string> toolNames)` —
Absätze erscheinen nur, wenn die zugehörigen Tools verfügbar sind:

- Watch-Job-Absatz nur bei `create_watch_job` im Toolset (fixt die Schwäche),
- neuer Web-Absatz nur bei `web_search` im Toolset, sinngemäß:
  *Für Fragen nach aktuellen Informationen (News, Preise, Öffnungszeiten,
  Verfügbarkeiten) `web_search` rufen; reichen die Snippets nicht, die
  vielversprechendste URL per `fetch_webpage` lesen. Antworten mit Quelle (URL)
  belegen. Keine Web-Suche für Kalender-/Regel-Aktionen und Smalltalk.*

`AgentRunner` hält die Tools bereits als Dictionary und ruft künftig
`AgentOperatingRules.Compose(_tools.Keys)` statt `.Text`.

### 5. Frontend

Zwei Einträge in `TOOL_STATUS_LABEL` (`ChatView.tsx`):
`web_search: "SUCHE IM WEB"`, `fetch_webpage: "LESE WEBSEITE"`.
Mehr nicht — Tool-Status-Anzeige und Fallback-Label existieren generisch.

## Fehlerbehandlung

Die Web-Bausteine werfen designbedingt nicht (leere Liste / leeres Dokument + Log).
Die Tools übersetzen das in ehrliche, LLM-lesbare Resultate (`hint`, `status: 0`),
statt Exceptions durch den Agent-Loop zu reichen. Ungültige Args (leere Query,
relative URL) ⇒ beschreibendes Fehler-JSON, kein Throw.

Gefetchter Webtext ist **untrusted Input** im LLM-Kontext und damit eine
Prompt-Injection-Oberfläche: eine Seite kann versuchen, dem Agenten Anweisungen
unterzuschieben. Für den Single-User-MVP wird das bewusst akzeptiert — destruktive
Tools sind laut Operating-Rules bestätigungspflichtig (kein Löschen/Verschieben ohne
Rückfrage), und die Web-Tools selbst sind read-only mit SSRF-Block. Sobald
unbestätigte Auto-Aktionen dazukommen, ist dieses Risiko neu zu bewerten.

## Tests (TDD)

- `WebSearchToolTests`: Treffer-Mapping, `max_results`-Klemmung/Default, leere
  Query ⇒ Fehler-JSON, leere Trefferliste ⇒ `hint`.
- `FetchWebpageToolTests`: Text-Kappung + `truncated`-Flag, ungültige URL ⇒
  Fehler-JSON, Status/URL-Durchreichung (Fake-`IWebFetch`).
- `AgentOperatingRulesTests`: Web-/Watch-Absätze erscheinen genau bei passendem
  Toolset; Basis-Regeln immer.
- Bestehende Watch-Job-/Web-Tests laufen nach dem Modul-Move unverändert grün
  (nur Namespace-/Bindungs-Anpassungen).

## Aus dem Scope

- Kein eigenes Ranking/Dedup über Suchtreffer (SearXNG aggregiert bereits).
- Kein Caching von Suchergebnissen.
- Keine UI für Quellen-Anzeige über den Chat-Text hinaus.
- Kein separates Frontend-Setting zum An-/Abschalten (Konfig = `Web:SearxngBaseUrl`).
