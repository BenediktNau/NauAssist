# WebSearch-Chat-Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Der Chat-Agent bekommt zwei Tools — `web_search` (SearXNG) und `fetch_webpage` (SSRF-gehärteter Fetch) — auf Basis der in Watch-Jobs Phase 1 gebauten Web-Bausteine.

**Architecture:** Der geteilte Web-Code zieht von `Features/WatchJobs/Web` nach `Features/Web` um (Config-Bindung `Web` statt `AutonomousAgent:WatchJobs:Web`). Zwei dünne `ITool`-Adapter in `Features/Web/Tools/`, registriert nur wenn `Web:SearxngBaseUrl` konfiguriert ist. `AgentOperatingRules` wird komponierbar: Absätze erscheinen nur für tatsächlich registrierte Tools.

**Tech Stack:** .NET 10 Minimal API, xUnit + AwesomeAssertions, React/TypeScript (nur zwei Label-Einträge).

**Spec:** `docs/superpowers/specs/2026-07-06-websearch-chat-design.md`

## Global Constraints

- Sprache in Code-Kommentaren, Commit-Messages und Doku: Deutsch; Commits im Stil `feat(backend): …` als reine Subject-Line (keine Trailer).
- Tests mit xUnit + **AwesomeAssertions** (`using AwesomeAssertions;`), Muster siehe `src/Backend.Tests/Features/WatchJobs/WatchJobToolsTests.cs`.
- Tool-Resultate sind kompakte JSON-Objekte mit `ok`-Feld (Konvention der Watch-Job-Tools); Tools werfen keine Exceptions Richtung Agent-Loop.
- Testlauf immer: `dotnet test src/NauAssist.slnx` (erwartet: alle Tests grün, Stand vor diesem Plan: 299).
- Branch: `feat/websearch-chat-tools`.

---

### Task 1: Modul-Move `Features/WatchJobs/Web` → `Features/Web`

Mechanischer Umzug, kein Verhaltenswechsel — die bestehenden Tests sichern ab. Kein TDD nötig.

**Files:**
- Move (git mv): `src/Backend/Features/WatchJobs/Web/{IWebSearch,IWebFetch,WebOptions,SearxngWebSearch,HttpWebFetch,SsrfGuard}.cs` → `src/Backend/Features/Web/`
- Move (git mv): `src/Backend.Tests/Features/WatchJobs/SsrfGuardTests.cs` → `src/Backend.Tests/Features/Web/SsrfGuardTests.cs`
- Modify: `src/Backend/Program.cs`, `src/Backend/Features/WatchJobs/WatchJobExecutor.cs`, `src/Backend/Features/WatchJobs/Tools/CreateWatchJobTool.cs`, `src/Backend/appsettings.json`
- Modify (usings): `src/Backend.Tests/Features/WatchJobs/{WatchJobExecutorTests,WatchJobSchedulerTests}.cs`

**Interfaces:**
- Produces: Namespace `NauAssist.Backend.Features.Web` mit unveränderten Typen `IWebSearch`, `WebSearchHit(Title, Url, Snippet)`, `IWebFetch`, `WebDocument(Url, StatusCode, Etag, TextContent, NotModified)`, `WebOptions`, `SsrfGuard`; Config-Bindung `Web` (top-level).

- [ ] **Step 1: Dateien verschieben**

```bash
mkdir -p src/Backend/Features/Web src/Backend.Tests/Features/Web
git mv src/Backend/Features/WatchJobs/Web/IWebSearch.cs src/Backend/Features/Web/
git mv src/Backend/Features/WatchJobs/Web/IWebFetch.cs src/Backend/Features/Web/
git mv src/Backend/Features/WatchJobs/Web/WebOptions.cs src/Backend/Features/Web/
git mv src/Backend/Features/WatchJobs/Web/SearxngWebSearch.cs src/Backend/Features/Web/
git mv src/Backend/Features/WatchJobs/Web/HttpWebFetch.cs src/Backend/Features/Web/
git mv src/Backend/Features/WatchJobs/Web/SsrfGuard.cs src/Backend/Features/Web/
git mv src/Backend.Tests/Features/WatchJobs/SsrfGuardTests.cs src/Backend.Tests/Features/Web/
```

