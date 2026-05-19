# Plan C — LLM-Client & Agent-Runner — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** LLM-Client-Abstraktion (`ILlmClient` mit `FakeLlmClient` + `OllamaLlmClient`), 7 Tool-Adapter (6 über Mediator + `present_proposals` als Side-Effect), und ein schmaler `AgentRunner`, der Konversationen orchestriert (LLM-Call → ggf. Tool-Loop → finale Text-Antwort), alles streaming-tauglich via `IAsyncEnumerable<AgentStreamEvent>`.

**Architecture:** `AgentRunner.HandleAsync` ist eine async-streaming-Methode, die `LlmStreamChunk`s vom `ILlmClient` konsumiert. Text-Chunks werden zu `TokenEvent`s, Tool-Call-Chunks werden synchron ausgeführt (über `ITool` → Mediator) und ihre Ergebnisse an die Konversation angehängt. Bei jeder Tool-Iteration startet der Runner einen neuen LLM-Stream. Stop-Bedingungen: LLM liefert nur Text (ohne weitere Tool-Calls), oder Tool-Loop-Limit von 5 ist erreicht. Das spezielle `present_proposals`-Tool wird vom Runner direkt abgefangen und löst ein `ProposalsEvent` aus, ohne über Mediator zu gehen.

**Abweichung von der Spec:** Statt Microsoft Agent Framework wird ein eigener `AgentRunner` implementiert (ca. 200 Zeilen). Begründung: MAF ist preview/early, default-mäßig auf OpenAI/Azure-Endpoints zugeschnitten, und für eine Single-User-MVP-Anwendung ist die Eigen-Implementierung schneller, kleiner und vollständig unter unserer Kontrolle. Wenn später Multi-Agent-Orchestrierung gewünscht wird, ist der Austausch gegen MAF rein mechanisch, weil `ILlmClient` und `ITool` als stabile Interfaces fungieren.

**Tech Stack:** .NET 10 · HttpClient (gegen Ollamas OpenAI-kompatibles Endpoint) · System.Text.Json · vorhandene Mediator/SQLite-Infrastruktur

**Bezug zur Spec:** `docs/superpowers/specs/2026-05-19-kalender-agent-mvp-design.md`, Abschnitte 6.2 (Agent Runner), 6.3 (LLM Client), 6.5 (Rules-Tools), Tool-Tabelle.

**Was am Ende dieses Plans steht:**
- `ILlmClient` mit `FakeLlmClient` (gescriptete Stream-Sequenzen) und `OllamaLlmClient` (HTTP)
- 7 Tools, in DI registriert: `lookup_free_slots`, `create_event`, `get_calendar_range`, `list_rules`, `add_rule`, `delete_rule`, `present_proposals`
- `AgentRunner` mit Tool-Loop, 5-Iterationen-Limit, Streaming
- `AgentStreamEvent`-Hierarchie (Token, ToolStarted, ToolFinished, Proposals, Done, Error)
- Vollständige Test-Suite gegen `FakeLlmClient` — deterministisch reproduzierbare End-to-End-Agent-Konversationen
- Noch KEIN Chat-Endpoint und KEIN SSE — das ist Plan D

---

## Datei-Übersicht (für diesen Plan)

**Neu anzulegen:**

| Pfad | Verantwortung |
|---|---|
| `src/Backend/Features/Infrastructure/Llm/ILlmClient.cs` | LLM-Abstraktion + Message/Chunk/ToolDefinition-Typen |
| `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaLlmClient.cs` | HTTP-Wrapper für Ollamas `/v1/chat/completions` |
| `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs` | Config (Host, Modell, Timeouts) |
| `src/Backend/Features/Agent/ITool.cs` | Tool-Interface (Name, Schema, Execute) |
| `src/Backend/Features/Agent/Tools/LookupFreeSlotsTool.cs` | Adapter → Mediator |
| `src/Backend/Features/Agent/Tools/CreateEventTool.cs` | Adapter → Mediator |
| `src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs` | Adapter → Mediator |
| `src/Backend/Features/Agent/Tools/ListRulesTool.cs` | Adapter → Mediator |
| `src/Backend/Features/Agent/Tools/AddRuleTool.cs` | Adapter → Mediator |
| `src/Backend/Features/Agent/Tools/DeleteRuleTool.cs` | Adapter → Mediator |
| `src/Backend/Features/Agent/Tools/PresentProposalsTool.cs` | Side-Effect-Marker (Schema only) |
| `src/Backend/Features/Agent/AgentStreamEvent.cs` | Event-Hierarchie (Token, ToolStarted, ...) |
| `src/Backend/Features/Agent/SlotInfo.cs` | DTO für `present_proposals` |
| `src/Backend/Features/Agent/AgentRunner.cs` | Orchestrierung mit Tool-Loop und Streaming |
| `src/Backend/Features/Agent/AgentOptions.cs` | Tool-Loop-Limit etc. |
| `src/Backend.Tests/Helpers/FakeLlmClient.cs` | Gescriptete Stream-Sequenzen |
| `src/Backend.Tests/Features/Agent/AgentStreamEventTests.cs` | Sanity-Tests für Event-Typen |
| `src/Backend.Tests/Features/Agent/FakeLlmClientTests.cs` | Tests des Fakes selbst |
| `src/Backend.Tests/Features/Agent/Tools/ToolAdapterTests.cs` | Pro Tool: Args parsen, Mediator-Aufruf, Result-Mapping |
| `src/Backend.Tests/Features/Agent/AgentRunnerTests.cs` | End-to-End-Konversations-Tests |

**Zu modifizieren:**

| Pfad | Änderung |
|---|---|
| `src/Backend/Program.cs` | DI-Registrierungen für `ILlmClient`, alle 7 Tools, AgentRunner, Options |
| `src/Backend/appsettings.json` | `Ollama`- und `Agent`-Sektionen |

---

## Task 1: LLM-Abstraktion (ILlmClient + Typen)

**Files:**
- Create: `src/Backend/Features/Infrastructure/Llm/ILlmClient.cs`

Eine einzelne Datei mit Interface und allen LLM-relevanten Typen. Klein gehalten, weil sie zentral und stabil sein soll.

- [ ] **Step 1: Datei anlegen**

Datei `src/Backend/Features/Infrastructure/Llm/ILlmClient.cs`:

```csharp
using System.Text.Json;

namespace NauAssist.Backend.Features.Infrastructure.Llm;

/// <summary>
/// Schmaler Wrapper um einen OpenAI-kompatiblen Chat-Endpoint mit Streaming und Tool-Calls.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sendet die Konversation an das LLM und gibt einen Stream von Chunks zurück.
    /// Chunks sind entweder Text-Deltas (mehrere Tokens) oder vollständige Tool-Calls
    /// (Ollama streamt Tool-Calls atomar, nicht token-weise).
    /// </summary>
    IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}

/// <summary>OpenAI-kompatible Chat-Message.</summary>
public sealed record LlmMessage(
    string Role,                                    // "system" | "user" | "assistant" | "tool"
    string? Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,   // wenn role=assistant und Tool-Aufruf
    string? ToolCallId = null);                     // wenn role=tool, referenziert ToolCall.Id

public sealed record LlmToolCall(
    string Id,
    string Name,
    JsonElement Arguments);

/// <summary>Tool-Definition, die dem LLM mitgegeben wird (für die Funktions-Auswahl).</summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParameterSchema);     // JSON-Schema-Object

/// <summary>Ein Chunk im Stream — entweder Text-Delta oder ein vollständiger Tool-Call.</summary>
public abstract record LlmStreamChunk;
public sealed record TextDeltaChunk(string Text) : LlmStreamChunk;
public sealed record ToolCallChunk(LlmToolCall Call) : LlmStreamChunk;
```

