# Plan D — Chat-Surface & SSE — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Den `AgentRunner` aus Plan C über einen HTTP-Endpoint zugänglich machen. `POST /api/chat` nimmt eine User-Nachricht entgegen und streamt die Agent-Antwort als **Server-Sent Events** (`text/event-stream`). `GET /api/chat/history` liefert die letzten 50 Nachrichten als JSON. Chat-Verlauf und Audit-Spuren werden in SQLite persistiert.

**Architecture:** Ein neuer `SendMessageRequest` als `IStreamCommand<SseEvent>` (Mediator-3-Streaming) orchestriert: User-Message persistieren → History laden (letzte 15) → `AgentRunner.HandleAsync` aufrufen → `AgentStreamEvent`s in `SseEvent`s mappen → bei `DoneEvent` die akkumulierte Agent-Antwort persistieren und `SseEvent.Done(messageId)` yielden. Der Endpoint schreibt die `SseEvent`s zeilen-orientiert (`event: ...\ndata: ...\n\n`) in den Response-Body. Audit-Schreiben passiert **in den Handlern** der Außenwirkungen (`CreateEvent`, `AddRule`, `DeleteRule`) — nicht in den Tools.

**Tech Stack:** .NET 10 · ASP.NET Core Minimal API · `IAsyncEnumerable<T>` · Mediator 3 Streaming (`IStreamCommand`) · SQLite + Dapper · `System.Text.Json`.

**Bezug zur Spec:** `docs/superpowers/specs/2026-05-19-kalender-agent-mvp-design.md`, Abschnitte 6.1 (Chat Surface), 6.6 (Persistence), 6.7 (Audit Log), 7 (Datenfluss), 8 (Fehlerbehandlung), 9 (SSE-Integration-Tests).

**Was am Ende dieses Plans steht:**
- Migration `0003` mit Tabellen `messages` und `audit_log`
- `MessageRepository` und `AuditLogRepository`
- `SseEvent`-Hierarchie + Writer-Helper für SSE-Wire-Format
- `SendMessageRequest` als Mediator-Streaming-Handler — orchestriert User-Persist + AgentRunner + Mapping + Persist
- `GetChatHistoryRequest` + Handler
- `ChatEndpoints` (`POST /api/chat`, `GET /api/chat/history`)
- Audit-Wiring in den drei Handlern mit Außenwirkung
- Integrations-Test gegen `ChatEndpoint` via `TestAppFactory` + `FakeLlmClient` + `FakeCalendarProvider`
- Backend-MVP komplett: `curl -N -d '{...}'  http://localhost:5000/api/chat` produziert echtes Streaming, Termine landen im Kalender, alles wird auditiert.

---

## Datei-Übersicht (für diesen Plan)

**Neu anzulegen:**

| Pfad | Verantwortung |
|---|---|
| `src/Backend/Features/Infrastructure/Persistence/Migrations/0003_chat_audit.sql` | Tabellen `messages` + `audit_log` |
| `src/Backend/Features/Chat/Message.cs` | Message-Record + `MessageRole`-Enum |
| `src/Backend/Features/Chat/MessageRepository.cs` | Append, GetRecent, MarkIncomplete |
| `src/Backend/Features/Infrastructure/Audit/AuditEntry.cs` | Audit-DTO |
| `src/Backend/Features/Infrastructure/Audit/AuditLogRepository.cs` | Append-only Audit-Writer |
| `src/Backend/Features/Chat/SseEvent.cs` | SSE-Event-Hierarchie (Token, ToolStarted, …) |
| `src/Backend/Features/Chat/SseWriter.cs` | Schreibt SSE-Wire-Format in einen `Stream` |
| `src/Backend/Features/Chat/SendMessage/SendMessageRequest.cs` | `IStreamCommand<SseEvent>` |
| `src/Backend/Features/Chat/SendMessage/SendMessageHandler.cs` | Streaming-Handler-Orchestrierung |
| `src/Backend/Features/Chat/ChatHistory/GetChatHistoryRequest.cs` | `IRequest<...>` |
| `src/Backend/Features/Chat/ChatHistory/GetChatHistoryHandler.cs` | Handler |
| `src/Backend/Endpoints/ChatEndpoints.cs` | `POST /api/chat`, `GET /api/chat/history` |
| `src/Backend.Tests/Features/Chat/MessageRepositoryTests.cs` | CRUD-Tests |
| `src/Backend.Tests/Features/Infrastructure/Audit/AuditLogRepositoryTests.cs` | Append-Tests |
| `src/Backend.Tests/Features/Chat/SseWriterTests.cs` | Wire-Format-Tests |
| `src/Backend.Tests/Features/Chat/SendMessageHandlerTests.cs` | Streaming-Handler-Tests gegen `FakeLlmClient` |
| `src/Backend.Tests/Endpoints/ChatEndpointTests.cs` | End-to-End gegen `TestAppFactory` mit SSE-Consumer |
| `src/Backend.Tests/Helpers/SseTestConsumer.cs` | Hilfsmittel: parst SSE-Response-Body in `List<(eventName, jsonData)>` |

**Zu modifizieren:**

| Pfad | Änderung |
|---|---|
| `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs` | Audit-Eintrag schreiben |
| `src/Backend/Features/Rules/AddRule/AddRuleHandler.cs` | Audit-Eintrag schreiben |
| `src/Backend/Features/Rules/DeleteRule/DeleteRuleHandler.cs` | Audit-Eintrag schreiben |
| `src/Backend/Program.cs` | DI-Registrierungen + `MapChatEndpoints()` |
| `src/Backend/appsettings.json` | (ggf.) `Chat:HistoryWindow` Default 15 |

---

## Task 1: Migration 0003 — messages + audit_log

