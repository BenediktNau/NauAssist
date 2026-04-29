# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

NauAssist is an autonomous personal AI agent ("digitaler Mitbewohner") that lives on a dedicated Linux box, perceives explicitly configured sources (mail, calendar, messengers), reflects periodically, and proactively contacts the user — but never sends anything outward without approval. Working language is German (UI, prompts, design docs, commit messages).

The full design is in `agent-konzept-handout.html` — open it before doing anything non-trivial. Architectural decisions there are constraints, not suggestions.

## Tech stack

- **.NET 10** (`net10.0`) using the new `.slnx` (XML) solution format. The handout still says ".NET 9" — that's pre-implementation; we're on 10.
- **Ollama** with model `gemma4:e4b`, runs on the **host**, not in the agent container.
- **Semantic Kernel** as the agent framework (planned, not yet wired in).
- **SQLite** for memory layers 1–3, **Quartz.NET** for scheduling, **Whisper.NET / Piper** for voice (planned).
- **React** frontend, deferred to Etappe 6.

## Development workflow: test-driven

This project is developed **test-first**. For every Akzeptanzkriterium in an issue:

1. Write the failing test in `NauAssist.Tests` first.
2. Implement the minimum that turns it green.
3. Refactor with the test as a safety net.

A new public API or behavior without a test that pins it is incomplete — even if it compiles and runs. Test names should reference the issue's acceptance criterion (e.g., `Rule_Disable_KeepsRecordButMarksInactive_E2_2`). Integration tests that need external services (Ollama, IMAP, CalDAV) skip cleanly when those services are unavailable; they don't fail CI.

## Build, test, run

The solution lives in `src/`. Always use the `.slnx` filename — not `.sln`.

```bash
cd src
dotnet build NauAssist.slnx
dotnet test  NauAssist.slnx
dotnet test  NauAssist.slnx --filter "FullyQualifiedName~MyTestClass"   # single test/class
dotnet run   --project Api                                              # start the host
```

## Architecture

Eight projects, strict dependency direction. Folder/csproj names drop the
`NauAssist.` prefix (the solution folder already supplies it); each csproj
keeps `RootNamespace` + `AssemblyName` set to `NauAssist.<Name>` so namespaces
and DLL names stay `NauAssist.Common`, `NauAssist.AICore`, etc.

```
Common      ← root, no deps                                 (NauAssist.Common)
Memory      → Common                                        (NauAssist.Memory)
Tools       → Common                                        (NauAssist.Tools)
AICore      → Common, Memory, Tools                         (LLM client + Reflection-Loop + Pipeline + Thought-Log)
Voice       → Common, AICore                                (STT, TTS, capture, conversation wiring)
Extensions  → Common, Tools                                 (Self-Extension: tools the agent builds itself)
Api         → Common, AICore, Memory, Tools, Voice          (Program.cs host, REST, WebSocket)
Tests       → all
```

**Never introduce a back-edge** (e.g., Memory referencing AICore). Cross-feature coordination lives in `Common` interfaces or in `Api` orchestration — not in lateral references.

### The Kern/Erweiterung boundary (most important rule)

Two physically separated worlds, with a runtime-enforced boundary (see Konzept §6 and issue #6):

- **Kernwelt** (`/src` + everything in this repo's tracked code) — written by humans, stable, the agent must **not** write here.
- **Erweiterungs-Welt** (`/extensions/`) — agent's territory. Self-built tools, specs, audit logs, weakness lists, draft files.

Anything that writes paths at runtime (tool generation, draft files, usage logs) **must** go through `IExtensionWorkspace` (issue #6 — not yet implemented). Hand-rolling `File.WriteAllText` outside the Kern is a bug.

`/extensions/` is treated as a fifth memory layer. When backing up the agent's "personality", that folder ships with the SQLite databases.

### Memory architecture (4 layers, not one vector DB for everything)

- **Layer 1 — Rules** (`rules` table): hard/soft constraints, loaded into every evaluation, never searched.
- **Layer 2 — Entities** (`entities`, `entity_facts`): typed cards (Person/Place/Project/Device), looked up by identifier (email/alias/phone), not by similarity.
- **Layer 3 — Active topics** (`topics`): small, current, rotating working memory.
- **Layer 4 — Episodic** (`conversation_log` + FTS5 for now, vector DB later): semantic recall when needed.

Incoming information is **routed** to layers via `IMemoryRouter` (issue #18). One event can land in multiple layers.

### Reflection-Loop (Etappe 5, not yet built)

Quartz job every 5–10 min runs a four-stage pipeline (issues #32–37):

1. Hard filters (deterministic — quiet hours, spam, whitelist) — 0 tokens
2. Factor extraction (deterministic scores) — 0 tokens
3. Threshold check (time-of-day-dependent)
4. LLM judgment with full context (only this stage costs tokens)

Goal: 80–90% of events are decided in stages 1–3.

## Roadmap

Development is **vertical** — each Etappe ships a runnable system with a new quality, not a complete horizontal layer:

| Etappe | Theme | Issues |
|--------|-------|--------|
| 0 | Foundation | #1–6 |
| 1 | Stimme & Ohren (voice) | #7–12 |
| 2 | Gehirn (memory) | #13–20 |
| 3 | Zeitgefühl (scheduler + tool-calling) | #21–24 |
| 4 | Fähigkeiten (tools, Vorlage-Mechanik) | #25–31 |
| 5 | Eigenleben (Reflection-Loop, Pipeline) | #32–37 |
| 6 | Sichtbarkeit (React UI) | #38–44 |

Issues are labeled `etappe-N` and `type:foundation|voice|memory|scheduler|tool|reflection|ui` and tied to milestones with the same names. Each issue has Outcome / Akzeptanzkriterien / Abhängigkeiten / Konzept-Referenz — read the dependency block before starting work to avoid out-of-order implementation.

## Hard rules from the Konzept that touch code

- **Prinzip ii — Keine ungenehmigten Außenwirkungen:** the agent reads freely but never sends. Outbound actions (mail send, calendar invite, message) go through the Vorlage-Mechanismus (issue #29) and require explicit approval. There is no "send anyway" path.
- **Prinzip iv — Stille als Standardzustand:** the Reflection-Loop's default decision is "don't notify". Lowering thresholds is a tuning task, not a fix.
- **Prinzip v — Selektive Wahrnehmung:** sources (mailbox, chat, calendar) are explicitly whitelisted in config. Auto-discovery of sources is wrong.
- **Prinzip vi — Lernen durch Gedächtnis:** adapt by enriching memory layers, never by fine-tuning the model. If a behavior needs to change, the answer is a Rule (Layer 1) or Entity-Fact (Layer 2), not a prompt rewrite.
- **Prinzip vii — Portabilität:** no hard-coded paths, no machine-specific config. All paths flow through `IPathResolver` (issue #2 — not yet implemented).

## Development environment

Ollama runs on the host (already installed); the agent is meant to run **containerized** so it can't damage the host. Container setup (issue #3) wires Ollama via `host.docker.internal:host-gateway`. Local `dotnet` is also available on the host for fast inner-loop work.