- [ ] **Step 2: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: 0 Errors.

- [ ] **Step 3: Commit**

```bash
git add src/
git commit -m "Plan C Task 1: LLM-Abstraktion (ILlmClient, LlmMessage, LlmStreamChunk, ToolDefinition)"
```

---

## Task 2: FakeLlmClient

**Files:**
- Create: `src/Backend.Tests/Helpers/FakeLlmClient.cs`
- Create: `src/Backend.Tests/Features/Agent/FakeLlmClientTests.cs`

Skriptbarer LLM-Fake. Speichert eine Liste vorgefertigter "Antwort-Sequenzen" und gibt sie der Reihe nach aus. Jede Sequenz ist eine Liste von Chunks (Text-Deltas und/oder Tool-Calls), die in einem einzigen `ChatStreamAsync`-Aufruf zurückgegeben werden.

- [ ] **Step 1: Failing-Tests schreiben**

Datei `src/Backend.Tests/Features/Agent/FakeLlmClientTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class FakeLlmClientTests
{
    [Fact]
    public async Task ChatStream_ReturnsScriptedChunks_InOrder()
    {
        var fake = new FakeLlmClient();
        fake.QueueResponse(new TextDeltaChunk("Hallo"), new TextDeltaChunk(" Welt"));

        var chunks = new List<LlmStreamChunk>();
        await foreach (var c in fake.ChatStreamAsync(
            new List<LlmMessage> { new("user", "Hi") },
            Array.Empty<ToolDefinition>(),
            CancellationToken.None))
        {
            chunks.Add(c);
        }

        chunks.Should().HaveCount(2);
        chunks[0].Should().BeOfType<TextDeltaChunk>().Which.Text.Should().Be("Hallo");
        chunks[1].Should().BeOfType<TextDeltaChunk>().Which.Text.Should().Be(" Welt");
    }

    [Fact]
    public async Task ChatStream_ConsecutiveCalls_UseConsecutiveQueuedResponses()
    {
        var fake = new FakeLlmClient();
        fake.QueueResponse(new TextDeltaChunk("erste"));
        fake.QueueResponse(new TextDeltaChunk("zweite"));

        var first = await Collect(fake);
        var second = await Collect(fake);

        ((TextDeltaChunk)first[0]).Text.Should().Be("erste");
        ((TextDeltaChunk)second[0]).Text.Should().Be("zweite");
    }

    [Fact]
    public async Task ChatStream_WhenQueueEmpty_Throws()
    {
        var fake = new FakeLlmClient();

        var act = async () => await Collect(fake);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChatStream_CapturesMessagesAndToolsForInspection()
    {
        var fake = new FakeLlmClient();
        fake.QueueResponse(new TextDeltaChunk("ok"));
        var tools = new[] { new ToolDefinition("foo", "Foo desc", JsonDocument.Parse("{}").RootElement) };

        await Collect(fake, new List<LlmMessage> { new("user", "Test") }, tools);

        fake.CapturedCalls.Should().HaveCount(1);
        fake.CapturedCalls[0].Messages.Should().ContainSingle(m => m.Role == "user");
        fake.CapturedCalls[0].Tools.Should().ContainSingle(t => t.Name == "foo");
    }

    private static async Task<List<LlmStreamChunk>> Collect(
        FakeLlmClient fake,
        IReadOnlyList<LlmMessage>? msgs = null,
        IReadOnlyList<ToolDefinition>? tools = null)
    {
        var list = new List<LlmStreamChunk>();
        await foreach (var c in fake.ChatStreamAsync(
            msgs ?? Array.Empty<LlmMessage>(),
            tools ?? Array.Empty<ToolDefinition>(),
            CancellationToken.None))
        {
            list.Add(c);
        }
        return list;
    }
}
```

- [ ] **Step 2: FakeLlmClient schreiben**

Datei `src/Backend.Tests/Helpers/FakeLlmClient.cs`:

```csharp
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// Skriptbarer LLM-Fake für deterministische Tests.
/// Pro Aufruf wird die nächste gequeuede Response konsumiert.
/// </summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<IReadOnlyList<LlmStreamChunk>> _scripted = new();
    private readonly List<CapturedCall> _captured = new();

    public IReadOnlyList<CapturedCall> CapturedCalls => _captured;

    /// <summary>Fügt eine Response-Sequenz hinzu, die beim nächsten Aufruf zurückgegeben wird.</summary>
    public void QueueResponse(params LlmStreamChunk[] chunks)
    {
        _scripted.Enqueue(chunks);
    }

    public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _captured.Add(new CapturedCall(messages.ToList(), tools.ToList()));

        if (_scripted.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeLlmClient hat keine gequeuede Response — vor dem Aufruf QueueResponse aufrufen.");
        }

        var response = _scripted.Dequeue();
        foreach (var chunk in response)
        {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }

    public sealed record CapturedCall(
        IReadOnlyList<LlmMessage> Messages,
        IReadOnlyList<ToolDefinition> Tools);
}
```

- [ ] **Step 3: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~FakeLlmClientTests"
```

Expected: 4 Tests grün.

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "Plan C Task 2: FakeLlmClient mit Capture-Mechanik für Tests"
```

---

## Task 3: OllamaLlmClient

**Files:**
- Create: `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs`
- Create: `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaLlmClient.cs`

Echte HTTP-Implementierung gegen `POST /v1/chat/completions` von Ollama. Server-Sent-Events-Stream parsen, in `LlmStreamChunk`s umsetzen. Wird in der Test-Suite **nicht** direkt verifiziert (das wäre ein Integration-Test gegen echtes Ollama). Stattdessen reicht uns, dass es kompiliert und die Stream-Parsing-Logik nachvollziehbar ist.

- [ ] **Step 1: Options schreiben**

Datei `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaOptions.cs`:

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Llm.Ollama;

public sealed class OllamaOptions
{
    public string Host { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b-instruct";

    /// <summary>Maximalzeit, bis der erste Chunk vom LLM zurückkommt.</summary>
    public int InitialTimeoutSeconds { get; set; } = 60;

    /// <summary>Maximalzeit zwischen zwei aufeinander folgenden Chunks.</summary>
    public int TokenTimeoutSeconds { get; set; } = 30;

    /// <summary>Optionaler System-Prompt, der jeder Konversation vorangestellt wird.</summary>
    public string? SystemPrompt { get; set; }
}
```

- [ ] **Step 2: OllamaLlmClient schreiben**

Datei `src/Backend/Features/Infrastructure/Llm/Ollama/OllamaLlmClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NauAssist.Backend.Features.Infrastructure.Llm.Ollama;

public sealed class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaLlmClient> _logger;