- [ ] **Step 2: Namespaces und Usings anpassen**

In den 6 verschobenen Backend-Dateien:
`namespace NauAssist.Backend.Features.WatchJobs.Web;` → `namespace NauAssist.Backend.Features.Web;`

In `SsrfGuardTests.cs`: `using NauAssist.Backend.Features.WatchJobs.Web;` → `using NauAssist.Backend.Features.Web;` und `namespace NauAssist.Backend.Tests.Features.WatchJobs;` → `namespace NauAssist.Backend.Tests.Features.Web;`

In `WatchJobExecutor.cs`, `CreateWatchJobTool.cs`, `Program.cs`, `WatchJobExecutorTests.cs`, `WatchJobSchedulerTests.cs`:
`using NauAssist.Backend.Features.WatchJobs.Web;` → `using NauAssist.Backend.Features.Web;`

- [ ] **Step 3: Config-Bindung und appsettings umziehen**

`src/Backend/Program.cs` (bisher Zeile 167–168):

```csharp
builder.Services.Configure<WebOptions>(
    builder.Configuration.GetSection("Web"));
```

`src/Backend/appsettings.json`: die Sektion `Web` aus `AutonomousAgent:WatchJobs` heraus auf top-level ziehen (Werte unverändert):

```json
  "AutonomousAgent": {
    "WatchJobs": {
      "Enabled": false,
      "TickSeconds": 10,
      "MinIntervalSeconds": 30,
      "MaxConcurrent": 4,
      "MaxActivePerUser": 10,
      "ConfidenceThreshold": 0.6
    }
  },
  "Web": {
    "SearxngBaseUrl": "",
    "MaxFetchBytes": 2000000,
    "FetchTimeoutSeconds": 15
  }
```

- [ ] **Step 4: Formulierungen entschärfen (Web ist jetzt geteilte Infrastruktur)**

In `WebOptions.cs`: XML-Doc der Klasse auf `Bindet die top-level <c>Web</c>-Sektion. Steuert den Web-Zugriff von Chat-Tools und Watch-Jobs …` ändern; Default-UserAgent:

```csharp
public string UserAgent { get; set; } = "NauAssist/1.0 (+https://github.com/BenediktNau/NauAssist)";
```

In `SearxngWebSearch.cs`: `public const string HttpClientName = "WebSearch";` und Log-Text `"Web-Suche übersprungen: SearxngBaseUrl ist nicht konfiguriert."`
In `HttpWebFetch.cs`: `public const string HttpClientName = "WebFetch";`

- [ ] **Step 5: Build + kompletter Testlauf**