**Files:**
- Create: `src/Backend/Features/Infrastructure/Persistence/Migrations/0003_chat_audit.sql`

Schema gemäß Spec 6.6.

- [ ] **Step 1: SQL-Datei anlegen**

```sql
CREATE TABLE messages (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      TEXT NOT NULL,
    role            TEXT NOT NULL,           -- 'user' | 'assistant'
    content         TEXT NOT NULL,
    proposals_json  TEXT NULL,
    incomplete      INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL
);

CREATE INDEX idx_messages_session_created ON messages(session_id, created_at);

CREATE TABLE audit_log (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    triggering_message_id   INTEGER NULL,
    tool_name               TEXT NOT NULL,
    tool_args_json          TEXT NOT NULL,
    result_json             TEXT NOT NULL,
    provider_event_id       TEXT NULL,
    created_at              TEXT NOT NULL,
    FOREIGN KEY (triggering_message_id) REFERENCES messages(id)
);

CREATE INDEX idx_audit_created ON audit_log(created_at);
```

- [ ] **Step 2: Sicherstellen, dass die Datei als Embedded Resource ausgeliefert wird**

Die `*.sql`-Dateien sind in `Backend.csproj` bereits per `<EmbeddedResource Include="Features/Infrastructure/Persistence/Migrations/*.sql" />` eingebunden — neue Datei wird automatisch mitgenommen. Verifizieren mit:
```bash
dotnet build src/Backend -nologo -v quiet 2>&1 | tail -5
```

- [ ] **Step 3: Smoke-Test im `DbInitializerTests`**

`DbInitializerTests` existiert bereits. Migration 0003 muss in `appliedVersions` landen, wenn `Initialize()` lief. Neuer Test:

```csharp
[Fact]
public void Initialize_AppliesMigration0003_CreatesMessagesAndAuditLog()
{
    using var temp = new TempSqliteDb();
    var initializer = new DbInitializer(temp.Db, new NullLogger<DbInitializer>());

    initializer.Initialize();

    using var conn = temp.Db.OpenConnection();
    var versions = conn.Query<string>("SELECT version FROM schema_version;").ToList();
    versions.Should().Contain("0003");

    var tables = conn.Query<string>(
        "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('messages','audit_log');").ToList();
    tables.Should().Contain("messages").And.Contain("audit_log");
}
```

- [ ] **Step 4: Tests laufen lassen**

```bash
dotnet test src/Backend.Tests --filter "FullyQualifiedName~DbInitializerTests" -nologo --logger "console;verbosity=minimal"
```

- [ ] **Step 5: Commit**

```
Plan D Task 1: Migration 0003 — messages + audit_log Tabellen
```

---

## Task 2: Message-Domain + MessageRepository

**Files:**
- Create: `src/Backend/Features/Chat/Message.cs`
- Create: `src/Backend/Features/Chat/MessageRepository.cs`
- Create: `src/Backend.Tests/Features/Chat/MessageRepositoryTests.cs`

- [ ] **Step 1: `Message.cs` anlegen**

```csharp
namespace NauAssist.Backend.Features.Chat;

public enum MessageRole
{
    User,
    Assistant,
}

public sealed record Message(
    long Id,
    string SessionId,
    MessageRole Role,
    string Content,
    string? ProposalsJson,
    bool Incomplete,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 2: TDD — `MessageRepositoryTests` schreiben (rot)**

```csharp
using FluentAssertions;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Tests.Helpers;
using Xunit;

namespace NauAssist.Backend.Tests.Features.Chat;