    public OllamaLlmClient(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaLlmClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var payload = BuildPayload(messages, tools);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(payload),
        };

        using var initialCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        initialCts.CancelAfter(TimeSpan.FromSeconds(_options.InitialTimeoutSeconds));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, initialCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Partial-Buffer für Tool-Call-Args (werden manchmal über mehrere Deltas verteilt geschickt)
        var toolCallBuffer = new Dictionary<int, ToolCallBuilder>();

        while (!reader.EndOfStream)
        {
            using var tokenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tokenCts.CancelAfter(TimeSpan.FromSeconds(_options.TokenTimeoutSeconds));

            var line = await reader.ReadLineAsync(tokenCts.Token);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            // Text-Delta
            if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    yield return new TextDeltaChunk(text);
                }
            }

            // Tool-Call-Delta (kann inkrementell kommen)
            if (delta.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tcEl in toolCallsEl.EnumerateArray())
                {
                    var index = tcEl.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                    if (!toolCallBuffer.TryGetValue(index, out var builder))
                    {
                        builder = new ToolCallBuilder();
                        toolCallBuffer[index] = builder;
                    }

                    if (tcEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        builder.Id ??= idEl.GetString();

                    if (tcEl.TryGetProperty("function", out var fnEl))
                    {
                        if (fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            builder.Name ??= nameEl.GetString();
                        if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            builder.ArgumentsBuffer.Append(argsEl.GetString());
                    }
                }
            }

            // Finish-Reason "tool_calls" → vorhandene Tool-Calls jetzt emittieren
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String && fr.GetString() == "tool_calls")
            {
                foreach (var b in toolCallBuffer.Values)
                {
                    if (b.Name is null) continue;
                    JsonElement args;
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(b.ArgumentsBuffer.Length > 0 ? b.ArgumentsBuffer.ToString() : "{}");
                        args = argsDoc.RootElement.Clone();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Tool-Call-Args sind kein gültiges JSON: '{Raw}'", b.ArgumentsBuffer);
                        continue;
                    }
                    yield return new ToolCallChunk(new LlmToolCall(b.Id ?? Guid.NewGuid().ToString(), b.Name, args));
                }
                toolCallBuffer.Clear();
            }
        }
    }

    private object BuildPayload(IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition> tools)
    {
        var serializedMessages = new List<object>();

        if (!string.IsNullOrEmpty(_options.SystemPrompt))
        {
            serializedMessages.Add(new { role = "system", content = _options.SystemPrompt });
        }

        foreach (var m in messages)
        {
            var dict = new Dictionary<string, object?> { ["role"] = m.Role };
            if (m.Content is not null) dict["content"] = m.Content;
            if (m.ToolCallId is not null) dict["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is not null)
            {
                dict["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments.GetRawText() },
                }).ToArray();
            }
            serializedMessages.Add(dict);
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = serializedMessages,
            ["stream"] = true,
        };

        if (tools.Count > 0)
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonDocument.Parse(t.ParameterSchema.GetRawText()).RootElement,
                },
            }).ToArray();
        }

        return payload;
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder ArgumentsBuffer { get; } = new();
    }
}
```

- [ ] **Step 3: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "Plan C Task 3: OllamaLlmClient mit Stream-Parsing für Text-Deltas + Tool-Calls"
```

---

## Task 4: ITool-Interface

**Files:**
- Create: `src/Backend/Features/Agent/ITool.cs`

- [ ] **Step 1: Datei anlegen**

Datei `src/Backend/Features/Agent/ITool.cs`:

```csharp
using System.Text.Json;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Agent;

/// <summary>
/// Ein Tool ist ein Adapter zwischen LLM-Tool-Call und einer fachlichen Aktion.
/// Die meisten Tools rufen intern Mediator.Send. Das spezielle present_proposals-Tool
/// wird vom AgentRunner direkt abgefangen (siehe dort).
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParameterSchema { get; }
    Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct);

    /// <summary>Hilfsmethode für AgentRunner: zur ToolDefinition für den LLM-Call.</summary>
    ToolDefinition ToDefinition() => new(Name, Description, ParameterSchema);
}
```

- [ ] **Step 2: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: 0 Errors.

- [ ] **Step 3: Commit**

```bash
git add src/
git commit -m "Plan C Task 4: ITool-Interface"
```

---

## Task 5: Sechs Mediator-Tool-Adapter

**Files (alle neu):**
- `src/Backend/Features/Agent/Tools/LookupFreeSlotsTool.cs`
- `src/Backend/Features/Agent/Tools/CreateEventTool.cs`
- `src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs`
- `src/Backend/Features/Agent/Tools/ListRulesTool.cs`
- `src/Backend/Features/Agent/Tools/AddRuleTool.cs`
- `src/Backend/Features/Agent/Tools/DeleteRuleTool.cs`
- `src/Backend.Tests/Features/Agent/Tools/ToolAdapterTests.cs`

Jeder Adapter folgt demselben Muster: Schema definieren (JSON-Schema-Objekt), Args parsen, Mediator.Send mit zugehörigem Request, Result-Objekt als JsonElement zurückgeben.

- [ ] **Step 1: Failing-Tests schreiben (eine Test-Datei für alle sechs Tools)**

Datei `src/Backend.Tests/Features/Agent/Tools/ToolAdapterTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Mediator;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;
using NauAssist.Backend.Features.Rules.DeleteRule;
using NauAssist.Backend.Features.Rules.ListRules;
using NauAssist.Backend.Features.Calendar.CreateEvent;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent.Tools;

public sealed class ToolAdapterTests
{
    [Fact]
    public async Task LookupFreeSlotsTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<LookupFreeSlotsRequest, LookupFreeSlotsResponse>(
            new LookupFreeSlotsResponse(Array.Empty<SlotAnnotation>()));
        var tool = new LookupFreeSlotsTool(mediator);

        var args = JsonDocument.Parse("""
            {"from":"2026-05-27T10:00:00+02:00","to":"2026-05-27T18:00:00+02:00","duration_minutes":60}
            """).RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        mediator.SentRequests.Should().ContainSingle();
        var req = (LookupFreeSlotsRequest)mediator.SentRequests[0];
        req.DurationMinutes.Should().Be(60);
        result.TryGetProperty("annotations", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CreateEventTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<CreateEventRequest, CreateEventResponse>(
            new CreateEventResponse("event-id-42"));
        var tool = new CreateEventTool(mediator);

        var args = JsonDocument.Parse("""
            {"title":"Pierre","start":"2026-05-27T14:00:00+02:00","end":"2026-05-27T15:00:00+02:00"}
            """).RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        var req = (CreateEventRequest)mediator.SentRequests[0];
        req.Title.Should().Be("Pierre");
        result.GetProperty("event_id").GetString().Should().Be("event-id-42");
    }

    [Fact]
    public async Task GetCalendarRangeTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<GetCalendarRangeRequest, GetCalendarRangeResponse>(
            new GetCalendarRangeResponse(Array.Empty<CalendarEvent>()));
        var tool = new GetCalendarRangeTool(mediator);

        var args = JsonDocument.Parse("""
            {"from":"2026-05-27T00:00:00+02:00","to":"2026-05-28T00:00:00+02:00"}
            """).RootElement;

        await tool.ExecuteAsync(args, CancellationToken.None);

        mediator.SentRequests.Should().ContainSingle(r => r is GetCalendarRangeRequest);
    }

    [Fact]
    public async Task ListRulesTool_NoArgs_ReturnsList()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<ListRulesRequest, ListRulesResponse>(
            new ListRulesResponse(Array.Empty<Rule>()));
        var tool = new ListRulesTool(mediator);

        var result = await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        result.TryGetProperty("rules", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AddRuleTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        var rule = new Rule(1, "Mi 19-20", DayOfWeekFlags.Wednesday,
            new TimeOnly(19, 0), new TimeOnly(20, 0), RuleHardness.Hard, DateTimeOffset.UtcNow);
        mediator.SetupResponse<AddRuleRequest, AddRuleResponse>(new AddRuleResponse(rule));
        var tool = new AddRuleTool(mediator);

        var args = JsonDocument.Parse("""
            {"text":"Mi 19-20 Sport","days_of_week":["wednesday"],"time_start":"19:00","time_end":"20:00","hardness":"hard"}
            """).RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        var req = (AddRuleRequest)mediator.SentRequests[0];
        req.DaysOfWeek.Should().Be(DayOfWeekFlags.Wednesday);
        req.TimeRangeStart.Should().Be(new TimeOnly(19, 0));
        result.GetProperty("rule_id").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task DeleteRuleTool_ParsesArgs_AndSendsRequest()
    {
        var mediator = new FakeMediator();
        mediator.SetupResponse<DeleteRuleRequest, DeleteRuleResponse>(new DeleteRuleResponse(true));
        var tool = new DeleteRuleTool(mediator);

        var args = JsonDocument.Parse("""{"rule_id":42}""").RootElement;

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        ((DeleteRuleRequest)mediator.SentRequests[0]).Id.Should().Be(42);
        result.GetProperty("deleted").GetBoolean().Should().BeTrue();
    }
}
```