Run: `dotnet test src/NauAssist.slnx`
Expected: alle Tests grün (299), keine Compile-Fehler.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(backend): Web-Zugriff von WatchJobs nach Features/Web umgezogen (Config top-level Web)"
```

---

### Task 2: `WebSearchTool` (TDD)

**Files:**
- Create: `src/Backend/Features/Web/Tools/WebSearchTool.cs`
- Test: `src/Backend.Tests/Features/Web/WebSearchToolTests.cs`

**Interfaces:**
- Consumes: `IWebSearch.SearchAsync(string query, int maxResults, CancellationToken ct)` → `IReadOnlyList<WebSearchHit>` (Task 1).
- Produces: `ITool` mit `Name == "web_search"`; Resultat-JSON: ok `{ "ok": true, "results": [{ "title", "url", "snippet" }] }`, leere Treffer zusätzlich `"hint"`, fehlende Query `{ "ok": false, "error": "…" }`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
using System.Text.Json;
using AwesomeAssertions;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Features.Web.Tools;

namespace NauAssist.Backend.Tests.Features.Web;

public sealed class WebSearchToolTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private sealed class FakeWebSearch : IWebSearch
    {
        public IReadOnlyList<WebSearchHit> Hits { get; set; } = [];
        public int? ReceivedMaxResults;
        public string? ReceivedQuery;

        public Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            ReceivedQuery = query;
            ReceivedMaxResults = maxResults;
            return Task.FromResult(Hits);
        }
    }

    [Fact]
    public async Task Search_MapsHitsToCompactJson()
    {
        var search = new FakeWebSearch
        {
            Hits = [new WebSearchHit("Titel", "https://example.org", "Snippet-Text")],
        };
        var tool = new WebSearchTool(search);

        var result = await tool.ExecuteAsync(Args("""{ "query": "midea portasplit" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        var hits = result.GetProperty("results").EnumerateArray().ToList();
        hits.Should().HaveCount(1);
        hits[0].GetProperty("title").GetString().Should().Be("Titel");
        hits[0].GetProperty("url").GetString().Should().Be("https://example.org");
        hits[0].GetProperty("snippet").GetString().Should().Be("Snippet-Text");
        search.ReceivedQuery.Should().Be("midea portasplit");
    }

    [Fact]
    public async Task Search_DefaultsToFiveResults_AndClampsToEight()
    {
        var search = new FakeWebSearch();
        var tool = new WebSearchTool(search);

        await tool.ExecuteAsync(Args("""{ "query": "x" }"""), CancellationToken.None);
        search.ReceivedMaxResults.Should().Be(5);

        await tool.ExecuteAsync(Args("""{ "query": "x", "max_results": 50 }"""), CancellationToken.None);
        search.ReceivedMaxResults.Should().Be(8);

        await tool.ExecuteAsync(Args("""{ "query": "x", "max_results": 0 }"""), CancellationToken.None);
        search.ReceivedMaxResults.Should().Be(1);
    }

    [Fact]
    public async Task Search_WithoutQuery_ReturnsError()
    {
        var tool = new WebSearchTool(new FakeWebSearch());

        var result = await tool.ExecuteAsync(Args("""{ "max_results": 3 }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Search_EmptyHits_ReturnsHintForLlm()
    {
        var tool = new WebSearchTool(new FakeWebSearch());

        var result = await tool.ExecuteAsync(Args("""{ "query": "x" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("results").GetArrayLength().Should().Be(0);
        result.GetProperty("hint").GetString().Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Tests laufen lassen — müssen scheitern**

Run: `dotnet test src/NauAssist.slnx --filter WebSearchToolTests`
Expected: Compile-Fehler `WebSearchTool` existiert nicht.

- [ ] **Step 3: Implementierung**

```csharp
using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.Web.Tools;

/// <summary>
/// Chat-Tool „web_search": Web-Suche über die konfigurierte SearXNG-Instanz.
/// Wird nur registriert, wenn <c>Web:SearxngBaseUrl</c> gesetzt ist (Program.cs).
/// </summary>
public sealed class WebSearchTool : ITool
{
    private const int DefaultResults = 5;
    private const int MaxResults = 8;

    public string Name => "web_search";