public sealed class MessageRepositoryTests
{
    [Fact]
    public async Task AddAsync_PersistsRow_ReturnsAssignedId()
    {
        using var temp = new TempSqliteDb();
        new DbInitializer(temp.Db, new NullLogger<DbInitializer>()).Initialize();
        var repo = new MessageRepository(temp.Db);

        var saved = await repo.AddAsync(
            new Message(0, "default", MessageRole.User, "hallo", null, false,
                DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        saved.Id.Should().BeGreaterThan(0);
        saved.Content.Should().Be("hallo");
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirstUpToTake()
    {
        using var temp = new TempSqliteDb();
        new DbInitializer(temp.Db, new NullLogger<DbInitializer>()).Initialize();
        var repo = new MessageRepository(temp.Db);

        for (var i = 0; i < 5; i++)
        {
            await repo.AddAsync(
                new Message(0, "default", MessageRole.User, $"msg {i}", null, false,
                    DateTimeOffset.Parse("2026-05-19T10:00:00Z").AddMinutes(i)),
                CancellationToken.None);
        }

        var recent = await repo.GetRecentAsync("default", take: 3, CancellationToken.None);

        recent.Should().HaveCount(3);
        recent.Select(m => m.Content).Should().Equal("msg 4", "msg 3", "msg 2");
    }

    [Fact]
    public async Task MarkIncompleteAsync_FlipsFlag()
    {
        using var temp = new TempSqliteDb();
        new DbInitializer(temp.Db, new NullLogger<DbInitializer>()).Initialize();
        var repo = new MessageRepository(temp.Db);

        var saved = await repo.AddAsync(
            new Message(0, "default", MessageRole.Assistant, "halb", null, false,
                DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
            CancellationToken.None);

        await repo.MarkIncompleteAsync(saved.Id, CancellationToken.None);

        var recent = await repo.GetRecentAsync("default", take: 1, CancellationToken.None);
        recent[0].Incomplete.Should().BeTrue();
    }
}
```

- [ ] **Step 3: `MessageRepository` implementieren (grün)**

```csharp
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Chat;

public sealed class MessageRepository
{
    private readonly AppDb _db;

    public MessageRepository(AppDb db) { _db = db; }

    public async Task<Message> AddAsync(Message msg, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO messages(session_id, role, content, proposals_json, incomplete, created_at)
            VALUES(@SessionId, @Role, @Content, @ProposalsJson, @Incomplete, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                msg.SessionId,
                Role = msg.Role.ToString().ToLowerInvariant(),
                msg.Content,
                msg.ProposalsJson,
                Incomplete = msg.Incomplete ? 1 : 0,
                CreatedAt = msg.CreatedAt.ToString("O"),
            });
        return msg with { Id = id };
    }

    public async Task<IReadOnlyList<Message>> GetRecentAsync(string sessionId, int take, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<MessageRow>(
            """
            SELECT id, session_id, role, content, proposals_json, incomplete, created_at
            FROM messages
            WHERE session_id = @sessionId
            ORDER BY id DESC
            LIMIT @take;
            """,
            new { sessionId, take });
        return rows.Select(MapToDomain).ToList();
    }

    public async Task MarkIncompleteAsync(long id, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(
            "UPDATE messages SET incomplete = 1 WHERE id = @id;",
            new { id });
    }

    private static Message MapToDomain(MessageRow r) => new(
        Id: r.id,
        SessionId: r.session_id,
        Role: Enum.Parse<MessageRole>(r.role, ignoreCase: true),
        Content: r.content,
        ProposalsJson: r.proposals_json,
        Incomplete: r.incomplete != 0,
        CreatedAt: DateTimeOffset.Parse(r.created_at));

    private sealed record MessageRow(
        long id, string session_id, string role, string content,
        string? proposals_json, long incomplete, string created_at);
}
```

> **Hinweis:** `incomplete` als `long`, weil SQLite `INTEGER` als Int64 zurückgibt (siehe Plan A Task 4: gleiches Pattern wie bei `RuleRow.days_of_week`).

- [ ] **Step 4: Tests laufen lassen**

```bash
dotnet test src/Backend.Tests --filter "FullyQualifiedName~MessageRepositoryTests" -nologo --logger "console;verbosity=minimal"
```

- [ ] **Step 5: Commit**

```
Plan D Task 2: Message-Domain + MessageRepository
```

---

## Task 3: AuditLogRepository

**Files:**
- Create: `src/Backend/Features/Infrastructure/Audit/AuditEntry.cs`
- Create: `src/Backend/Features/Infrastructure/Audit/AuditLogRepository.cs`
- Create: `src/Backend.Tests/Features/Infrastructure/Audit/AuditLogRepositoryTests.cs`

- [ ] **Step 1: `AuditEntry.cs`**

```csharp
namespace NauAssist.Backend.Features.Infrastructure.Audit;

public sealed record AuditEntry(
    long Id,
    long? TriggeringMessageId,
    string ToolName,
    string ToolArgsJson,
    string ResultJson,
    string? ProviderEventId,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 2: TDD — `AuditLogRepositoryTests`**

```csharp
[Fact]
public async Task AppendAsync_PersistsRow_ReturnsId()
{
    using var temp = new TempSqliteDb();
    new DbInitializer(temp.Db, new NullLogger<DbInitializer>()).Initialize();
    var repo = new AuditLogRepository(temp.Db);

    var saved = await repo.AppendAsync(new AuditEntry(
        Id: 0,
        TriggeringMessageId: null,
        ToolName: "create_event",
        ToolArgsJson: """{"title":"X"}""",
        ResultJson: """{"id":"evt1"}""",
        ProviderEventId: "evt1",
        CreatedAt: DateTimeOffset.Parse("2026-05-19T10:00:00Z")),
        CancellationToken.None);

    saved.Id.Should().BeGreaterThan(0);
}

[Fact]
public async Task GetByMessageIdAsync_ReturnsAllEntriesForMessage()
{
    // ... append 3 entries, 2 mit messageId=42, einer mit null → 2 zurück
}
```

- [ ] **Step 3: `AuditLogRepository` implementieren**

```csharp
using Dapper;
using NauAssist.Backend.Features.Infrastructure.Persistence;

namespace NauAssist.Backend.Features.Infrastructure.Audit;

public sealed class AuditLogRepository
{
    private readonly AppDb _db;
    public AuditLogRepository(AppDb db) { _db = db; }

    public async Task<AuditEntry> AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO audit_log(triggering_message_id, tool_name, tool_args_json, result_json, provider_event_id, created_at)
            VALUES(@TriggeringMessageId, @ToolName, @ToolArgsJson, @ResultJson, @ProviderEventId, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                entry.TriggeringMessageId,
                entry.ToolName,
                entry.ToolArgsJson,
                entry.ResultJson,
                entry.ProviderEventId,
                CreatedAt = entry.CreatedAt.ToString("O"),
            });
        return entry with { Id = id };
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByMessageIdAsync(long messageId, CancellationToken ct)
    {
        using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<AuditRow>(
            """
            SELECT id, triggering_message_id, tool_name, tool_args_json, result_json, provider_event_id, created_at
            FROM audit_log
            WHERE triggering_message_id = @messageId
            ORDER BY id;
            """,
            new { messageId });
        return rows.Select(MapToDomain).ToList();
    }

    private static AuditEntry MapToDomain(AuditRow r) => new(
        r.id, r.triggering_message_id, r.tool_name, r.tool_args_json,
        r.result_json, r.provider_event_id, DateTimeOffset.Parse(r.created_at));

    private sealed record AuditRow(
        long id, long? triggering_message_id, string tool_name,
        string tool_args_json, string result_json, string? provider_event_id, string created_at);
}
```

- [ ] **Step 4: Tests grün, Commit**

```
Plan D Task 3: AuditLogRepository
```

---

## Task 4: SseEvent + SseWriter

**Files:**
- Create: `src/Backend/Features/Chat/SseEvent.cs`
- Create: `src/Backend/Features/Chat/SseWriter.cs`
- Create: `src/Backend.Tests/Features/Chat/SseWriterTests.cs`

SSE-Wire-Format ist trivial, aber wir kapseln es trotzdem, weil das Mapping zwischen `SseEvent` und Bytes an einer Stelle leben soll.

- [ ] **Step 1: `SseEvent.cs`**

```csharp
using NauAssist.Backend.Features.Agent;

namespace NauAssist.Backend.Features.Chat;

/// <summary>Eine Nachricht im SSE-Stream zwischen Server und React-UI.</summary>
public abstract record SseEvent(string EventName);

public sealed record SseToken(string Text) : SseEvent("token");
public sealed record SseToolStarted(string Name) : SseEvent("tool_started");
public sealed record SseToolFinished(string Name, bool Ok) : SseEvent("tool_finished");
public sealed record SseProposals(IReadOnlyList<SlotInfo> Slots) : SseEvent("proposals");
public sealed record SseDone(long MessageId) : SseEvent("done");
public sealed record SseError(string Message, string? CorrelationId = null) : SseEvent("error");
```

- [ ] **Step 2: TDD — `SseWriterTests`**

```csharp
[Fact]
public async Task WriteAsync_TokenEvent_ProducesExactWireFormat()
{
    using var memory = new MemoryStream();
    var writer = new SseWriter(memory);

    await writer.WriteAsync(new SseToken("hallo"), CancellationToken.None);

    var s = Encoding.UTF8.GetString(memory.ToArray());
    s.Should().Be("event: token\ndata: {\"text\":\"hallo\"}\n\n");
}

[Fact]
public async Task WriteAsync_DoneEvent_IncludesMessageId()
{
    using var memory = new MemoryStream();
    var writer = new SseWriter(memory);

    await writer.WriteAsync(new SseDone(42), CancellationToken.None);

    var s = Encoding.UTF8.GetString(memory.ToArray());
    s.Should().Contain("event: done\n");
    s.Should().Contain("\"messageId\":42");
}

[Fact]
public async Task WriteAsync_ProposalsEvent_SerializesSlots()
{
    using var memory = new MemoryStream();
    var writer = new SseWriter(memory);
    var slots = new[]
    {
        new SlotInfo(
            DateTimeOffset.Parse("2026-05-20T09:00:00Z"),
            DateTimeOffset.Parse("2026-05-20T10:00:00Z"),
            null),
    };

    await writer.WriteAsync(new SseProposals(slots), CancellationToken.None);

    var s = Encoding.UTF8.GetString(memory.ToArray());
    s.Should().StartWith("event: proposals\ndata: [");
    s.Should().Contain("2026-05-20T09:00:00");
}
```

- [ ] **Step 3: `SseWriter` implementieren**

```csharp
using System.Text;
using System.Text.Json;

namespace NauAssist.Backend.Features.Chat;

public sealed class SseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Stream _stream;

    public SseWriter(Stream stream) { _stream = stream; }

    public async Task WriteAsync(SseEvent ev, CancellationToken ct)
    {
        var dataJson = ev switch
        {
            SseToken t          => JsonSerializer.Serialize(new { text = t.Text }, JsonOptions),
            SseToolStarted ts   => JsonSerializer.Serialize(new { name = ts.Name }, JsonOptions),
            SseToolFinished tf  => JsonSerializer.Serialize(new { name = tf.Name, ok = tf.Ok }, JsonOptions),
            SseProposals p      => JsonSerializer.Serialize(p.Slots, JsonOptions),
            SseDone d           => JsonSerializer.Serialize(new { messageId = d.MessageId }, JsonOptions),
            SseError e          => JsonSerializer.Serialize(new { message = e.Message, correlationId = e.CorrelationId }, JsonOptions),
            _ => throw new InvalidOperationException($"Unbekannter SseEvent-Typ: {ev.GetType().Name}"),
        };

        var frame = $"event: {ev.EventName}\ndata: {dataJson}\n\n";
        var bytes = Encoding.UTF8.GetBytes(frame);
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }
}
```

- [ ] **Step 4: Tests grün, Commit**

```
Plan D Task 4: SseEvent-Hierarchie + SseWriter
```

---

## Task 5: SendMessageRequest + Handler (Streaming-Orchestrierung)

**Files:**
- Create: `src/Backend/Features/Chat/SendMessage/SendMessageRequest.cs`
- Create: `src/Backend/Features/Chat/SendMessage/SendMessageHandler.cs`
- Create: `src/Backend.Tests/Features/Chat/SendMessageHandlerTests.cs`

Das Herzstück von Plan D. Mediator-3-Streaming nutzt `IStreamCommand<TResponse>`.

- [ ] **Step 1: Request anlegen**

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Chat.SendMessage;

public sealed record SendMessageRequest(string SessionId, string UserText) : IStreamCommand<SseEvent>;
```

- [ ] **Step 2: TDD — `SendMessageHandlerTests` (Skelett)**

Der Test setzt die DI selbst zusammen (kein WebApplicationFactory). Tools sind via `FakeMediator` gestubbt, `ILlmClient` ist `FakeLlmClient`, `MessageRepository` läuft gegen `TempSqliteDb`.

```csharp
public sealed class SendMessageHandlerTests
{
    [Fact]
    public async Task Handle_HappyPath_PersistsUserAndAssistant_YieldsDone()
    {
        using var temp = new TempSqliteDb();
        new DbInitializer(temp.Db, new NullLogger<DbInitializer>()).Initialize();
        var messages = new MessageRepository(temp.Db);

        var fakeLlm = new FakeLlmClient();
        fakeLlm.EnqueueResponse(new[]
        {
            (LlmStreamChunk)new TextDeltaChunk("Hallo "),
            new TextDeltaChunk("Benedikt"),
        });

        var runner = new AgentRunner(
            fakeLlm,
            tools: Array.Empty<ITool>(),
            options: Options.Create(new AgentOptions { MaxToolIterations = 5 }),
            logger: new NullLogger<AgentRunner>());

        var handler = new SendMessageHandler(
            messages,
            runner,
            clock: () => DateTimeOffset.Parse("2026-05-19T10:00:00Z"),
            logger: new NullLogger<SendMessageHandler>());

        var events = new List<SseEvent>();
        await foreach (var ev in handler.Handle(
            new SendMessageRequest("default", "Hi"), CancellationToken.None))
        {
            events.Add(ev);
        }

        events.Should().NotBeEmpty();
        events.OfType<SseToken>().Select(t => t.Text).Should().Equal("Hallo ", "Benedikt");
        events.Last().Should().BeOfType<SseDone>();

        var persisted = await messages.GetRecentAsync("default", 10, CancellationToken.None);
        persisted.Should().HaveCount(2);
        persisted.Single(m => m.Role == MessageRole.User).Content.Should().Be("Hi");
        persisted.Single(m => m.Role == MessageRole.Assistant).Content.Should().Be("Hallo Benedikt");
    }

    [Fact]
    public async Task Handle_ProposalsEvent_PersistsProposalsJsonOnAssistantMessage()
    {
        // ... script LLM zu present_proposals → text → done
        // Assert: assistant message hat ProposalsJson != null
    }

    [Fact]
    public async Task Handle_ErrorFromAgentRunner_PersistsPartialAsIncomplete()
    {
        // ... script LLM zu nur Text-Delta dann Cancellation
        // Assert: assistant.Incomplete == true
    }
}
```

- [ ] **Step 3: `SendMessageHandler` implementieren**

Orchestrierung:
1. User-Message in DB
2. History laden (letzte 15 inkl. der eben gespeicherten User-Message)
3. Zur `LlmMessage`-Liste mappen
4. `AgentRunner.HandleAsync` durchlaufen
5. Text-Deltas akkumulieren
6. Bei `ProposalsEvent` → JSON für Persistierung merken, `SseProposals` weitergeben
7. Bei `DoneEvent` → Assistant-Message persistieren, `SseDone(messageId)` yielden
8. Bei `ErrorEvent` → Teil-Antwort als `incomplete=true` persistieren, `SseError` yielden

```csharp
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Infrastructure.Llm;

namespace NauAssist.Backend.Features.Chat.SendMessage;

public sealed class SendMessageHandler : IStreamCommandHandler<SendMessageRequest, SseEvent>
{
    private const int HistoryWindow = 15;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly MessageRepository _messages;
    private readonly AgentRunner _runner;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        MessageRepository messages,
        AgentRunner runner,
        Func<DateTimeOffset> clock,
        ILogger<SendMessageHandler> logger)
    {
        _messages = messages;
        _runner = runner;
        _clock = clock;
        _logger = logger;
    }

    public async IAsyncEnumerable<SseEvent> Handle(
        SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var userMessage = await _messages.AddAsync(
            new Message(0, request.SessionId, MessageRole.User, request.UserText, null, false, _clock()),
            ct);

        var recent = await _messages.GetRecentAsync(request.SessionId, HistoryWindow, ct);
        var ordered = recent.Reverse().ToList();
        var history = ordered.Select(MapToLlmMessage).ToList();

        var accumulated = new StringBuilder();
        IReadOnlyList<SlotInfo>? lastProposals = null;
        var hadError = false;
        long persistedId = 0;

        await foreach (var ev in _runner.HandleAsync(history, ct).WithCancellation(ct))
        {
            switch (ev)
            {
                case TokenEvent t:
                    accumulated.Append(t.Text);
                    yield return new SseToken(t.Text);
                    break;
                case ToolStartedEvent ts:
                    yield return new SseToolStarted(ts.Name);
                    break;
                case ToolFinishedEvent tf:
                    yield return new SseToolFinished(tf.Name, tf.Ok);
                    break;
                case ProposalsEvent p:
                    lastProposals = p.Slots;
                    yield return new SseProposals(p.Slots);
                    break;
                case DoneEvent:
                    persistedId = await PersistAssistantAsync(request.SessionId, accumulated.ToString(),
                        lastProposals, incomplete: false, ct);
                    yield return new SseDone(persistedId);
                    yield break;
                case ErrorEvent e:
                    persistedId = await PersistAssistantAsync(request.SessionId, accumulated.ToString(),
                        lastProposals, incomplete: true, ct);
                    hadError = true;
                    yield return new SseError(e.Message, e.CorrelationId);
                    yield break;
            }
        }

        if (!hadError && persistedId == 0)
        {
            // Stream endete ohne Done/Error (z.B. Cancellation) — Teil-Antwort retten
            persistedId = await PersistAssistantAsync(request.SessionId, accumulated.ToString(),
                lastProposals, incomplete: true, ct);
        }
    }

    private async Task<long> PersistAssistantAsync(
        string sessionId, string content, IReadOnlyList<SlotInfo>? proposals, bool incomplete, CancellationToken ct)
    {
        var proposalsJson = proposals is null
            ? null
            : JsonSerializer.Serialize(proposals, JsonOptions);

        var saved = await _messages.AddAsync(
            new Message(0, sessionId, MessageRole.Assistant, content, proposalsJson, incomplete, _clock()),
            ct);
        return saved.Id;
    }

    private static LlmMessage MapToLlmMessage(Message m) => m.Role switch
    {
        MessageRole.User      => new LlmMessage("user", m.Content),
        MessageRole.Assistant => new LlmMessage("assistant", m.Content),
        _ => throw new InvalidOperationException($"Unbekannte MessageRole {m.Role}"),
    };
}
```

> **Hinweis:** `[EnumeratorCancellation]` ist essenziell für `async IAsyncEnumerable<T>` — sonst frisst die Method den `ct` nicht aus dem `WithCancellation`.

- [ ] **Step 4: Tests grün, Commit**

```
Plan D Task 5: SendMessageRequest + Streaming-Handler
```

---

## Task 6: GetChatHistory

**Files:**
- Create: `src/Backend/Features/Chat/ChatHistory/GetChatHistoryRequest.cs`
- Create: `src/Backend/Features/Chat/ChatHistory/GetChatHistoryHandler.cs`
- Create: `src/Backend.Tests/Features/Chat/GetChatHistoryHandlerTests.cs`

Simpler Read-Handler.

- [ ] **Step 1: Request + Response**

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Chat.ChatHistory;

public sealed record GetChatHistoryRequest(string SessionId, int Take = 50) : IRequest<GetChatHistoryResponse>;
public sealed record GetChatHistoryResponse(IReadOnlyList<Message> Messages);
```

- [ ] **Step 2: TDD — Test**

```csharp
[Fact]
public async Task Handle_ReturnsRecentMessagesOldestFirst()
{
    using var temp = new TempSqliteDb();
    new DbInitializer(temp.Db, new NullLogger<DbInitializer>()).Initialize();
    var repo = new MessageRepository(temp.Db);

    for (var i = 0; i < 3; i++)
    {
        await repo.AddAsync(
            new Message(0, "default", MessageRole.User, $"m{i}", null, false,
                DateTimeOffset.Parse("2026-05-19T10:00:00Z").AddMinutes(i)),
            CancellationToken.None);
    }

    var handler = new GetChatHistoryHandler(repo);
    var response = await handler.Handle(new GetChatHistoryRequest("default"), CancellationToken.None);

    response.Messages.Select(m => m.Content).Should().Equal("m0", "m1", "m2");
}
```

- [ ] **Step 3: Handler**

```csharp
using Mediator;

namespace NauAssist.Backend.Features.Chat.ChatHistory;

public sealed class GetChatHistoryHandler : IRequestHandler<GetChatHistoryRequest, GetChatHistoryResponse>
{
    private readonly MessageRepository _repo;
    public GetChatHistoryHandler(MessageRepository repo) { _repo = repo; }

    public async ValueTask<GetChatHistoryResponse> Handle(
        GetChatHistoryRequest request, CancellationToken cancellationToken)
    {
        var recent = await _repo.GetRecentAsync(request.SessionId, request.Take, cancellationToken);
        return new GetChatHistoryResponse(recent.Reverse().ToList());
    }
}
```

- [ ] **Step 4: Commit**

```
Plan D Task 6: GetChatHistory-Handler
```

---

## Task 7: ChatEndpoints

**Files:**
- Create: `src/Backend/Endpoints/ChatEndpoints.cs`
- Create: `src/Backend.Tests/Helpers/SseTestConsumer.cs`
- Create: `src/Backend.Tests/Endpoints/ChatEndpointTests.cs`

- [ ] **Step 1: `ChatEndpoints.cs`**

```csharp
using Mediator;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Chat.ChatHistory;
using NauAssist.Backend.Features.Chat.SendMessage;

namespace NauAssist.Backend.Endpoints;

public static class ChatEndpoints
{
    private const string DefaultSessionId = "default";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (
            SendMessagePayload payload,
            IMediator mediator,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var writer = new SseWriter(ctx.Response.Body);
            var request = new SendMessageRequest(DefaultSessionId, payload.Message);

            await foreach (var ev in mediator.CreateStream(request, ct).WithCancellation(ct))
            {
                await writer.WriteAsync(ev, ct);
            }
        });

        app.MapGet("/api/chat/history", async (IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new GetChatHistoryRequest(DefaultSessionId), ct);
            return Results.Ok(response);
        });

        return app;
    }

    public sealed record SendMessagePayload(string Message);
}
```

> **Hinweis:** `X-Accel-Buffering: no` schaltet Nginx-Pufferung ab — schadet im Dev nicht, hilft später bei Reverse-Proxy. Default-Session-ID ist hardcodiert (Spec §6.1: "kein Multi-Tab- oder Multi-User-State").

- [ ] **Step 2: `SseTestConsumer` (Test-Helper)**

```csharp
namespace NauAssist.Backend.Tests.Helpers;

/// <summary>Liest einen SSE-Response-Body bis Stream-Ende und gibt (event, data)-Paare zurück.</summary>
public static class SseTestConsumer
{
    public static async Task<List<(string Event, string Data)>> ConsumeAsync(
        Stream body, CancellationToken ct)
    {
        var events = new List<(string, string)>();
        using var reader = new StreamReader(body, Encoding.UTF8);
        string? eventName = null;
        string? data = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (eventName != null && data != null)
                    events.Add((eventName, data));
                eventName = null;
                data = null;
                continue;
            }
            if (line.StartsWith("event: ")) eventName = line[7..];
            else if (line.StartsWith("data: ")) data = line[6..];
        }
        return events;
    }
}
```

- [ ] **Step 3: TDD — `ChatEndpointTests`**

```csharp
public sealed class ChatEndpointTests : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task PostChat_StreamsTokensAndDone()
    {
        await using var factory = new TestAppFactory();

        // FakeLlmClient + FakeCalendarProvider via WithWebHostBuilder überschreiben
        var fakeLlm = new FakeLlmClient();
        fakeLlm.EnqueueResponse(new[]
        {
            (LlmStreamChunk)new TextDeltaChunk("Hallo "),
            new TextDeltaChunk("Welt"),
        });

        await using var customFactory = factory.WithWebHostBuilder(b => b.ConfigureServices(svc =>
        {
            svc.AddSingleton<ILlmClient>(fakeLlm);
            svc.AddSingleton<ICalendarProvider, FakeCalendarProvider>();
        }));

        using var client = customFactory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/chat", new { message = "Hi" });
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestConsumer.ConsumeAsync(stream, CancellationToken.None);

        events.Select(e => e.Event).Should().ContainInOrder("token", "token", "done");
    }

    [Fact]
    public async Task GetHistory_ReturnsPersistedMessages()
    {
        // ... POST eine Message rein, dann GET, beide assertieren
    }
}
```

- [ ] **Step 4: DI vorbereiten** — siehe Task 10. Hier nur Build/Test:

```bash
dotnet test src/Backend.Tests --filter "FullyQualifiedName~ChatEndpointTests" -nologo --logger "console;verbosity=minimal"
```

- [ ] **Step 5: Commit**

```
Plan D Task 7: ChatEndpoints + SSE-Wiring
```

---

## Task 8: Audit-Wiring in Handlern mit Außenwirkung

**Files modify:**
- `src/Backend/Features/Calendar/CreateEvent/CreateEventHandler.cs`
- `src/Backend/Features/Rules/AddRule/AddRuleHandler.cs`
- `src/Backend/Features/Rules/DeleteRule/DeleteRuleHandler.cs`

Spec 6.7: Audit-Eintrag wird **nach** der externen Aktion geschrieben. Wenn Audit-Write fehlschlägt, ist die User-Aktion trotzdem erfolgreich (nur Log-Warning). Wir injizieren `AuditLogRepository` und ein `Func<DateTimeOffset>` und kapseln das in einem privaten Helper.

> **Wichtig:** Audit-Eintrag-`TriggeringMessageId` bleibt im MVP `null`. Den Bezug zwischen Tool-Call und auslösender Message herzustellen würde voraussetzen, dass die User-Message-ID bis in den Tool-Handler durchgereicht wird (per AsyncLocal oder explizit per Request-Feld). Das ist eine eigene Etappe — wir bauen das jetzt nicht ein, weil's nicht kritisch ist und das Schema die Spalte als nullable führt.

- [ ] **Step 1: `CreateEventHandler` erweitern**

```csharp
public sealed class CreateEventHandler : IRequestHandler<CreateEventRequest, CreateEventResponse>
{
    private readonly ICalendarProvider _calendar;
    private readonly AuditLogRepository _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<CreateEventHandler> _logger;

    public CreateEventHandler(
        ICalendarProvider calendar,
        AuditLogRepository audit,
        Func<DateTimeOffset> clock,
        ILogger<CreateEventHandler> logger)
    {
        _calendar = calendar;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<CreateEventResponse> Handle(CreateEventRequest request, CancellationToken ct)
    {
        // ... bisherige Validierung + CreateEventAsync wie gehabt
        var id = await _calendar.CreateEventAsync(newEvent, ct);

        await TryWriteAuditAsync(
            toolName: "create_event",
            argsJson: JsonSerializer.Serialize(request),
            resultJson: JsonSerializer.Serialize(new { id }),
            providerEventId: id,
            ct);

        return new CreateEventResponse(id);
    }

    private async Task TryWriteAuditAsync(
        string toolName, string argsJson, string resultJson, string? providerEventId, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new AuditEntry(
                Id: 0,
                TriggeringMessageId: null,
                ToolName: toolName,
                ToolArgsJson: argsJson,
                ResultJson: resultJson,
                ProviderEventId: providerEventId,
                CreatedAt: _clock()),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit-Eintrag für {Tool} fehlgeschlagen.", toolName);
        }
    }
}
```

- [ ] **Step 2: gleiches Pattern in `AddRuleHandler` und `DeleteRuleHandler`**

  - `add_rule` → `argsJson` = serialisierter Request, `resultJson` = `{"ruleId": ...}`, `providerEventId` = null
  - `delete_rule` → `argsJson` = `{"id": ...}`, `resultJson` = `{"deleted": true|false}`, `providerEventId` = null

- [ ] **Step 3: Bestehende Handler-Tests anpassen**

`CreateEventHandlerTests`, `AddRuleHandlerTests`, `DeleteRuleHandlerTests` brauchen jetzt zusätzliche Konstruktor-Argumente (`AuditLogRepository`, Clock, Logger). Für die bestehenden Assertions ändert sich nichts — wir geben einfach einen frisch initialisierten `AuditLogRepository` gegen eine Temp-DB rein.

Plus ein neuer Test pro Handler:
```csharp
[Fact]
public async Task Handle_AfterCreate_WritesAuditEntry()
{
    // ... Handler aufrufen, dann AuditRepo.GetByMessageIdAsync(...) oder
    //     direkter Count-Check: SELECT COUNT(*) FROM audit_log = 1
}
```

- [ ] **Step 4: Tests laufen**

```bash
dotnet test src/Backend.Tests -nologo --logger "console;verbosity=minimal"
```

- [ ] **Step 5: Commit**

```
Plan D Task 8: Audit-Log-Wiring in CreateEvent + AddRule + DeleteRule
```

---

## Task 9: Integrations-Test — Full-Loop gegen ChatEndpoint

**Files:**
- Create: `src/Backend.Tests/Endpoints/ChatEndpointFullLoopTests.cs` (oder ergänzen in `ChatEndpointTests`)

Ein Test, der den kompletten Pfad durchspielt: User pastet Anfrage → `lookup_free_slots` → `present_proposals` → finale Text-Antwort → `create_event` (zweite Message) → Audit-Eintrag ist da. Das ist die End-to-End-Sicherung des Backends vor Plan E.

- [ ] **Step 1: Test schreiben**

```csharp
[Fact]
public async Task FullLoop_RequestProposeAndConfirm_CreatesAuditedEvent()
{
    await using var factory = new TestAppFactory();

    var fakeLlm = new FakeLlmClient();
    // Skript: erst lookup_free_slots, dann present_proposals, dann Text "Schlag X vor"
    fakeLlm.EnqueueResponse(new[]
    {
        (LlmStreamChunk)new ToolCallChunk(new LlmToolCall("c1", "lookup_free_slots",
            JsonDocument.Parse("""{"from":"2026-05-20T08:00:00Z","to":"2026-05-22T17:00:00Z","durationMinutes":60}""").RootElement)),
    });
    fakeLlm.EnqueueResponse(new[]
    {
        (LlmStreamChunk)new ToolCallChunk(new LlmToolCall("c2", "present_proposals",
            JsonDocument.Parse("""{"slots":[{"start":"2026-05-20T09:00:00Z","end":"2026-05-20T10:00:00Z"}]}""").RootElement)),
    });
    fakeLlm.EnqueueResponse(new[]
    {
        (LlmStreamChunk)new TextDeltaChunk("Wie wäre 20.05. 9 Uhr?"),
    });

    var fakeCal = new FakeCalendarProvider();

    await using var customFactory = factory.WithWebHostBuilder(b => b.ConfigureServices(svc =>
    {
        svc.AddSingleton<ILlmClient>(fakeLlm);
        svc.AddSingleton<ICalendarProvider>(fakeCal);
    }));

    using var client = customFactory.CreateClient();

    // 1. User pastet
    using var resp1 = await client.PostAsJsonAsync("/api/chat", new { message = "Treffen mit Anna nächste Woche?" });
    resp1.EnsureSuccessStatusCode();
    var events1 = await SseTestConsumer.ConsumeAsync(await resp1.Content.ReadAsStreamAsync(), default);
    events1.Select(e => e.Event).Should().Contain("tool_started").And.Contain("proposals").And.Contain("done");

    // 2. User bestätigt
    fakeLlm.EnqueueResponse(new[]
    {
        (LlmStreamChunk)new ToolCallChunk(new LlmToolCall("c3", "create_event",
            JsonDocument.Parse("""{"title":"Treffen mit Anna","start":"2026-05-20T09:00:00Z","end":"2026-05-20T10:00:00Z"}""").RootElement)),
    });
    fakeLlm.EnqueueResponse(new[]
    {
        (LlmStreamChunk)new TextDeltaChunk("Erledigt!"),
    });

    using var resp2 = await client.PostAsJsonAsync("/api/chat", new { message = "Ja, passt." });
    resp2.EnsureSuccessStatusCode();

    fakeCal.CreatedEvents.Should().HaveCount(1);
    // Audit-Eintrag direkt aus der DB ziehen (über DI-Service)
}
```

- [ ] **Step 2: Test grün**

- [ ] **Step 3: Commit**

```
Plan D Task 9: End-to-End-Integrations-Test für Chat-Loop
```

---

## Task 10: DI-Verkabelung + Endpoint-Map

**Files modify:**
- `src/Backend/Program.cs`

- [ ] **Step 1: DI-Registrierungen ergänzen**

In `Program.cs` nach den bestehenden Calendar/Agent-Registrierungen:

```csharp
// Chat & Audit
builder.Services.AddScoped<MessageRepository>();
builder.Services.AddScoped<AuditLogRepository>();
```

Und nach `MapRulesEndpoints()` einfügen:

```csharp
app.MapChatEndpoints();
```

- [ ] **Step 2: `using` ergänzen**

```csharp
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Infrastructure.Audit;
```

- [ ] **Step 3: Build verifizieren**

```bash
dotnet build src/Backend -nologo -v quiet 2>&1 | tail -5
```

- [ ] **Step 4: Volle Test-Suite laufen**

```bash
dotnet test -nologo --logger "console;verbosity=minimal"
```

Alle bisherigen 72 + neue Plan-D-Tests müssen grün sein.

- [ ] **Step 5: Manueller Smoke-Test (optional, nicht im CI-Pfad)**

```bash
dotnet run --project src/Backend &
sleep 2
curl -N -X POST http://localhost:5000/api/chat \
     -H "Content-Type: application/json" \
     -d '{"message":"Hi"}'
# erwartet: text/event-stream mit token/.../done-Frames
```

> Falls Ollama nicht läuft, wird der Stream mit `error`-Event abbrechen. Das ist OK — wir wollen nur sehen, dass der Endpoint überhaupt antwortet.

- [ ] **Step 6: Commit**

```
Plan D Task 10: DI-Verkabelung für Chat + Audit, MapChatEndpoints
```

---

## Was nach Plan D steht

**Backend-MVP komplett.** Folgendes geht durch:
- `POST /api/chat` mit echtem Streaming
- Konversation persistiert in SQLite, History abrufbar
- Termine landen im Google Kalender, Regeln werden gespeichert, alles auditiert
- Tool-Loop-Limit greift, Token-Timeout greift, partielle Antworten werden geretten
- 80+ Tests grün

**Was als Nächstes kommt (Plan E — Frontend):**
- `frontend/` mit Vite + React + TypeScript (strict) + Tailwind + shadcn/ui
- Chat-UI: Eingabefeld, Bubble-Liste, Slot-Karten
- `EventSource`-Konsument für SSE
- History-Initial-Load via `GET /api/chat/history`
- Lokales Dev-Setup: `npm run dev` + CORS für Backend-Port

Damit ist der MVP nach Spec Abschnitt 2 vollständig — der Workflow „User pastet → Agent schlägt vor → User bestätigt → Termin gebucht" läuft End-to-End.