- [ ] **Step 2: FakeMediator-Helper schreiben**

Datei `src/Backend.Tests/Helpers/FakeMediator.cs`:

```csharp
using Mediator;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// Minimaler Mediator-Stub für Tool-Tests. Erlaubt Setup pro Request-Typ und sammelt
/// alle gesendeten Requests zur späteren Inspektion.
/// </summary>
public sealed class FakeMediator : IMediator
{
    private readonly Dictionary<Type, object> _responses = new();
    private readonly List<object> _sent = new();

    public IReadOnlyList<object> SentRequests => _sent;

    public void SetupResponse<TRequest, TResponse>(TResponse response)
        where TRequest : IRequest<TResponse>
    {
        _responses[typeof(TRequest)] = response!;
    }

    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _sent.Add(request);
        if (_responses.TryGetValue(request.GetType(), out var resp))
        {
            return ValueTask.FromResult((TResponse)resp);
        }
        throw new InvalidOperationException($"FakeMediator hat keine Response für {request.GetType().Name}.");
    }

    public ValueTask<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wird in den Tool-Adapter-Tests nicht benötigt.");

    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => ValueTask.CompletedTask;

    public ValueTask Publish(object notification, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wird in den Tool-Adapter-Tests nicht benötigt.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wird in den Tool-Adapter-Tests nicht benötigt.");
}
```

- [ ] **Step 3: LookupFreeSlotsTool schreiben**

Datei `src/Backend/Features/Agent/Tools/LookupFreeSlotsTool.cs`:

```csharp
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class LookupFreeSlotsTool : ITool
{
    public string Name => "lookup_free_slots";
    public string Description =>
        "Sucht freie Slots im Kalender für einen Zeitbereich. Berücksichtigt aktive Regeln. " +
        "Liefert eine annotierte Liste von Kandidaten — der Agent wählt 2–3 daraus und ruft danach present_proposals.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "format": "date-time", "description": "ISO-8601-Beginn des Suchbereichs" },
            "to":   { "type": "string", "format": "date-time", "description": "ISO-8601-Ende des Suchbereichs (exklusiv)" },
            "duration_minutes": { "type": "integer", "minimum": 1, "description": "Gewünschte Slot-Länge in Minuten" }
          },
          "required": ["from", "to", "duration_minutes"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public LookupFreeSlotsTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var from = DateTimeOffset.Parse(args.GetProperty("from").GetString()!);
        var to = DateTimeOffset.Parse(args.GetProperty("to").GetString()!);
        var duration = args.GetProperty("duration_minutes").GetInt32();

        var response = await _mediator.Send(new LookupFreeSlotsRequest(from, to, duration), ct);

        var resultObj = new
        {
            annotations = response.Annotations.Select(a => new
            {
                start = a.Slot.Start.ToString("O"),
                end = a.Slot.End.ToString("O"),
                status = a.Status.ToString().ToLowerInvariant(),
                violated_by = a.ViolatedBy is null ? null : new { id = a.ViolatedBy.Id, text = a.ViolatedBy.Text },
            }),
        };
        return JsonSerializer.SerializeToElement(resultObj);
    }
}
```

- [ ] **Step 4: CreateEventTool schreiben**

Datei `src/Backend/Features/Agent/Tools/CreateEventTool.cs`:

```csharp
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.CreateEvent;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class CreateEventTool : ITool
{
    public string Name => "create_event";
    public string Description => "Legt einen neuen Termin im Kalender an, nachdem der User bestätigt hat.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Titel des Termins" },
            "start": { "type": "string", "format": "date-time" },
            "end":   { "type": "string", "format": "date-time" },
            "description": { "type": ["string", "null"] },
            "location":    { "type": ["string", "null"] }
          },
          "required": ["title", "start", "end"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public CreateEventTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var request = new CreateEventRequest(
            Title: args.GetProperty("title").GetString()!,
            Start: DateTimeOffset.Parse(args.GetProperty("start").GetString()!),
            End: DateTimeOffset.Parse(args.GetProperty("end").GetString()!),
            Description: args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null,
            Location: args.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.String ? locEl.GetString() : null);

        var response = await _mediator.Send(request, ct);
        return JsonSerializer.SerializeToElement(new { event_id = response.EventId, status = "created" });
    }
}
```

- [ ] **Step 5: GetCalendarRangeTool schreiben**

Datei `src/Backend/Features/Agent/Tools/GetCalendarRangeTool.cs`:

```csharp
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Calendar.GetCalendarRange;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class GetCalendarRangeTool : ITool
{
    public string Name => "get_calendar_range";
    public string Description => "Liefert alle Termine im angefragten Zeitbereich (z. B. um Kontext zu schaffen).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "format": "date-time" },
            "to":   { "type": "string", "format": "date-time" }
          },
          "required": ["from", "to"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public GetCalendarRangeTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var from = DateTimeOffset.Parse(args.GetProperty("from").GetString()!);
        var to = DateTimeOffset.Parse(args.GetProperty("to").GetString()!);

        var response = await _mediator.Send(new GetCalendarRangeRequest(from, to), ct);
        var resultObj = new
        {
            events = response.Events.Select(e => new
            {
                id = e.Id,
                title = e.Title,
                start = e.Start.ToString("O"),
                end = e.End.ToString("O"),
                description = e.Description,
                location = e.Location,
            }),
        };
        return JsonSerializer.SerializeToElement(resultObj);
    }
}
```