    public string Description =>
        "Sucht im Web nach aktuellen Informationen (News, Preise, Öffnungszeiten, Verfügbarkeiten) " +
        "und liefert Treffer mit Titel, URL und Snippet.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Suchanfrage" },
            "max_results": { "type": "integer", "description": "Maximale Trefferzahl (1-8, Default 5)" }
          },
          "required": ["query"]
        }
        """).RootElement;

    private readonly IWebSearch _search;

    public WebSearchTool(IWebSearch search)
    {
        _search = search;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var query = args.TryGetProperty("query", out var q) ? q.GetString() : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.SerializeToElement(new { ok = false, error = "query fehlt oder ist leer." });
        }

        var maxResults = DefaultResults;
        if (args.TryGetProperty("max_results", out var max) && max.ValueKind == JsonValueKind.Number)
        {
            maxResults = Math.Clamp(max.GetInt32(), 1, MaxResults);
        }

        var hits = await _search.SearchAsync(query, maxResults, ct);
        var results = hits.Select(h => new { title = h.Title, url = h.Url, snippet = h.Snippet });

        // Die Suche wirft designbedingt nicht (leere Liste bei Fehlern) — dem LLM ehrlich
        // signalisieren, dass es „keine Treffer" von „Suche kaputt/unkonfiguriert" nicht
        // unterscheiden kann.
        return hits.Count == 0
            ? JsonSerializer.SerializeToElement(new
            {
                ok = true,
                results,
                hint = "Keine Treffer (oder Suche nicht erreichbar). Sag dem User ehrlich, dass du nichts gefunden hast.",
            })
            : JsonSerializer.SerializeToElement(new { ok = true, results });
    }
}
```

- [ ] **Step 4: Tests laufen lassen — müssen grün sein**

Run: `dotnet test src/NauAssist.slnx --filter WebSearchToolTests`
Expected: 4 Tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Web/Tools/WebSearchTool.cs src/Backend.Tests/Features/Web/WebSearchToolTests.cs
git commit -m "feat(backend): web_search-Chat-Tool auf SearXNG-Baustein"
```

---

### Task 3: `FetchWebpageTool` (TDD)

**Files:**
- Create: `src/Backend/Features/Web/Tools/FetchWebpageTool.cs`
- Test: `src/Backend.Tests/Features/Web/FetchWebpageToolTests.cs`

**Interfaces:**
- Consumes: `IWebFetch.FetchAsync(string url, string? etag, CancellationToken ct)` → `WebDocument(Url, StatusCode, Etag, TextContent, NotModified)`; `SsrfGuard.IsAllowedUrl(string url, out Uri? uri)` (beides Task 1).
- Produces: `ITool` mit `Name == "fetch_webpage"`; Resultat-JSON: ok `{ "ok": true, "url", "status", "text", "truncated" }`, leerer Text zusätzlich `"hint"`, ungültige URL `{ "ok": false, "error": "…" }`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
using System.Text.Json;
using AwesomeAssertions;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Features.Web.Tools;

namespace NauAssist.Backend.Tests.Features.Web;

public sealed class FetchWebpageToolTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private sealed class FakeWebFetch : IWebFetch
    {
        public WebDocument Result { get; set; } = new("https://example.org", 200, null, "Inhalt", false);

        public Task<WebDocument> FetchAsync(string url, string? etag, CancellationToken ct)
            => Task.FromResult(Result);
    }

    [Fact]
    public async Task Fetch_ReturnsTextStatusAndUrl()
    {
        var tool = new FetchWebpageTool(new FakeWebFetch());

        var result = await tool.ExecuteAsync(Args("""{ "url": "https://example.org" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("url").GetString().Should().Be("https://example.org");
        result.GetProperty("status").GetInt32().Should().Be(200);
        result.GetProperty("text").GetString().Should().Be("Inhalt");
        result.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Fetch_TruncatesLongTextAt6000Chars()
    {
        var fetch = new FakeWebFetch
        {
            Result = new WebDocument("https://example.org", 200, null, new string('a', 10_000), false),
        };
        var tool = new FetchWebpageTool(fetch);

        var result = await tool.ExecuteAsync(Args("""{ "url": "https://example.org" }"""), CancellationToken.None);

        result.GetProperty("text").GetString()!.Length.Should().Be(6000);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Fetch_InvalidUrl_ReturnsError()
    {
        var tool = new FetchWebpageTool(new FakeWebFetch());

        foreach (var badArgs in new[] { """{ }""", """{ "url": "not-a-url" }""", """{ "url": "file:///etc/passwd" }""" })
        {
            var result = await tool.ExecuteAsync(Args(badArgs), CancellationToken.None);
            result.GetProperty("ok").GetBoolean().Should().BeFalse();
            result.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Fetch_EmptyText_ReturnsHintForLlm()
    {
        var fetch = new FakeWebFetch
        {
            Result = new WebDocument("https://example.org", 0, null, "", false),
        };
        var tool = new FetchWebpageTool(fetch);

        var result = await tool.ExecuteAsync(Args("""{ "url": "https://example.org" }"""), CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("status").GetInt32().Should().Be(0);
        result.GetProperty("hint").GetString().Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Tests laufen lassen — müssen scheitern**

Run: `dotnet test src/NauAssist.slnx --filter FetchWebpageToolTests`
Expected: Compile-Fehler `FetchWebpageTool` existiert nicht.

- [ ] **Step 3: Implementierung**

```csharp
using System.Text.Json;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.Web.Tools;

/// <summary>
/// Chat-Tool „fetch_webpage": liest eine Webseite als reduzierten Text (SSRF-gehärteter
/// Fetch, siehe <see cref="SsrfGuard"/>). Der Text wird zusätzlich zu MaxFetchBytes auf
/// <see cref="MaxTextChars"/> gekappt — Schutz des LLM-Kontextfensters.
/// </summary>
public sealed class FetchWebpageTool : ITool
{
    internal const int MaxTextChars = 6_000;

    public string Name => "fetch_webpage";

    public string Description =>
        "Lädt eine Webseite und liefert ihren Inhalt als Text — z. B. um einen web_search-Treffer " +
        "im Detail zu lesen. Nur absolute http(s)-URLs.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "url": { "type": "string", "description": "Absolute http(s)-URL der Seite" }
          },
          "required": ["url"]
        }
        """).RootElement;

    private readonly IWebFetch _fetch;

    public FetchWebpageTool(IWebFetch fetch)
    {
        _fetch = fetch;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var url = args.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url) || !SsrfGuard.IsAllowedUrl(url, out _))
        {
            return JsonSerializer.SerializeToElement(new
            {
                ok = false,
                error = "url fehlt oder ist keine absolute http(s)-URL.",
            });
        }

        var doc = await _fetch.FetchAsync(url, etag: null, ct);
        var truncated = doc.TextContent.Length > MaxTextChars;
        var text = truncated ? doc.TextContent[..MaxTextChars] : doc.TextContent;

        // Fetch wirft designbedingt nicht (leeres Dokument bei Fehlern) — leeren Text
        // dem LLM als möglichen Fehler kennzeichnen.
        return text.Length == 0
            ? JsonSerializer.SerializeToElement(new
            {
                ok = true,
                url = doc.Url,
                status = doc.StatusCode,
                text,
                truncated,
                hint = "Seite lieferte keinen Text (Fehler, Block oder leere Seite).",
            })
            : JsonSerializer.SerializeToElement(new { ok = true, url = doc.Url, status = doc.StatusCode, text, truncated });
    }
}
```

- [ ] **Step 4: Tests laufen lassen — müssen grün sein**

Run: `dotnet test src/NauAssist.slnx --filter FetchWebpageToolTests`
Expected: 4 Tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Web/Tools/FetchWebpageTool.cs src/Backend.Tests/Features/Web/FetchWebpageToolTests.cs
git commit -m "feat(backend): fetch_webpage-Chat-Tool auf SSRF-gehärtetem Fetch"
```

---

### Task 4: Komponierbare `AgentOperatingRules` (TDD)

**Files:**
- Modify: `src/Backend/Features/Agent/AgentOperatingRules.cs`
- Modify: `src/Backend/Features/Agent/AgentRunner.cs:49`
- Test: `src/Backend.Tests/Features/Agent/AgentOperatingRulesTests.cs` (neu)

**Interfaces:**
- Produces: `AgentOperatingRules.Compose(IEnumerable<string> toolNames)` → `string`. Der Header bleibt `[Agent-Spielregeln — verbindlich]` (bestehende Tests asserten `Contains("[Agent-Spielregeln")`).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
using AwesomeAssertions;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentOperatingRulesTests
{
    private static readonly string[] BaseTools = ["lookup_free_slots", "create_event"];

    [Fact]
    public void Compose_ContainsHeaderAndBaseRules_Always()
    {
        var text = AgentOperatingRules.Compose(BaseTools);

        text.Should().StartWith("[Agent-Spielregeln — verbindlich]");
        text.Should().Contain("lookup_free_slots");
        text.Should().Contain("Zeit-Kontext-Block");
    }

    [Fact]
    public void Compose_WatchJobParagraph_OnlyWithWatchJobTool()
    {
        AgentOperatingRules.Compose(BaseTools).Should().NotContain("create_watch_job");
        AgentOperatingRules.Compose([.. BaseTools, "create_watch_job"]).Should().Contain("create_watch_job");
    }

    [Fact]
    public void Compose_WebParagraph_OnlyWithWebSearchTool()
    {
        AgentOperatingRules.Compose(BaseTools).Should().NotContain("web_search");
        var withWeb = AgentOperatingRules.Compose([.. BaseTools, "web_search"]);
        withWeb.Should().Contain("web_search");
        withWeb.Should().Contain("fetch_webpage");
    }
}
```

- [ ] **Step 2: Tests laufen lassen — müssen scheitern**

Run: `dotnet test src/NauAssist.slnx --filter AgentOperatingRulesTests`
Expected: Compile-Fehler `Compose` existiert nicht.

- [ ] **Step 3: Implementierung**

`AgentOperatingRules.cs` — die Konstante `Text` wird durch `Compose` ersetzt. Der bestehende Regeltext wird in drei Blöcke zerlegt (Inhalt 1:1 übernehmen, nicht umformulieren):

```csharp
namespace NauAssist.Backend.Features.Agent;

/// <summary>
/// Fixe, im Backend verdrahtete Spielregeln für den Agenten — werden bei jedem
/// Lauf vor den User-konfigurierten <c>SystemPrompt</c> gehängt. Hier landet
/// alles, was Tool-Verhalten betrifft (welches Tool wann, Datumsformate,
/// Bestätigungs-Konventionen). Absätze zu optionalen Tools (Watch-Jobs, Web)
/// erscheinen nur, wenn die Tools tatsächlich registriert sind — sonst
/// halluziniert das LLM Calls auf nicht existente Tools.
/// </summary>
internal static class AgentOperatingRules
{
    public static string Compose(IEnumerable<string> toolNames)
    {
        var tools = toolNames as ISet<string> ?? new HashSet<string>(toolNames, StringComparer.Ordinal);

        var text = Header + BaseToolRules;
        if (tools.Contains("create_watch_job")) text += WatchJobRules;
        if (tools.Contains("web_search")) text += WebRules;
        text += DateTimeRules;
        return text;
    }

    private const string Header =
        "[Agent-Spielregeln — verbindlich]\n" +
        "\n" +
        "Tools & Workflows:\n";

    private const string BaseToolRules = /* bisherige Bullet-Zeilen 1–4 (lookup_free_slots …, Regel-Eingaben …, Termin löschen …, update_event …, Serien-Instanzen …) unverändert */;

    private const string WatchJobRules = /* bisherige Watch-Job-Bullet-Zeile unverändert */;

    private const string WebRules =
        "- Fragen nach aktuellen Informationen (News, Preise, Öffnungszeiten, Verfügbarkeiten, Fakten außerhalb deines Wissens): " +
        "rufe web_search mit einer präzisen Suchanfrage. Reichen die Snippets nicht, lies die vielversprechendste URL per fetch_webpage. " +
        "Nenne in der Antwort die Quelle (URL). Keine Web-Suche für Kalender-/Regel-Aktionen oder Smalltalk.\n";

    private const string DateTimeRules = /* bisherige Blöcke "Datums-/Zeitformat:" und "Längerfristiger Kontext:" unverändert, beginnend mit "\nDatums-/Zeitformat:\n" */;
}
```

Wichtig: Die `/* … */`-Kommentare sind Abschreibe-Anweisungen — der Inhalt steht wörtlich in der bisherigen `Text`-Konstante (`AgentOperatingRules.cs`, Zeilen 12–32); nichts umformulieren, nur zerschneiden. Die Watch-Job-Zeile endet mit `\n`, danach folgt in `DateTimeRules` `"\nDatums-/Zeitformat:\n" + …`.

`AgentRunner.cs` Zeile 49:

```csharp
new LlmMessage("system", AgentOperatingRules.Compose(_tools.Keys)),
```

- [ ] **Step 4: Kompletter Testlauf**

Run: `dotnet test src/NauAssist.slnx`
Expected: alle Tests grün (die bestehenden AgentRunner-Tests asserten nur den `[Agent-Spielregeln`-Header — bleiben grün).

- [ ] **Step 5: Commit**

```bash
git add src/Backend/Features/Agent/AgentOperatingRules.cs src/Backend/Features/Agent/AgentRunner.cs src/Backend.Tests/Features/Agent/AgentOperatingRulesTests.cs
git commit -m "feat(backend): Operating-Rules komponierbar – Absätze nur für registrierte Tools"
```

---

### Task 5: Registrierung & Gating in `Program.cs`

**Files:**
- Modify: `src/Backend/Program.cs` (Web-DI-Block, ca. Zeilen 166–192 nach Task 1)

**Interfaces:**
- Consumes: `WebSearchTool` (Task 2), `FetchWebpageTool` (Task 3), `WebOptions` (Task 1).

- [ ] **Step 1: Gating implementieren**

Im Web-/WatchJobs-DI-Block von `Program.cs` — `using NauAssist.Backend.Features.Web.Tools;` ergänzen, `WebOptions` eager lesen und die Tools bedingt registrieren:

```csharp
var webOptions = builder.Configuration.GetSection("Web").Get<WebOptions>() ?? new WebOptions();

// Web-Chat-Tools nur anbieten, wenn eine SearXNG-Instanz konfiguriert ist — ohne
// Such-Backend wären web_search/fetch_webpage tote Tools im Prompt.
if (!string.IsNullOrEmpty(webOptions.SearxngBaseUrl))
{
    builder.Services.AddScoped<ITool, WebSearchTool>();
    builder.Services.AddScoped<ITool, FetchWebpageTool>();
}
```

Platzierung: direkt nach `builder.Services.AddScoped<IWebFetch, HttpWebFetch>();`, vor dem `if (watchJobOptions.Enabled)`-Block. (Kein eigener Test — DI-Verdrahtung folgt dem Watch-Jobs-Präzedenzfall; die Tools selbst sind in Task 2/3 getestet.)

- [ ] **Step 2: Build + kompletter Testlauf**

Run: `dotnet test src/NauAssist.slnx`
Expected: alle Tests grün.

- [ ] **Step 3: Commit**

```bash
git add src/Backend/Program.cs
git commit -m "feat(backend): web_search/fetch_webpage registriert, wenn SearXNG konfiguriert"
```

---

### Task 6: Frontend-Labels für die neuen Tools

**Files:**
- Modify: `src/frontend/src/components/ChatView.tsx:19-27` (`TOOL_STATUS_LABEL`)

**Interfaces:**
- Consumes: Tool-Namen `web_search`, `fetch_webpage` (Task 2/3) — Backend streamt sie via `ToolStartedEvent`.

- [ ] **Step 1: Labels ergänzen**

```typescript
const TOOL_STATUS_LABEL: Record<string, string> = {
  lookup_free_slots: "SUCHE FREIE SLOTS",
  get_calendar_range: "LESE KALENDER",
  create_event: "LEGE TERMIN AN",
  add_rule: "SPEICHERE REGEL",
  delete_rule: "LÖSCHE REGEL",
  list_rules: "LADE REGELN",
  present_proposals: "BEREITE VORSCHLÄGE VOR",
  web_search: "SUCHE IM WEB",
  fetch_webpage: "LESE WEBSEITE",
};
```

- [ ] **Step 2: Lint + Build**

Run: `cd src/frontend && npm run lint && npm run build`
Expected: beide ohne Fehler.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/components/ChatView.tsx
git commit -m "feat(frontend): Statuslabels für web_search/fetch_webpage"
```

---

### Task 7: Verifikation & PR

- [ ] **Step 1: Kompletter Testlauf Backend + Frontend-Build**

Run: `dotnet test src/NauAssist.slnx && cd src/frontend && npm run lint && npm run build`
Expected: alle Backend-Tests grün (299 + 11 neue = 310), Frontend-Build ok.

- [ ] **Step 2: Push + PR gegen `main`**

```bash
git push -u origin feat/websearch-chat-tools
gh pr create --title "feat: WebSearch im Chat – web_search & fetch_webpage Tools" --body "…(Zusammenfassung aus Spec: Ziel, Architektur-Entscheidungen, Gating, Tests)…"
```

Expected: PR offen, CI (inkl. naudit-review, fail-closed) läuft.
