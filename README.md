# NauAssist

Autonomer persönlicher KI-Agent — ein „digitaler Mitbewohner" auf einer
dedizierten Linux-Box. Liest aus konfigurierten Quellen (Mail, Kalender,
Messenger), reflektiert periodisch und meldet sich proaktiv. Schickt aber
nichts nach draußen ohne ausdrückliche Zustimmung.

Arbeitssprache: Deutsch (Oberfläche, Prompts, Doku, Commits).

## Konzept

Das vollständige Designdokument liegt in
[`agent-konzept-handout.html`](./agent-konzept-handout.html) — vor allem
nicht-trivialen Änderungen erst dort lesen. Architekturentscheidungen
darin sind Vorgaben, keine Vorschläge.

Operative Regeln für die Codebase stehen in [`CLAUDE.md`](./CLAUDE.md).

## Tech-Stack

- **.NET 10** mit `.slnx`-Solutionformat
- **Ollama** auf dem Host (Modell `gemma4:e4b`)
- **Semantic Kernel** (geplant)
- **SQLite** für Gedächtnis-Layer 1–3, **Quartz.NET** für Scheduling
- **React** für Oberfläche (Etappe 6)

## Projektstruktur

```
src/                Kernwelt — Code von Menschen, der Agent darf hier nicht schreiben
  Common/             Querschnitts-Interfaces, keine Abhängigkeiten
  AICore/             LLM-Client, Reflexions-Loop, Gedanken-Log
  Memory/             4-Schicht-Gedächtnis
  Tools/              Werkzeuge des Agenten
  Voice/              STT, TTS, Konversations-Pipeline
  Extensions/         Self-Extension: Tools, die der Agent selbst baut
  Api/                Host (REST, WebSocket, /health, /metrics)
  Tests/              Tests für alles oben
extensions/          Erweiterungs-Welt — Territorium des Agenten,
                     Schreibzugriff nur über IExtensionWorkspace
```

## Build, Test, Run

```bash
cd src
dotnet build NauAssist.slnx
dotnet test  NauAssist.slnx
dotnet test  NauAssist.slnx --filter "FullyQualifiedName~MyTestClass"
dotnet run   --project Api
```

Container-basierte Inbetriebnahme folgt mit Etappe 0 / Issue #3.

## Roadmap

Vertikale Etappen — jede Etappe liefert ein lauffähiges System mit einer
neuen Eigenschaft, keine vollständigen horizontalen Schichten.

| Etappe | Thema                              | Issues  |
|--------|------------------------------------|---------|
| 0      | Foundation                         | #1–6    |
| 1      | Stimme & Ohren                     | #7–12   |
| 2      | Gehirn (Gedächtnis)                | #13–20  |
| 3      | Zeitgefühl (Scheduler, Tool-Calls) | #21–24  |
| 4      | Fähigkeiten (Tools, Vorlagen)      | #25–31  |
| 5      | Eigenleben (Reflexion, Pipeline)   | #32–37  |
| 6      | Sichtbarkeit (React-UI)            | #38–44  |