- [ ] **Step 6: ListRulesTool schreiben**

Datei `src/Backend/Features/Agent/Tools/ListRulesTool.cs`:

```csharp
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Rules.ListRules;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class ListRulesTool : ITool
{
    public string Name => "list_rules";
    public string Description => "Listet alle gespeicherten Regeln auf.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    private readonly IMediator _mediator;

    public ListRulesTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var response = await _mediator.Send(new ListRulesRequest(), ct);
        var resultObj = new
        {
            rules = response.Rules.Select(r => new
            {
                id = r.Id,
                text = r.Text,
                days_of_week = (int)r.DaysOfWeek,
                time_start = r.TimeRangeStart?.ToString("HH:mm"),
                time_end = r.TimeRangeEnd?.ToString("HH:mm"),
                hardness = r.Hardness.ToString().ToLowerInvariant(),
            }),
        };
        return JsonSerializer.SerializeToElement(resultObj);
    }
}
```

- [ ] **Step 7: AddRuleTool schreiben**

Datei `src/Backend/Features/Agent/Tools/AddRuleTool.cs`:

```csharp
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class AddRuleTool : ITool
{
    public string Name => "add_rule";
    public string Description =>
        "Speichert eine vom User formulierte Regel (z. B. 'keine Termine nach 18 Uhr'). " +
        "Args sind strukturiert — das LLM wandelt die natürliche Eingabe vorher in dieses Schema.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "text": { "type": "string", "description": "Original-Klartext der Regel" },
            "days_of_week": {
              "type": "array",
              "items": { "type": "string", "enum": ["monday","tuesday","wednesday","thursday","friday","saturday","sunday"] }
            },
            "time_start": { "type": ["string","null"], "description": "HH:mm — Beginn der Sperrzeit, null = ganzer Tag" },
            "time_end":   { "type": ["string","null"], "description": "HH:mm — Ende der Sperrzeit" },
            "hardness":   { "type": "string", "enum": ["hard","soft"] }
          },
          "required": ["text", "days_of_week", "hardness"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public AddRuleTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var days = DayOfWeekFlags.None;
        foreach (var dayEl in args.GetProperty("days_of_week").EnumerateArray())
        {
            days |= dayEl.GetString() switch
            {
                "monday" => DayOfWeekFlags.Monday,
                "tuesday" => DayOfWeekFlags.Tuesday,
                "wednesday" => DayOfWeekFlags.Wednesday,
                "thursday" => DayOfWeekFlags.Thursday,
                "friday" => DayOfWeekFlags.Friday,
                "saturday" => DayOfWeekFlags.Saturday,
                "sunday" => DayOfWeekFlags.Sunday,
                _ => DayOfWeekFlags.None,
            };
        }

        var request = new AddRuleRequest(
            Text: args.GetProperty("text").GetString()!,
            DaysOfWeek: days,
            TimeRangeStart: ParseTime(args, "time_start"),
            TimeRangeEnd: ParseTime(args, "time_end"),
            Hardness: Enum.Parse<RuleHardness>(args.GetProperty("hardness").GetString()!, ignoreCase: true));

        var response = await _mediator.Send(request, ct);
        return JsonSerializer.SerializeToElement(new
        {
            rule_id = response.Rule.Id,
            interpreted = new
            {
                text = response.Rule.Text,
                days_of_week = (int)response.Rule.DaysOfWeek,
                time_start = response.Rule.TimeRangeStart?.ToString("HH:mm"),
                time_end = response.Rule.TimeRangeEnd?.ToString("HH:mm"),
                hardness = response.Rule.Hardness.ToString().ToLowerInvariant(),
            },
        });
    }

    private static TimeOnly? ParseTime(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : TimeOnly.Parse(s);
    }
}
```

- [ ] **Step 8: DeleteRuleTool schreiben**

Datei `src/Backend/Features/Agent/Tools/DeleteRuleTool.cs`:

```csharp
using System.Text.Json;
using Mediator;
using NauAssist.Backend.Features.Rules.DeleteRule;

namespace NauAssist.Backend.Features.Agent.Tools;

public sealed class DeleteRuleTool : ITool
{
    public string Name => "delete_rule";
    public string Description => "Löscht eine Regel anhand ihrer ID (vom list_rules-Tool erhältlich).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": { "rule_id": { "type": "integer" } },
          "required": ["rule_id"]
        }
        """).RootElement;

    private readonly IMediator _mediator;

    public DeleteRuleTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.GetProperty("rule_id").GetInt64();
        var response = await _mediator.Send(new DeleteRuleRequest(id), ct);
        return JsonSerializer.SerializeToElement(new { deleted = response.Deleted });
    }
}
```

- [ ] **Step 9: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~ToolAdapterTests"
```

Expected: 6 Tests grün.

- [ ] **Step 10: Commit**

```bash
git add src/
git commit -m "Plan C Task 5: Sechs Mediator-Tool-Adapter (rules + calendar) inkl. FakeMediator und Tests"
```

---

## Task 6: PresentProposalsTool + SlotInfo

**Files:**
- Create: `src/Backend/Features/Agent/SlotInfo.cs`
- Create: `src/Backend/Features/Agent/Tools/PresentProposalsTool.cs`

Dieses Tool ist eine Sonderform: Es ruft keinen Mediator-Handler. Es dient nur als Schema-Träger für das LLM. Der AgentRunner fängt Tool-Calls mit Name `present_proposals` ab, extrahiert die Slots und löst ein `ProposalsEvent` aus.

- [ ] **Step 1: SlotInfo schreiben**

Datei `src/Backend/Features/Agent/SlotInfo.cs`:

```csharp
namespace NauAssist.Backend.Features.Agent;

public sealed record SlotInfo(
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Note);
```

- [ ] **Step 2: PresentProposalsTool schreiben**

Datei `src/Backend/Features/Agent/Tools/PresentProposalsTool.cs`:

```csharp
using System.Text.Json;

namespace NauAssist.Backend.Features.Agent.Tools;

/// <summary>
/// Side-Effect-Tool. Wird vom AgentRunner abgefangen — ExecuteAsync wirft, weil es niemals
/// regulär aufgerufen werden sollte.
/// Existiert nur, damit das LLM eine Tool-Definition mit JSON-Schema sieht.
/// </summary>
public sealed class PresentProposalsTool : ITool
{
    public const string ToolName = "present_proposals";

    public string Name => ToolName;
    public string Description =>
        "Veröffentlicht die finale Auswahl von 2–3 Slot-Vorschlägen an die Benutzeroberfläche. " +
        "Nach diesem Aufruf formuliert der Agent den begleitenden Antwort-Text in natürlicher Sprache.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "slots": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "properties": {
                  "start": { "type": "string", "format": "date-time" },
                  "end":   { "type": "string", "format": "date-time" },
                  "note":  { "type": ["string","null"], "description": "optionaler Kurz-Hinweis, z. B. 'Mi vormittag'" }
                },
                "required": ["start","end"]
              }
            }
          },
          "required": ["slots"]
        }
        """).RootElement;

    public Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        throw new InvalidOperationException(
            "PresentProposalsTool.ExecuteAsync darf nicht aufgerufen werden — der AgentRunner fängt diesen Tool-Call vorher ab.");
}
```

- [ ] **Step 3: Build verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
```

Expected: 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "Plan C Task 6: PresentProposalsTool (Side-Effect-Marker) + SlotInfo"
```

---

## Task 7: AgentStreamEvent-Hierarchie

**Files:**
- Create: `src/Backend/Features/Agent/AgentStreamEvent.cs`
- Create: `src/Backend.Tests/Features/Agent/AgentStreamEventTests.cs`

- [ ] **Step 1: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Agent/AgentStreamEventTests.cs`:

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentStreamEventTests
{
    [Fact]
    public void Events_CanBeMatchedViaSwitch()
    {
        AgentStreamEvent[] events = {
            new TokenEvent("hallo"),
            new ToolStartedEvent("lookup_free_slots"),
            new ToolFinishedEvent("lookup_free_slots", Ok: true),
            new ProposalsEvent(new[] { new SlotInfo(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "Test") }),
            new DoneEvent(),
            new ErrorEvent("oops", "corr-1"),
        };

        var kinds = events.Select(e => e switch
        {
            TokenEvent => "token",
            ToolStartedEvent => "tool_started",
            ToolFinishedEvent => "tool_finished",
            ProposalsEvent => "proposals",
            DoneEvent => "done",
            ErrorEvent => "error",
            _ => "unknown",
        }).ToArray();

        kinds.Should().BeEquivalentTo(new[] { "token", "tool_started", "tool_finished", "proposals", "done", "error" });
    }
}
```

- [ ] **Step 2: AgentStreamEvent schreiben**

Datei `src/Backend/Features/Agent/AgentStreamEvent.cs`:

```csharp
namespace NauAssist.Backend.Features.Agent;

public abstract record AgentStreamEvent;

public sealed record TokenEvent(string Text) : AgentStreamEvent;
public sealed record ToolStartedEvent(string Name) : AgentStreamEvent;
public sealed record ToolFinishedEvent(string Name, bool Ok) : AgentStreamEvent;
public sealed record ProposalsEvent(IReadOnlyList<SlotInfo> Slots) : AgentStreamEvent;
public sealed record DoneEvent() : AgentStreamEvent;
public sealed record ErrorEvent(string Message, string? CorrelationId = null) : AgentStreamEvent;
```

- [ ] **Step 3: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~AgentStreamEventTests"
```

Expected: 1 Test grün.

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "Plan C Task 7: AgentStreamEvent-Hierarchie (Token, ToolStarted, ToolFinished, Proposals, Done, Error)"
```

---

## Task 8: AgentRunner — Skelett (eine Iteration, nur Text)

**Files:**
- Create: `src/Backend/Features/Agent/AgentOptions.cs`
- Create: `src/Backend/Features/Agent/AgentRunner.cs`
- Create: `src/Backend.Tests/Features/Agent/AgentRunnerTests.cs`

Der Runner in seiner einfachsten Form: ruft das LLM einmal auf, gibt jeden Text-Chunk als `TokenEvent` durch, schließt mit `DoneEvent`. Noch keine Tool-Behandlung. Das kommt in Task 9.

- [ ] **Step 1: AgentOptions schreiben**

Datei `src/Backend/Features/Agent/AgentOptions.cs`:

```csharp
namespace NauAssist.Backend.Features.Agent;

public sealed class AgentOptions
{
    /// <summary>Maximalanzahl Tool-Iterationen pro User-Message, bevor der Runner abbricht.</summary>
    public int MaxToolIterations { get; set; } = 5;
}
```

- [ ] **Step 2: Failing-Test schreiben**

Datei `src/Backend.Tests/Features/Agent/AgentRunnerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Agent;

public sealed class AgentRunnerTests
{
    [Fact]
    public async Task Run_PureTextResponse_YieldsTokensAndDone()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(
            new TextDeltaChunk("Hallo"),
            new TextDeltaChunk(", "),
            new TextDeltaChunk("Welt"));

        var runner = new AgentRunner(llm, Array.Empty<ITool>(),
            Options.Create(new AgentOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentRunner>.Instance);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "Hi") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.OfType<TokenEvent>().Select(t => t.Text).Should().Equal("Hallo", ", ", "Welt");
        events.Last().Should().BeOfType<DoneEvent>();
    }
}
```

- [ ] **Step 3: AgentRunner schreiben (Skelett)**

Datei `src/Backend/Features/Agent/AgentRunner.cs`:

```csharp
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Agent;

public sealed class AgentRunner
{
    private readonly ILlmClient _llm;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentRunner> _logger;

    public AgentRunner(
        ILlmClient llm,
        IEnumerable<ITool> tools,
        IOptions<AgentOptions> options,
        ILogger<AgentRunner> logger)
    {
        _llm = llm;
        _tools = tools.ToDictionary(t => t.Name);
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<AgentStreamEvent> HandleAsync(
        IReadOnlyList<LlmMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var toolDefs = _tools.Values.Select(t => t.ToDefinition()).ToList();

        await foreach (var chunk in _llm.ChatStreamAsync(history, toolDefs, ct).WithCancellation(ct))
        {
            if (chunk is TextDeltaChunk text)
            {
                yield return new TokenEvent(text.Text);
            }
            // Tool-Calls werden in Task 9 ergänzt
        }

        yield return new DoneEvent();
    }
}
```

- [ ] **Step 4: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~AgentRunnerTests"
```

Expected: 1 Test grün.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan C Task 8: AgentRunner-Skelett (eine Iteration, nur Text-Tokens)"
```

---

## Task 9: AgentRunner — Tool-Loop + present_proposals + Iterations-Limit

**Files:**
- Modify: `src/Backend/Features/Agent/AgentRunner.cs`
- Modify: `src/Backend.Tests/Features/Agent/AgentRunnerTests.cs`

Erweiterung des Runners: Tool-Calls aus dem LLM-Stream einsammeln, ausführen (oder bei `present_proposals` als `ProposalsEvent` emittieren), Ergebnisse an die Konversation anhängen, und Stream neu starten. Maximal `MaxToolIterations` Durchgänge.

- [ ] **Step 1: Zusätzliche Tests anhängen**

In `src/Backend.Tests/Features/Agent/AgentRunnerTests.cs` ergänzen (die Klasse erweitern):

```csharp
    [Fact]
    public async Task Run_WhenLlmCallsTool_ExecutesAndFeedsResultBack()
    {
        var llm = new FakeLlmClient();
        // 1. Antwort: Tool-Call lookup_free_slots
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            Id: "call-1",
            Name: "echo",
            Arguments: System.Text.Json.JsonDocument.Parse("""{"value":"hi"}""").RootElement)));
        // 2. Antwort: finale Text-Antwort
        llm.QueueResponse(new TextDeltaChunk("fertig"));

        var echoTool = new EchoTool();
        var runner = new AgentRunner(llm, new[] { (ITool)echoTool },
            Options.Create(new AgentOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentRunner>.Instance);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "Hi") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.Should().Contain(e => e is ToolStartedEvent ts && ts.Name == "echo");
        events.Should().Contain(e => e is ToolFinishedEvent tf && tf.Name == "echo" && tf.Ok);
        events.OfType<TokenEvent>().Single().Text.Should().Be("fertig");
        events.Last().Should().BeOfType<DoneEvent>();
        echoTool.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Run_PresentProposalsTool_EmitsProposalsEvent_WithoutCallingTool()
    {
        var llm = new FakeLlmClient();
        // 1. Antwort: present_proposals mit zwei Slots
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            Id: "call-1",
            Name: "present_proposals",
            Arguments: System.Text.Json.JsonDocument.Parse("""
                {"slots":[
                    {"start":"2026-05-27T10:00:00+02:00","end":"2026-05-27T11:00:00+02:00","note":"Vormittag"},
                    {"start":"2026-05-27T14:00:00+02:00","end":"2026-05-27T15:00:00+02:00","note":"Nachmittag"}
                ]}
                """).RootElement)));
        // 2. Antwort: Begleittext
        llm.QueueResponse(new TextDeltaChunk("Zwei Slots für dich"));

        var present = new NauAssist.Backend.Features.Agent.Tools.PresentProposalsTool();
        var runner = new AgentRunner(llm, new[] { (ITool)present },
            Options.Create(new AgentOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentRunner>.Instance);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "Vorschläge?") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.OfType<ProposalsEvent>().Should().HaveCount(1);
        events.OfType<ProposalsEvent>().Single().Slots.Should().HaveCount(2);
        events.OfType<TokenEvent>().Single().Text.Should().Be("Zwei Slots für dich");
    }

    [Fact]
    public async Task Run_ExceedsIterationLimit_EmitsErrorEvent()
    {
        var llm = new FakeLlmClient();
        // 6 Iterationen Tool-Call (mehr als der Limit von 5)
        for (var i = 0; i < 6; i++)
        {
            llm.QueueResponse(new ToolCallChunk(new LlmToolCall(
                Id: $"call-{i}",
                Name: "echo",
                Arguments: System.Text.Json.JsonDocument.Parse("""{"value":"loop"}""").RootElement)));
        }

        var echoTool = new EchoTool();
        var runner = new AgentRunner(llm, new[] { (ITool)echoTool },
            Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentRunner>.Instance);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "loop") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.Last().Should().BeOfType<ErrorEvent>();
        echoTool.ExecutionCount.Should().Be(5); // Genau Limit ausgeschöpft
    }

    [Fact]
    public async Task Run_ToolThrows_EmitsToolFinishedNotOkAndContinues()
    {
        var llm = new FakeLlmClient();
        llm.QueueResponse(new ToolCallChunk(new LlmToolCall("c1", "boom", System.Text.Json.JsonDocument.Parse("{}").RootElement)));
        llm.QueueResponse(new TextDeltaChunk("hab den Fehler bemerkt"));

        var runner = new AgentRunner(llm, new[] { (ITool)new ThrowingTool() },
            Options.Create(new AgentOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentRunner>.Instance);

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in runner.HandleAsync(
            new List<LlmMessage> { new("user", "?") }, CancellationToken.None))
        {
            events.Add(ev);
        }

        events.Should().Contain(e => e is ToolFinishedEvent tf && tf.Name == "boom" && !tf.Ok);
        events.OfType<TokenEvent>().Single().Text.Should().Be("hab den Fehler bemerkt");
    }

    /// <summary>Test-Tool: gibt seine Args als JSON zurück.</summary>
    private sealed class EchoTool : ITool
    {
        public int ExecutionCount { get; private set; }
        public string Name => "echo";
        public string Description => "Echo-Test-Tool";
        public System.Text.Json.JsonElement ParameterSchema { get; } =
            System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
        public Task<System.Text.Json.JsonElement> ExecuteAsync(System.Text.Json.JsonElement args, CancellationToken ct)
        {
            ExecutionCount++;
            return Task.FromResult(args);
        }
    }

    /// <summary>Test-Tool: wirft immer eine Exception.</summary>
    private sealed class ThrowingTool : ITool
    {
        public string Name => "boom";
        public string Description => "Wirft.";
        public System.Text.Json.JsonElement ParameterSchema { get; } =
            System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement;
        public Task<System.Text.Json.JsonElement> ExecuteAsync(System.Text.Json.JsonElement args, CancellationToken ct) =>
            throw new InvalidOperationException("kaputt");
    }
```

- [ ] **Step 2: AgentRunner aktualisieren — komplette Implementierung**

`src/Backend/Features/Agent/AgentRunner.cs` komplett ersetzen:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Agent;

public sealed class AgentRunner
{
    private readonly ILlmClient _llm;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentRunner> _logger;

    public AgentRunner(
        ILlmClient llm,
        IEnumerable<ITool> tools,
        IOptions<AgentOptions> options,
        ILogger<AgentRunner> logger)
    {
        _llm = llm;
        _tools = tools.ToDictionary(t => t.Name);
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<AgentStreamEvent> HandleAsync(
        IReadOnlyList<LlmMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var toolDefs = _tools.Values.Select(t => t.ToDefinition()).ToList();
        var conversation = history.ToList();

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var pendingToolCalls = new List<LlmToolCall>();

            await foreach (var chunk in _llm.ChatStreamAsync(conversation, toolDefs, ct).WithCancellation(ct))
            {
                switch (chunk)
                {
                    case TextDeltaChunk text:
                        yield return new TokenEvent(text.Text);
                        break;
                    case ToolCallChunk toolCall:
                        pendingToolCalls.Add(toolCall.Call);
                        break;
                }
            }

            if (pendingToolCalls.Count == 0)
            {
                // Keine Tool-Calls — LLM ist fertig
                yield return new DoneEvent();
                yield break;
            }

            // Tool-Calls zur Konversation als assistant-Message anhängen (damit das LLM den Bezug behält)
            conversation.Add(new LlmMessage(
                Role: "assistant",
                Content: null,
                ToolCalls: pendingToolCalls));

            foreach (var call in pendingToolCalls)
            {
                if (call.Name == PresentProposalsTool.ToolName)
                {
                    var slots = ParseSlotsFromPresentProposalsArgs(call.Arguments);
                    yield return new ProposalsEvent(slots);
                    yield return new ToolFinishedEvent(call.Name, Ok: true);
                    conversation.Add(new LlmMessage(
                        Role: "tool",
                        Content: """{"ok":true}""",
                        ToolCallId: call.Id));
                    continue;
                }

                if (!_tools.TryGetValue(call.Name, out var tool))
                {
                    _logger.LogWarning("LLM hat ein unbekanntes Tool gerufen: {Name}", call.Name);
                    yield return new ToolStartedEvent(call.Name);
                    yield return new ToolFinishedEvent(call.Name, Ok: false);
                    conversation.Add(new LlmMessage(
                        Role: "tool",
                        Content: $$"""{"error":"unknown_tool","name":"{{call.Name}}"}""",
                        ToolCallId: call.Id));
                    continue;
                }

                yield return new ToolStartedEvent(call.Name);

                JsonElement result;
                bool ok;
                try
                {
                    result = await tool.ExecuteAsync(call.Arguments, ct);
                    ok = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tool {Name} hat geworfen.", call.Name);
                    var errObj = new { error = ex.GetType().Name, message = ex.Message };
                    result = JsonSerializer.SerializeToElement(errObj);
                    ok = false;
                }

                yield return new ToolFinishedEvent(call.Name, ok);
                conversation.Add(new LlmMessage(
                    Role: "tool",
                    Content: result.GetRawText(),
                    ToolCallId: call.Id));
            }
        }

        // Limit erreicht ohne finale Text-Antwort
        _logger.LogWarning("Tool-Loop-Limit ({Max}) erreicht ohne finale Text-Antwort.", _options.MaxToolIterations);
        yield return new ErrorEvent("Ich komme da gerade nicht weiter (Tool-Loop-Limit erreicht).");
    }

    private static IReadOnlyList<SlotInfo> ParseSlotsFromPresentProposalsArgs(JsonElement args)
    {
        var slots = new List<SlotInfo>();
        if (!args.TryGetProperty("slots", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return slots;
        }

        foreach (var s in arr.EnumerateArray())
        {
            var start = DateTimeOffset.Parse(s.GetProperty("start").GetString()!);
            var end = DateTimeOffset.Parse(s.GetProperty("end").GetString()!);
            string? note = null;
            if (s.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
            {
                note = noteEl.GetString();
            }
            slots.Add(new SlotInfo(start, end, note));
        }
        return slots;
    }
}
```

- [ ] **Step 3: Tests laufen lassen, PASS bestätigen**

Run:
```bash
dotnet test src/NauAssist.slnx --filter "FullyQualifiedName~AgentRunnerTests"
```

Expected: 5 Tests grün (1 aus Task 8 + 4 neue).

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "Plan C Task 9: AgentRunner mit Tool-Loop, present_proposals, Iterations-Limit, Tool-Fehler"
```

---

## Task 10: DI-Verkabelung + Plan-C-Abschluss

**Files:**
- Modify: `src/Backend/Program.cs`
- Modify: `src/Backend/appsettings.json`

- [ ] **Step 1: appsettings.json erweitern**

Komplette neue Version:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Persistence": {
    "DatabasePath": "./data/nauassist.db"
  },
  "Calendar": {
    "WorkingHoursStart": "09:00",
    "WorkingHoursEnd": "18:00",
    "DefaultDurationMinutes": 60,
    "SearchHorizonDays": 14,
    "GoogleCalendarId": "primary",
    "GoogleCredentialsPath": "./data/google-credentials.json"
  },
  "Ollama": {
    "Host": "http://localhost:11434",
    "Model": "qwen2.5:7b-instruct",
    "InitialTimeoutSeconds": 60,
    "TokenTimeoutSeconds": 30,
    "SystemPrompt": "Du bist NauAssist, ein persönlicher Kalender-Agent für Benedikt. Antworte präzise und auf Deutsch. Wenn der User eine Terminanfrage paste-t, rufe lookup_free_slots, wähle 2-3 passende Slots, rufe present_proposals damit, und formuliere danach eine kurze Antwort. Bestätigt der User einen Slot, rufe create_event. Bei Regel-Eingaben rufe add_rule mit strukturierten Args."
  },
  "Agent": {
    "MaxToolIterations": 5
  }
}
```

- [ ] **Step 2: Program.cs erweitern**

Im `src/Backend/Program.cs` die DI-Registrierungen vor `builder.Services.AddMediator(...)` erweitern:

```csharp
// LLM
builder.Services.Configure<NauAssist.Backend.Features.Infrastructure.Llm.Ollama.OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<NauAssist.Backend.Features.Infrastructure.Llm.ILlmClient, NauAssist.Backend.Features.Infrastructure.Llm.Ollama.OllamaLlmClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NauAssist.Backend.Features.Infrastructure.Llm.Ollama.OllamaOptions>>().Value;
    client.BaseAddress = new Uri(opts.Host);
});

// Agent
builder.Services.Configure<NauAssist.Backend.Features.Agent.AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.LookupFreeSlotsTool>();
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.CreateEventTool>();
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.GetCalendarRangeTool>();
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.ListRulesTool>();
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.AddRuleTool>();
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.DeleteRuleTool>();
builder.Services.AddSingleton<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.PresentProposalsTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.AgentRunner>();
```

**Wichtig:** Die ITool-Registrierungen müssen `AddSingleton<ITool, ConcreteType>` sein, damit `IEnumerable<ITool>` in den AgentRunner-Konstruktor injectet wird.

Außerdem: weil ITool-Konstruktoren `IMediator` brauchen (außer PresentProposals), und IMediator scoped ist, müssen wir entweder die Tools auch als Scoped registrieren ODER (besser) im AgentRunner über einen IServiceScopeFactory arbeiten. Sicherheitshalber: alle Tools `AddScoped` statt Singleton:

```csharp
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.LookupFreeSlotsTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.CreateEventTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.GetCalendarRangeTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.ListRulesTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.AddRuleTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.DeleteRuleTool>();
builder.Services.AddScoped<NauAssist.Backend.Features.Agent.ITool, NauAssist.Backend.Features.Agent.Tools.PresentProposalsTool>();
```

(AddRunner ist sowieso Scoped.)

- [ ] **Step 3: Build und volle Test-Suite verifizieren**

Run:
```bash
dotnet build src/NauAssist.slnx
dotnet test src/NauAssist.slnx
```

Expected: Build sauber, alle Tests grün (Plan A 30 + Plan B 26 + Plan C neu: FakeLlmClient 4 + AgentStreamEvent 1 + ToolAdapter 6 + AgentRunner 5 = 16; insgesamt 72).

- [ ] **Step 4: App-Start verifizieren (ohne Ollama-Verbindungstest)**

Run:
```bash
rm -rf src/Backend/data
dotnet run --project src/Backend --no-build &
APP_PID=$!
sleep 4
curl -s http://localhost:5182/health
kill $APP_PID 2>/dev/null
wait $APP_PID 2>/dev/null
rm -rf src/Backend/data
```

Expected: `ok` als Antwort, App startet trotz fehlender Ollama-Verbindung (der LLM-Client wird erst bei Bedarf instantiiert).

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "Plan C Task 10: DI-Verkabelung für LLM, Tools, AgentRunner + appsettings"
```

---

## Plan-C-Abschluss

Nach Task 10 läuft:
- ✅ `ILlmClient`-Abstraktion mit `FakeLlmClient` (deterministisch) und `OllamaLlmClient` (HTTP-Stream)
- ✅ Sieben Tools registriert, alle in DI
- ✅ `AgentRunner` orchestriert Konversationen mit Tool-Loop, Iterations-Limit, Side-Effect-Behandlung für `present_proposals`
- ✅ End-to-End-Tests über `FakeLlmClient` deterministisch reproduzierbar

**Was als Nächstes kommt (Plan D — Chat-Surface & SSE):**
- Migration `0003_messages.sql` (Tabellen `messages` + `audit_log`)
- `MessageRepository` für Chat-History-Persistenz
- `SendMessageRequest` als `IStreamRequest<SseEvent>` + Handler
- SSE-Event-Writer (Mapping `AgentStreamEvent` → `SseEvent`)
- `POST /api/chat` mit `Content-Type: text/event-stream`
- `GET /api/chat/history`
- Audit-Log-Wiring in `create_event`-, `add_rule`-, `delete_rule`-Handlern

Damit ist das MVP-Backend dann komplett. Plan E liefert das React-Frontend dazu.
