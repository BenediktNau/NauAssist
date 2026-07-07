# Self-Writing Watch-Jobs — Phase 2 (Pushover + Async-UX) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. TDD nur dort, wo ein Test-Runner existiert (`src/Backend.Tests`, xUnit + **AwesomeAssertions**); das Frontend hat keinen Test-Runner — dort gilt `typecheck` + `lint` + `build` als Verifikation.

**Design-Grundlage:** [`docs/superpowers/specs/2026-06-24-self-writing-watch-jobs-design.md`](../specs/2026-06-24-self-writing-watch-jobs-design.md) — Abschnitt „Phasen-Vorschlag → Phase 2". Phase 1 (PR #87, gemergt) lieferte: Datenmodell/Repository, Web-Zugriff, Judge/Executor/Scheduler, Chat-Tools, Web-Push-Benachrichtigung, `GET /api/watch-jobs`.

**Goal:** Watch-Jobs melden sich über **Pushover** zusätzlich zu Web-Push, die offene PWA bekommt Treffer **live** über einen `/api/events`-SSE-Stream, eine **Watcher-Seite** zeigt und steuert laufende Jobs (Pause/Weiter/Stop), und ein **Teilsignal** des Judge verdichtet die Prüf-Kadenz (Hot-Mode).

**Architecture:** Benachrichtigung wandert hinter eine `INotificationChannel`-Abstraktion (DI-Collection; `WebPushChannel` adaptiert den bestehenden `WebPushSender`, `PushoverChannel` ist neu). Ein singleton `ProactiveEventBroker` (System.Threading.Channels, strikt pro User) entkoppelt Publisher (`WatchJobNotifier`) von SSE-Subscribern (`GET /api/events`). Die Watcher-UI ist eine vierte Tab-Seite nach dem Muster der `RecommendationsPage`, capabilities-gated. Hot-Mode ist ein neues `partialSignal`-Feld im Judge-JSON, das der Executor in ein kurzes Intervall übersetzt.

**Tech Stack:** .NET 10 Minimal API, Dapper/SQLite, System.Threading.Channels; Tests xUnit + **AwesomeAssertions**. Frontend React + TypeScript + Vite, TanStack Query, `@microsoft/fetch-event-source` (bereits Dependency), Tailwind (nau-Design-Tokens), lucide-react.

## Global Constraints

- **Reihenfolge:** 1 → 11 strikt. Jede Task ist für sich baubar (`dotnet build src/Backend` bzw. Frontend-`build`).
- **Commit-Style:** Conventional-Commit-Subjects wie im Repo (`feat(backend): …`), **eine Subject-Line, kein Body, keine Co-Author-Trailer** (Repo-Konvention).
- **Test-Framework:** **AwesomeAssertions** (nicht FluentAssertions). Vorbilder in `src/Backend.Tests`.
- **Additiv & opt-in:** Feature-Flag `AutonomousAgent:WatchJobs:Enabled` (default `false`) bleibt der Schalter für Scheduler/Tools/Watch-Job-Endpoints. **Ausnahme:** `/api/events` und die Pushover-Settings werden immer gemappt — sie sind generische Infrastruktur (der Event-Stream soll später auch anderen Features dienen).
- **Multi-User:** `/api/events` und der Broker sind strikt per `IUserContext.UserId` getrennt; Repos bleiben user-gescoped wie in Phase 1.
- **Secrets:** Pushover-Token/User-Key werden **nie** im Klartext an den Client zurückgegeben (nur `hasToken`/`hasUserKey`) und **nie** ins Audit-Log geschrieben.
- **Kadenz-Schutz:** Hot-Mode darf `MinIntervalSeconds` (30) bewusst unterschreiten, aber nie unter die harte Untergrenze **10 s**.
- **Frontend-Verifikation:** in `src/frontend`: `npm run typecheck && npm run lint && npm run build`.

## File Structure

| Datei | Verantwortung | Task |
| --- | --- | --- |
| `src/Backend/Features/WatchJobs/Notify/INotificationChannel.cs` *(neu)* | Kanal-Interface + `WatchNotification`-Record | 1 |
| `src/Backend/Features/WatchJobs/Notify/WebPushChannel.cs` *(neu)* | Adapter `WebPushSender` → Kanal `webpush` | 1 |
| `src/Backend/Features/WatchJobs/WatchJobNotifier.cs` | Refactor: iteriert DI-Kanäle statt hartem Web-Push | 1, 5 |
| `src/Backend/Features/Settings/PushoverSettings.cs` *(neu)* | Record Token/UserKey + `IsConfigured` | 2 |
| `src/Backend/Features/Settings/IAppSettingsRepository.cs` + `AppSettingsRepository.cs` | `GetPushoverAsync`/`SetPushoverAsync` (Keys `push.pushover_*`) | 2 |
| `src/Backend/Endpoints/SettingsEndpoints.cs` | `GET`/`PUT /api/settings/pushover` | 2 |
| `src/Backend/Features/WatchJobs/Notify/PushoverChannel.cs` *(neu)* | Kanal `pushover` über Pushover-Message-API | 3 |
| `src/Backend/Features/Events/ProactiveEventBroker.cs` *(neu)* | Singleton-Broker: per-User-Channels, Subscribe/Publish | 4 |
| `src/Backend/Endpoints/EventsEndpoints.cs` *(neu)* | `GET /api/events` (SSE, Heartbeat) | 5 |
| `src/Backend/Features/WatchJobs/WatchJudgeResult.cs` + `WatchJudge.cs` | `partialSignal` im Urteil | 6 |
| `src/Backend/Features/WatchJobs/WatchJobOptions.cs` + `WatchJobExecutor.cs` + `appsettings.json` | `HotIntervalSeconds` + Hot-Mode-Decide | 6 |
| `src/Backend/Features/WatchJobs/WatchJobRepository.cs` | `ListByUserAsync` (alle Status) | 7 |
| `src/Backend/Endpoints/WatchJobsEndpoints.cs` | Gesamtliste + `POST {id}/pause\|resume\|cancel` | 7 |
| `src/Backend/Features/WatchJobs/Tools/CancelWatchJobTool.cs` | `mode: resume` | 7 |
| `src/Backend/Endpoints/CapabilitiesEndpoints.cs` | Flag `watchJobs` | 8 |
| `src/frontend/src/api/watch-jobs.ts` *(neu)* · `api/pushover.ts` *(neu)* · `api/capabilities.ts` · `hooks/queries.ts` · `hooks/useProactiveEvents.ts` *(neu)* | API-Layer, Query-Keys, Live-Event-Hook | 9 |
| `src/frontend/src/components/pages/WatchersPage.tsx` *(neu)* · `App.tsx` · `components/nau/Layout.tsx` · `components/nau/MobileTabBar.tsx` | Watcher-Seite + Tab-Navigation (gated) | 10 |
| `src/frontend/src/components/settings/PushoverSection.tsx` *(neu)* · `components/pages/SettingsPage.tsx` | Pushover-Settings-UI | 11 |

---

## Task 1: `INotificationChannel` + `WebPushChannel` (Notifier-Refactor)

**Files:**
- Create: `src/Backend/Features/WatchJobs/Notify/INotificationChannel.cs`
- Create: `src/Backend/Features/WatchJobs/Notify/WebPushChannel.cs`
- Modify: `src/Backend/Features/WatchJobs/WatchJobNotifier.cs`
- Modify: `src/Backend/Program.cs` (DI)
- Test: `src/Backend.Tests/Features/WatchJobs/WatchJobNotifierTests.cs`

**Interfaces:**
- Produces: `INotificationChannel { string Name; Task<bool> SendAsync(WatchNotification, ct) }`, `record WatchNotification(string Title, string Body, string? Url, string? Tag)` — Task 3 implementiert dagegen, Task 5 erweitert den Notifier weiter.
- Consumes: `WebPushSender.BroadcastAsync(PushNotificationPayload, ct) → Task<int>` (bestehend).

- [ ] **Step 1: Failing Test.** In `WatchJobNotifierTests.cs` den Helper `BuildNotifier` auf die neue Signatur umstellen (kompiliert erst nach Step 2 — das ist der „failing test") und einen neuen Test ergänzen:

```csharp
// BuildNotifier neu:
private static WatchJobNotifier BuildNotifier(TempSqliteDb temp, UserContextHolder holder, MessageRepository messages)
{
    var push = new WebPushSender(
        new PushSubscriptionRepository(temp.AppDb, holder),
        new FakeSettingsRepo(),
        () => Now,
        NullLogger<WebPushSender>.Instance);
    return new WatchJobNotifier(
        new INotificationChannel[] { new WebPushChannel(push) },
        messages, () => Now, NullLogger<WatchJobNotifier>.Instance);
}

[Fact]
public async Task NotifyAsync_FailingChannelDoesNotPreventOtherChannels()
{
    using var temp = new TempSqliteDb();
    var holder = new UserContextHolder();
    var messages = new MessageRepository(temp.AppDb, holder);
    var ok = new RecordingChannel("ok");
    var boom = new ThrowingChannel("boom");
    var notifier = new WatchJobNotifier(
        new INotificationChannel[] { boom, ok },
        messages, () => Now, NullLogger<WatchJobNotifier>.Instance);

    var job = SampleJob(channels: new[] { "boom", "ok" });
    await notifier.NotifyAsync(job, new WatchJudgeResult(true, 0.9, Array.Empty<JudgeEvidence>(), "Treffer"), CancellationToken.None);

    ok.Sent.Should().HaveCount(1);
    (await messages.GetRecentAsync("default", 10, CancellationToken.None)).Should().HaveCount(1);
}

private sealed class RecordingChannel : INotificationChannel
{
    public RecordingChannel(string name) => Name = name;
    public string Name { get; }
    public List<WatchNotification> Sent { get; } = new();
    public Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
    {
        Sent.Add(notification);
        return Task.FromResult(true);
    }
}

private sealed class ThrowingChannel : INotificationChannel
{
    public ThrowingChannel(string name) => Name = name;
    public string Name { get; }
    public Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
        => throw new InvalidOperationException("Kanal kaputt");
}
```

Oben `using NauAssist.Backend.Features.WatchJobs.Notify;` ergänzen. Der bestehende Test `NotifyAsync_WithUnknownChannel_StillPersistsMessage` bleibt unverändert gültig (unbekannter Kanal `pushover` wird bis Task 3 weiter ignoriert).

- [ ] **Step 2: Interface + Adapter anlegen.**

`src/Backend/Features/WatchJobs/Notify/INotificationChannel.cs`:

```csharp
namespace NauAssist.Backend.Features.WatchJobs.Notify;

/// <summary>Was beim Feuern eines Watch-Jobs verschickt wird — kanalneutral.</summary>
public sealed record WatchNotification(string Title, string Body, string? Url, string? Tag);

/// <summary>
/// Ein Benachrichtigungskanal (webpush, pushover, …). Implementierungen werden über DI
/// als Collection eingesammelt; <see cref="Name"/> ist der Wire-Name, wie er in
/// <c>WatchJobNotify.Channels</c> steht.
/// </summary>
public interface INotificationChannel
{
    string Name { get; }

    /// <summary>Sendet; false, wenn der Kanal nicht konfiguriert ist oder nichts zugestellt wurde.</summary>
    Task<bool> SendAsync(WatchNotification notification, CancellationToken ct);
}
```

`src/Backend/Features/WatchJobs/Notify/WebPushChannel.cs`:

```csharp
using NauAssist.Backend.Features.AutonomousAgent.Push;

namespace NauAssist.Backend.Features.WatchJobs.Notify;

/// <summary>Adapter: der bestehende <see cref="WebPushSender"/> als Watch-Job-Kanal "webpush".</summary>
public sealed class WebPushChannel : INotificationChannel
{
    private readonly WebPushSender _push;

    public WebPushChannel(WebPushSender push) => _push = push;

    public string Name => "webpush";

    public async Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
        => await _push.BroadcastAsync(
            new PushNotificationPayload(notification.Title, notification.Body, notification.Url, notification.Tag),
            ct) > 0;
}
```

- [ ] **Step 3: Notifier refactoren.** `WatchJobNotifier.cs` — Konstruktor nimmt `IEnumerable<INotificationChannel>` statt `WebPushSender`; `NotifyAsync` persistiert die Chat-Nachricht wie bisher und iteriert dann die gewünschten Kanäle:

```csharp
using System.Text;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.WatchJobs.Notify;

namespace NauAssist.Backend.Features.WatchJobs;

/// <summary>
/// Benachrichtigt beim Feuern eines Watch-Jobs: proaktive Assistant-Nachricht in der
/// Chat-History (Deep-Link-Ziel der Pushes) plus alle in der Job-Spec gewünschten Kanäle.
/// Unbekannte Kanäle werden geloggt und ignoriert; ein fehlschlagender Kanal stoppt die anderen nicht.
/// </summary>
public sealed class WatchJobNotifier
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly MessageRepository _messages;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<WatchJobNotifier> _logger;

    public WatchJobNotifier(
        IEnumerable<INotificationChannel> channels,
        MessageRepository messages,
        Func<DateTimeOffset> clock,
        ILogger<WatchJobNotifier> logger)
    {
        _channels = channels.ToList();
        _messages = messages;
        _clock = clock;
        _logger = logger;
    }

    public async Task NotifyAsync(WatchJob job, WatchJudgeResult result, CancellationToken ct)
    {
        var body = BuildBody(job, result);

        // Proaktive Chat-Nachricht — taucht in der History auf und ist das Ziel des Push-Deep-Links.
        await _messages.AddAsync(
            new Message(
                Id: 0,
                SessionId: ChatSessions.Default,
                Role: MessageRole.Assistant,
                Content: body,
                ProposalsJson: null,
                Incomplete: false,
                CreatedAt: _clock()),
            ct);

        var notification = new WatchNotification(
            Title: job.Title,
            Body: Truncate(result.Summary, 200),
            Url: "/chat",
            Tag: $"watch-{job.Id}");

        foreach (var name in job.Notify.Channels)
        {
            var channel = _channels.FirstOrDefault(
                c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (channel is null)
            {
                _logger.LogInformation("WatchJob {Id}: unbekannter Kanal '{Channel}' — übersprungen.", job.Id, name);
                continue;
            }

            try
            {
                await channel.SendAsync(notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "WatchJob {Id}: Kanal '{Channel}' fehlgeschlagen.", job.Id, name);
            }
        }
    }

    // BuildBody(...) und Truncate(...) unverändert aus der bestehenden Datei übernehmen.
}
```

- [ ] **Step 4: DI.** In `Program.cs` bei den Watch-Job-Registrierungen (`builder.Services.AddScoped<WatchJobNotifier>();`) ergänzen — plus `using NauAssist.Backend.Features.WatchJobs.Notify;`:

```csharp
builder.Services.AddScoped<INotificationChannel, WebPushChannel>();
```

- [ ] **Step 5: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS (alle bestehenden + neuer Test).
- [ ] **Step 6: Commit.** `git commit -m "refactor(backend): INotificationChannel-Abstraktion + WebPushChannel für Watch-Job-Benachrichtigung"`

## Task 2: Pushover-Settings (Repository + Endpoints)

**Files:**
- Create: `src/Backend/Features/Settings/PushoverSettings.cs`
- Modify: `src/Backend/Features/Settings/IAppSettingsRepository.cs`, `src/Backend/Features/Settings/AppSettingsRepository.cs`
- Modify: `src/Backend/Endpoints/SettingsEndpoints.cs`
- Modify: `src/Backend.Tests/Helpers/FakeSettingsRepo.cs` (neue Interface-Member)
- Test: `src/Backend.Tests/Features/Settings/AppSettingsRepository.PushoverTests.cs` *(neu)*

**Interfaces:**
- Produces: `record PushoverSettings(string Token, string UserKey) { bool IsConfigured }`; `IAppSettingsRepository.GetPushoverAsync(ct)` / `SetPushoverAsync(settings, ct)`; `GET/PUT /api/settings/pushover`. Task 3 (Channel) und Task 11 (UI) bauen darauf.

- [ ] **Step 1: Failing Test.** `AppSettingsRepository.PushoverTests.cs` (Konstruktion wie die Nachbar-Tests im Ordner, `AppSettingsRepository(AppDb, IUserContext)` mit `TempSqliteDb` + `UserContextHolder`):

```csharp
using AwesomeAssertions;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryPushoverTests
{
    [Fact]
    public async Task Pushover_DefaultsToEmpty_AndRoundtrips()
    {
        using var temp = new TempSqliteDb();
        var repo = new AppSettingsRepository(temp.AppDb, new UserContextHolder());

        var initial = await repo.GetPushoverAsync(CancellationToken.None);
        initial.IsConfigured.Should().BeFalse();

        await repo.SetPushoverAsync(new PushoverSettings("app-token", "user-key"), CancellationToken.None);

        var loaded = await repo.GetPushoverAsync(CancellationToken.None);
        loaded.Token.Should().Be("app-token");
        loaded.UserKey.Should().Be("user-key");
        loaded.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Pushover_CanBeCleared()
    {
        using var temp = new TempSqliteDb();
        var repo = new AppSettingsRepository(temp.AppDb, new UserContextHolder());
        await repo.SetPushoverAsync(new PushoverSettings("t", "u"), CancellationToken.None);

        await repo.SetPushoverAsync(new PushoverSettings("", ""), CancellationToken.None);

        (await repo.GetPushoverAsync(CancellationToken.None)).IsConfigured.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails.** Run: `dotnet test src/Backend.Tests --filter Pushover` — Expected: Compile-Fehler (`PushoverSettings`/`GetPushoverAsync` unbekannt).
- [ ] **Step 3: Model + Repository.**

`src/Backend/Features/Settings/PushoverSettings.cs`:

```csharp
namespace NauAssist.Backend.Features.Settings;

/// <summary>Pushover-Zugangsdaten (https://pushover.net): App-Token + User-Key.</summary>
public sealed record PushoverSettings(string Token, string UserKey)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(UserKey);
}
```

`IAppSettingsRepository.cs` — unter den Vapid-Membern ergänzen:

```csharp
Task<PushoverSettings> GetPushoverAsync(CancellationToken ct);
Task SetPushoverAsync(PushoverSettings settings, CancellationToken ct);
```

`AppSettingsRepository.cs` — Keys bei den anderen `private const string` und Methoden exakt nach dem Muster `GetVapidAsync`/`SetVapidAsync` (gleiche Helfer `UpsertAsync`, gleiche Transaktion):

```csharp
private const string KeyPushoverToken   = "push.pushover_token";
private const string KeyPushoverUserKey = "push.pushover_user_key";

public async Task<PushoverSettings> GetPushoverAsync(CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    var rows = await conn.QueryAsync<(string Key, string Value)>(new CommandDefinition(
        "SELECT key, value FROM app_settings WHERE key IN (@k1, @k2);",
        new { k1 = KeyPushoverToken, k2 = KeyPushoverUserKey },
        cancellationToken: ct));
    var map = rows.ToDictionary(r => r.Key, r => r.Value);
    return new PushoverSettings(
        Token: map.GetValueOrDefault(KeyPushoverToken, ""),
        UserKey: map.GetValueOrDefault(KeyPushoverUserKey, ""));
}

public async Task SetPushoverAsync(PushoverSettings settings, CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    using var tx = conn.BeginTransaction();
    try
    {
        await UpsertAsync(conn, tx, KeyPushoverToken, settings.Token, ct);
        await UpsertAsync(conn, tx, KeyPushoverUserKey, settings.UserKey, ct);
        tx.Commit();
    }
    catch
    {
        tx.Rollback();
        throw;
    }
}
```

`FakeSettingsRepo.cs` (Tests-Helpers) — neue Member mit setzbarem Wert, damit Task 3 ihn nutzen kann:

```csharp
public PushoverSettings Pushover { get; set; } = new("", "");

public Task<PushoverSettings> GetPushoverAsync(CancellationToken ct) => Task.FromResult(Pushover);

public Task SetPushoverAsync(PushoverSettings settings, CancellationToken ct)
{
    Pushover = settings;
    return Task.CompletedTask;
}
```

Falls weitere `IAppSettingsRepository`-Implementierungen existieren, meldet der Compiler sie — dort genauso minimal ergänzen.

- [ ] **Step 4: Endpoints.** In `SettingsEndpoints.MapSettingsEndpoints` vor `return app;` (Muster: Persona-Endpoints; `using System.Text.Json;` falls noch nicht vorhanden):

```csharp
app.MapGet("/api/settings/pushover", async (IAppSettingsRepository repo, CancellationToken ct) =>
{
    var s = await repo.GetPushoverAsync(ct);
    // Secrets nie zurückgeben — nur, ob sie gesetzt sind.
    return Results.Ok(new PushoverSettingsDto(
        HasToken: !string.IsNullOrWhiteSpace(s.Token),
        HasUserKey: !string.IsNullOrWhiteSpace(s.UserKey)));
});

app.MapPut("/api/settings/pushover", async (
    UpdatePushoverPayload payload,
    IAppSettingsRepository repo,
    AuditLogRepository audit,
    Func<DateTimeOffset> clock,
    CancellationToken ct) =>
{
    var next = new PushoverSettings(
        Token: payload.Token?.Trim() ?? "",
        UserKey: payload.UserKey?.Trim() ?? "");
    await repo.SetPushoverAsync(next, ct);
    await audit.AppendAsync(new AuditEntry(
        0, null, "settings.pushover.update",
        JsonSerializer.Serialize(new { hasToken = next.Token.Length > 0, hasUserKey = next.UserKey.Length > 0 }),
        "{\"ok\":true}", null, clock()), ct);
    return Results.NoContent();
});
```

DTOs bei den anderen Records am Dateiende:

```csharp
public sealed record UpdatePushoverPayload(string? Token, string? UserKey);
private sealed record PushoverSettingsDto(bool HasToken, bool HasUserKey);
```

- [ ] **Step 5: Endpoint-Test.** In `src/Backend.Tests/Endpoints/SettingsEndpointsTests.cs` ergänzen (bestehende Factory-/Client-Konstruktion der Datei wiederverwenden):

```csharp
[Fact]
public async Task PushoverSettings_PutThenGet_ReportsConfiguredWithoutLeakingSecrets()
{
    using var factory = new TestAppFactory();
    var client = factory.CreateClient();

    var put = await client.PutAsJsonAsync("/api/settings/pushover", new { token = "app-token", userKey = "user-key" });
    put.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var res = await client.GetAsync("/api/settings/pushover");
    res.EnsureSuccessStatusCode();
    var body = await res.Content.ReadAsStringAsync();
    body.Should().Contain("\"hasToken\":true").And.Contain("\"hasUserKey\":true");
    body.Should().NotContain("app-token").And.NotContain("user-key");
}
```

- [ ] **Step 6: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS.
- [ ] **Step 7: Commit.** `git commit -m "feat(backend): Pushover-Settings — Repository + GET/PUT /api/settings/pushover"`

## Task 3: `PushoverChannel`

**Files:**
- Create: `src/Backend/Features/WatchJobs/Notify/PushoverChannel.cs`
- Modify: `src/Backend/Program.cs` (HttpClient + DI)
- Modify: `src/Backend/Features/WatchJobs/Tools/CreateWatchJobTool.cs` (Schema-Beschreibung `channels`)
- Test: `src/Backend.Tests/Features/WatchJobs/PushoverChannelTests.cs` *(neu)*

**Interfaces:**
- Consumes: `INotificationChannel`/`WatchNotification` (Task 1), `IAppSettingsRepository.GetPushoverAsync` (Task 2).
- Produces: Kanal-Name `"pushover"`; `PushoverChannel.HttpClientName = "Pushover"`.

- [ ] **Step 1: Failing Tests.** `PushoverChannelTests.cs`:

```csharp
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.WatchJobs.Notify;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class PushoverChannelTests
{
    [Fact]
    public async Task SendAsync_PostsFormFieldsToPushoverApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":1}""");
        var channel = BuildChannel(handler, new PushoverSettings("tok", "usr"));

        var ok = await channel.SendAsync(
            new WatchNotification("Midea verfügbar", "Bei ShopX lieferbar", "/chat", "watch-1"),
            CancellationToken.None);

        ok.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.pushover.net/1/messages.json");
        handler.LastBody.Should().Contain("token=tok").And.Contain("user=usr");
        handler.LastBody.Should().Contain("title=Midea+verf").And.Contain("message=Bei+ShopX");
        // Relative URLs (PWA-interne Deep-Links) werden nicht mitgesendet.
        handler.LastBody.Should().NotContain("url=");
    }

    [Fact]
    public async Task SendAsync_NotConfigured_SkipsWithoutRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":1}""");
        var channel = BuildChannel(handler, new PushoverSettings("", ""));

        var ok = await channel.SendAsync(new WatchNotification("T", "B", null, null), CancellationToken.None);

        ok.Should().BeFalse();
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_HttpError_ReturnsFalse()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"status":0,"errors":["user key invalid"]}""");
        var channel = BuildChannel(handler, new PushoverSettings("tok", "usr"));

        (await channel.SendAsync(new WatchNotification("T", "B", null, null), CancellationToken.None))
            .Should().BeFalse();
    }

    private static PushoverChannel BuildChannel(RecordingHandler handler, PushoverSettings settings)
        => new(
            new SingleClientFactory(new HttpClient(handler)),
            new FakeSettingsRepo { Pushover = settings },
            NullLogger<PushoverChannel>.Instance);

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
```

Hinweis: falls `FakeSettingsRepo` keinen parameterlosen Objekt-Initialisierer erlaubt, `new FakeSettingsRepo() { Pushover = settings }` mit dem Default-Konstruktor verwenden (Property kommt aus Task 2).

- [ ] **Step 2: Run tests to verify they fail.** Run: `dotnet test src/Backend.Tests --filter PushoverChannel` — Expected: Compile-Fehler (`PushoverChannel` unbekannt).
- [ ] **Step 3: Implementierung.** `src/Backend/Features/WatchJobs/Notify/PushoverChannel.cs`:

```csharp
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.WatchJobs.Notify;

/// <summary>
/// Kanal "pushover": sendet über die Pushover-Message-API (https://pushover.net/api).
/// Ohne konfigurierte Zugangsdaten wird still übersprungen (false) — der Kanal ist optional.
/// </summary>
public sealed class PushoverChannel : INotificationChannel
{
    public const string HttpClientName = "Pushover";
    private const string MessagesUrl = "https://api.pushover.net/1/messages.json";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IAppSettingsRepository _settings;
    private readonly ILogger<PushoverChannel> _logger;

    public PushoverChannel(
        IHttpClientFactory httpFactory,
        IAppSettingsRepository settings,
        ILogger<PushoverChannel> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    public string Name => "pushover";

    public async Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
    {
        var s = await _settings.GetPushoverAsync(ct);
        if (!s.IsConfigured)
        {
            _logger.LogInformation("Pushover nicht konfiguriert — Kanal wird übersprungen.");
            return false;
        }

        var fields = new Dictionary<string, string>
        {
            ["token"] = s.Token,
            ["user"] = s.UserKey,
            ["title"] = notification.Title,
            ["message"] = notification.Body,
        };
        // Pushover braucht absolute URLs; PWA-interne Pfade wie "/chat" sind dort nutzlos.
        if (Uri.TryCreate(notification.Url, UriKind.Absolute, out _))
        {
            fields["url"] = notification.Url!;
            fields["url_title"] = "In NauAssist öffnen";
        }

        var client = _httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(MessagesUrl, new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Pushover-Send fehlgeschlagen: HTTP {Status} — {Body}",
                (int)response.StatusCode, Truncate(body, 200));
            return false;
        }

        return true;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
```

- [ ] **Step 4: DI + Tool-Beschreibung.** `Program.cs` (direkt unter der `WebPushChannel`-Registrierung):

```csharp
builder.Services.AddHttpClient(PushoverChannel.HttpClientName);
builder.Services.AddScoped<INotificationChannel, PushoverChannel>();
```

`CreateWatchJobTool.cs` — die `channels`-Beschreibung im `ParameterSchema` aktualisieren:

```
"channels": { "type": "array", "items": { "type": "string" }, "description": "Benachrichtigungskanäle: webpush, pushover" },
```

- [ ] **Step 5: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS.
- [ ] **Step 6: Commit.** `git commit -m "feat(backend): PushoverChannel — Watch-Job-Benachrichtigung über Pushover"`

## Task 4: `ProactiveEventBroker`

**Files:**
- Create: `src/Backend/Features/Events/ProactiveEventBroker.cs`
- Test: `src/Backend.Tests/Features/Events/ProactiveEventBrokerTests.cs` *(neu)*

**Interfaces:**
- Produces: `record ProactiveEvent(string EventName, string DataJson)`; `ProactiveEventBroker.Subscribe(string userId) → Subscription { ChannelReader<ProactiveEvent> Reader } : IDisposable`; `Publish(string userId, ProactiveEvent ev) → int` (Anzahl erreichter Subscriber). Task 5 nutzt beides.

- [ ] **Step 1: Failing Tests.** `ProactiveEventBrokerTests.cs`:

```csharp
using AwesomeAssertions;
using NauAssist.Backend.Features.Events;

namespace NauAssist.Backend.Tests.Features.Events;

public sealed class ProactiveEventBrokerTests
{
    [Fact]
    public async Task Publish_ReachesSubscriberOfSameUser()
    {
        var broker = new ProactiveEventBroker();
        using var sub = broker.Subscribe("user-a");

        var delivered = broker.Publish("user-a", new ProactiveEvent("chat_message", """{"messageId":1}"""));

        delivered.Should().Be(1);
        var ev = await sub.Reader.ReadAsync(CancellationToken.None);
        ev.EventName.Should().Be("chat_message");
        ev.DataJson.Should().Contain("\"messageId\":1");
    }

    [Fact]
    public void Publish_DoesNotCrossUsers()
    {
        var broker = new ProactiveEventBroker();
        using var subB = broker.Subscribe("user-b");

        var delivered = broker.Publish("user-a", new ProactiveEvent("chat_message", "{}"));

        delivered.Should().Be(0);
        subB.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_WithoutSubscribers_ReturnsZero()
    {
        var broker = new ProactiveEventBroker();
        broker.Publish("niemand", new ProactiveEvent("x", "{}")).Should().Be(0);
    }

    [Fact]
    public void DisposedSubscription_NoLongerReceives()
    {
        var broker = new ProactiveEventBroker();
        var sub = broker.Subscribe("user-a");
        sub.Dispose();

        broker.Publish("user-a", new ProactiveEvent("x", "{}")).Should().Be(0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail.** Run: `dotnet test src/Backend.Tests --filter ProactiveEventBroker` — Expected: Compile-Fehler.
- [ ] **Step 3: Implementierung.** `src/Backend/Features/Events/ProactiveEventBroker.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NauAssist.Backend.Features.Events;

/// <summary>Ein server-initiiertes Ereignis für die offene PWA (SSE-Frame: EventName + fertiges Daten-JSON).</summary>
public sealed record ProactiveEvent(string EventName, string DataJson);

/// <summary>
/// Verteilt server-initiierte Ereignisse an offene <c>/api/events</c>-Streams, strikt pro User.
/// Singleton; Publisher (z.B. WatchJobNotifier) und Subscriber (SSE-Endpoint) sind entkoppelt.
/// Bounded Channel mit DropOldest: ein hängender Client staut keinen Speicher auf —
/// die UI lädt ohnehin per Query-Invalidierung nach, verlorene Events sind verschmerzbar.
/// </summary>
public sealed class ProactiveEventBroker
{
    private const int BufferPerSubscriber = 16;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ProactiveEvent>>> _subscribers = new();

    public Subscription Subscribe(string userId)
    {
        var channel = Channel.CreateBounded<ProactiveEvent>(new BoundedChannelOptions(BufferPerSubscriber)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        _subscribers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<ProactiveEvent>>())[id] = channel;
        return new Subscription(this, userId, id, channel.Reader);
    }

    /// <summary>Liefert die Zahl der Subscriber, die das Ereignis angenommen haben.</summary>
    public int Publish(string userId, ProactiveEvent ev)
    {
        if (!_subscribers.TryGetValue(userId, out var perUser)) return 0;
        var delivered = 0;
        foreach (var channel in perUser.Values)
        {
            if (channel.Writer.TryWrite(ev)) delivered++;
        }

        return delivered;
    }

    private void Unsubscribe(string userId, Guid id)
    {
        if (_subscribers.TryGetValue(userId, out var perUser) && perUser.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly ProactiveEventBroker _broker;
        private readonly string _userId;
        private readonly Guid _id;

        internal Subscription(ProactiveEventBroker broker, string userId, Guid id, ChannelReader<ProactiveEvent> reader)
        {
            _broker = broker;
            _userId = userId;
            _id = id;
            Reader = reader;
        }

        public ChannelReader<ProactiveEvent> Reader { get; }

        public void Dispose() => _broker.Unsubscribe(_userId, _id);
    }
}
```

- [ ] **Step 4: Build & Test.** Run: `dotnet test src/Backend.Tests --filter ProactiveEventBroker` — Expected: PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(backend): ProactiveEventBroker — per-User-Event-Streams für server-initiierte Nachrichten"`

## Task 5: `GET /api/events` (SSE) + Live-Publish beim Feuern

**Files:**
- Create: `src/Backend/Endpoints/EventsEndpoints.cs`
- Modify: `src/Backend/Program.cs` (Singleton + Map)
- Modify: `src/Backend/Features/WatchJobs/WatchJobNotifier.cs` (publiziert Events)
- Test: `src/Backend.Tests/Endpoints/EventsEndpointsTests.cs` *(neu)*; `WatchJobNotifierTests.cs` erweitern

**Interfaces:**
- Consumes: `ProactiveEventBroker` (Task 4), `IUserContext.UserId`, `MessageRepository.AddAsync → Task<Message>` (liefert die vergebene Id).
- Produces: SSE-Events `chat_message` (`{messageId, jobId}`) und `watch_job_fired` (`{jobId, title, summary}`) — Task 9 (Frontend-Hook) konsumiert exakt diese Namen.

- [ ] **Step 1: Failing Notifier-Test.** In `WatchJobNotifierTests.cs` — `BuildNotifier` um Broker/UserContext erweitern und neuen Test ergänzen:

```csharp
// BuildNotifier-Signatur erweitern:
private static WatchJobNotifier BuildNotifier(
    TempSqliteDb temp, UserContextHolder holder, MessageRepository messages, ProactiveEventBroker? broker = null)
{
    var push = new WebPushSender(
        new PushSubscriptionRepository(temp.AppDb, holder),
        new FakeSettingsRepo(),
        () => Now,
        NullLogger<WebPushSender>.Instance);
    return new WatchJobNotifier(
        new INotificationChannel[] { new WebPushChannel(push) },
        messages,
        broker ?? new ProactiveEventBroker(),
        holder,
        () => Now,
        NullLogger<WatchJobNotifier>.Instance);
}

[Fact]
public async Task NotifyAsync_PublishesProactiveEvents()
{
    using var temp = new TempSqliteDb();
    var holder = new UserContextHolder();
    var messages = new MessageRepository(temp.AppDb, holder);
    var broker = new ProactiveEventBroker();
    using var sub = broker.Subscribe(holder.UserId);
    var notifier = BuildNotifier(temp, holder, messages, broker);

    await notifier.NotifyAsync(
        SampleJob(channels: new[] { "webpush" }),
        new WatchJudgeResult(true, 0.9, Array.Empty<JudgeEvidence>(), "Treffer"),
        CancellationToken.None);

    var first = await sub.Reader.ReadAsync(CancellationToken.None);
    first.EventName.Should().Be("chat_message");
    var second = await sub.Reader.ReadAsync(CancellationToken.None);
    second.EventName.Should().Be("watch_job_fired");
    second.DataJson.Should().Contain("\"jobId\":7");
}
```

(`using NauAssist.Backend.Features.Events;` ergänzen; die bestehenden `BuildNotifier`-Aufrufe bleiben durch den optionalen Parameter gültig.)

- [ ] **Step 2: Notifier erweitern.** `WatchJobNotifier` — zwei neue Konstruktor-Parameter (`ProactiveEventBroker broker`, `IUserContext user`; usings `NauAssist.Backend.Features.Events`, `NauAssist.Backend.Features.Infrastructure.Auth`, `System.Text.Json`) und in `NotifyAsync`:

```csharp
private static readonly JsonSerializerOptions JsonOpts = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

// … in NotifyAsync: Rückgabewert von AddAsync verwenden:
var saved = await _messages.AddAsync(new Message(...wie bisher...), ct);

// Live an die offene PWA — vor den (potenziell langsamen) Push-Kanälen.
_broker.Publish(_user.UserId, new ProactiveEvent(
    "chat_message",
    JsonSerializer.Serialize(new { messageId = saved.Id, jobId = job.Id }, JsonOpts)));
_broker.Publish(_user.UserId, new ProactiveEvent(
    "watch_job_fired",
    JsonSerializer.Serialize(new { jobId = job.Id, title = job.Title, summary = result.Summary }, JsonOpts)));
```

- [ ] **Step 3: SSE-Endpoint.** `src/Backend/Endpoints/EventsEndpoints.cs`:

```csharp
using System.Text;
using NauAssist.Backend.Features.Events;
using NauAssist.Backend.Features.Infrastructure.Auth;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Schlanker SSE-Stream für server-initiierte Nachrichten an die offene PWA
/// (z.B. "Watch-Job gefeuert" ⇒ Chat-History live nachladen). Pro Verbindung eine
/// Broker-Subscription; ein Heartbeat-Kommentar alle 25 s hält Proxies die Verbindung offen.
/// </summary>
public static class EventsEndpoints
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (
            HttpContext ctx,
            ProactiveEventBroker broker,
            IUserContext user,
            CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            await ctx.Response.Body.FlushAsync(ct);

            using var subscription = broker.Subscribe(user.UserId);
            Task<ProactiveEvent>? pending = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    pending ??= subscription.Reader.ReadAsync(ct).AsTask();
                    var winner = await Task.WhenAny(pending, Task.Delay(Heartbeat, ct));

                    string frame;
                    if (winner == pending)
                    {
                        var ev = await pending;
                        pending = null;
                        frame = $"event: {ev.EventName}\ndata: {ev.DataJson}\n\n";
                    }
                    else
                    {
                        frame = ": ping\n\n";
                    }

                    await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client hat getrennt — normales Ende.
            }
        });

        return app;
    }
}
```

- [ ] **Step 4: Verdrahten.** `Program.cs`: `using NauAssist.Backend.Features.Events;`; bei den Singletons `builder.Services.AddSingleton<ProactiveEventBroker>();`; bei den Endpoint-Maps (ungeflaggt, direkt nach `app.MapCapabilitiesEndpoints();`): `app.MapEventsEndpoints();`.
- [ ] **Step 5: Endpoint-Test.** `src/Backend.Tests/Endpoints/EventsEndpointsTests.cs`:

```csharp
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Features.Events;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class EventsEndpointsTests
{
    [Fact]
    public async Task EventsStream_DeliversPublishedEventForOwnUser()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await client.GetAsync(
            "/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        // Default-User ermitteln (anonyme Requests laufen als dieser User).
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            userId = scope.ServiceProvider.GetRequiredService<IUserContext>().UserId;
        }

        // Bis der Endpoint subscribed hat, kann Publish ins Leere gehen — kurz nachliefern.
        var broker = factory.Services.GetRequiredService<ProactiveEventBroker>();
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 100 && !cts.IsCancellationRequested; i++)
            {
                if (broker.Publish(userId, new ProactiveEvent("chat_message", """{"messageId":42}""")) > 0) return;
                await Task.Delay(50, cts.Token);
            }
        }, cts.Token);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var received = new StringBuilder();
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            received.AppendLine(line);
            if (received.ToString().Contains("\"messageId\":42")) break;
        }

        received.ToString().Should().Contain("event: chat_message").And.Contain("\"messageId\":42");
    }
}
```

- [ ] **Step 6: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS.
- [ ] **Step 7: Commit.** `git commit -m "feat(backend): GET /api/events — SSE-Stream + Live-Publish beim Watch-Job-Feuern"`

## Task 6: Hot-Mode-Kadenz (`partialSignal`)

**Files:**
- Modify: `src/Backend/Features/WatchJobs/WatchJudgeResult.cs`, `WatchJudge.cs`, `WatchJobOptions.cs`, `WatchJobExecutor.cs`
- Modify: `src/Backend/appsettings.json`
- Test: `src/Backend.Tests/Features/WatchJobs/WatchJobExecutorTests.cs` (erweitern)

**Interfaces:**
- Produces: `WatchJudgeResult` bekommt `bool PartialSignal = false` (optionaler Record-Parameter ⇒ bestehende Aufrufe bleiben gültig); `WatchJobOptions.HotIntervalSeconds` (default 15).

- [ ] **Step 1: Failing Test.** In `WatchJobExecutorTests.cs` ergänzen:

```csharp
private const string PartialSignalJson =
    """{"met":false,"confidence":0.3,"evidence":[],"summary":"Suchindex zeigt Treffer, Produktseite noch ausverkauft","partialSignal":true}""";

[Fact]
public async Task PartialSignal_SwitchesToHotInterval()
{
    var llm = new FakeLlmClient();
    llm.QueueResponse(new TextDeltaChunk(PartialSignalJson));
    var executor = BuildExecutor(llm);

    var outcome = await executor.RunOnceAsync(SampleJob(), CancellationToken.None);

    outcome.Fired.Should().BeFalse();
    outcome.Status.Should().Be(WatchJobStatus.Active);
    // Hot-Mode: 15 s + Jitter (≤ 3 s) statt Basis-Intervall 60 s.
    outcome.NextDueAt.Should().BeOnOrAfter(Now.AddSeconds(15));
    outcome.NextDueAt.Should().BeOnOrBefore(Now.AddSeconds(18));
}
```

In `BuildExecutor` die Options um `HotIntervalSeconds = 15` ergänzen (explizit, damit der Test nicht am Default hängt).

- [ ] **Step 2: Run test to verify it fails.** Run: `dotnet test src/Backend.Tests --filter PartialSignal` — Expected: FAIL (NextDueAt ≈ Now+60, da `partialSignal` noch unbekannt).
- [ ] **Step 3: Judge-Result + Prompt + Parsing.**

`WatchJudgeResult.cs`:

```csharp
public sealed record WatchJudgeResult(
    bool Met,
    double Confidence,
    IReadOnlyList<JudgeEvidence> Evidence,
    string Summary,
    bool PartialSignal = false);
```

`WatchJudge.cs` — im Schema-Block von `BuildSystemPrompt` vor der `summary`-Zeile ergänzen:

```csharp
sb.AppendLine("  \"partialSignal\": boolean,        // true, wenn es unbestätigte Teil-Hinweise gibt (z.B. Suchtreffer deutet Verfügbarkeit an, die Produktseite bestätigt es nicht)");
```

Und in `EvaluateAsync` beim Konstruieren des Results:

```csharp
return new WatchJudgeResult(
    Met: GetBool(root, "met"),
    Confidence: GetDouble(root, "confidence") ?? 0.0,
    Evidence: ParseEvidence(root),
    Summary: GetString(root, "summary") ?? "",
    PartialSignal: GetBool(root, "partialSignal"));
```

- [ ] **Step 4: Option + Executor.**

`WatchJobOptions.cs`:

```csharp
/// <summary>
/// Hot-Mode-Intervall: greift, wenn der Judge ein Teilsignal meldet (etwas tut sich,
/// ist aber unbestätigt). Darf MinIntervalSeconds bewusst unterschreiten; harte Untergrenze 10 s.
/// </summary>
public int HotIntervalSeconds { get; set; } = 15;
```

`WatchJobExecutor.cs` — im „Kein (sicherer) Treffer"-Zweig (letztes `return` in `RunOnceAsync`):

```csharp
// Kein (sicherer) Treffer ⇒ weiterbeobachten. Ein Teilsignal verdichtet die Kadenz
// (Hot-Mode), sonst greift das exponentielle Backoff.
return new ExecutionOutcome(
    Fired: false,
    Status: WatchJobStatus.Active,
    NextDueAt: judgeResult.PartialSignal ? HotNextDueAt(now) : NextDueAt(job, now, backoff: true),
    CheckedAt: now,
    CheckCount: checkCount,
    ConsecutiveErrors: 0,
    ResultJson: resultJson,
    FiredHash: job.FiredHash,
    JudgeResult: judgeResult);
```

Neue private Methode neben `NextDueAt`:

```csharp
/// <summary>
/// Hot-Mode: bei einem Teilsignal kurzzeitig eng pollen. Bewusst unterhalb von
/// MinIntervalSeconds erlaubt (harte Untergrenze 10 s), weil sich der Zustand gerade ändert;
/// sobald das Teilsignal wegfällt, greift wieder das normale Backoff.
/// </summary>
private DateTimeOffset HotNextDueAt(DateTimeOffset now)
{
    var hot = Math.Max(10, _options.HotIntervalSeconds);
    var jitter = Random.Shared.Next(0, Math.Max(1, hot / 5) + 1);
    return now.AddSeconds(hot + jitter);
}
```

`appsettings.json` — im Block `AutonomousAgent:WatchJobs` nach `MinIntervalSeconds` ergänzen: `"HotIntervalSeconds": 15,`.

- [ ] **Step 5: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS (bestehende Executor-Tests bleiben durch den optionalen Record-Parameter grün).
- [ ] **Step 6: Commit.** `git commit -m "feat(backend): Hot-Mode-Kadenz — Teilsignal des Judge verdichtet das Prüfintervall"`

## Task 7: Verwaltungs-API (Gesamtliste + pause/resume/cancel)

**Files:**
- Modify: `src/Backend/Features/WatchJobs/WatchJobRepository.cs` (`ListByUserAsync`)
- Modify: `src/Backend/Endpoints/WatchJobsEndpoints.cs`
- Modify: `src/Backend/Features/WatchJobs/Tools/CancelWatchJobTool.cs` (`mode: resume`)
- Test: `WatchJobRepositoryTests.cs`, `WatchJobsEndpointsTests.cs`, `WatchJobToolsTests.cs` (jeweils erweitern)

**Interfaces:**
- Produces: `WatchJobRepository.ListByUserAsync(int limit, ct) → IReadOnlyList<WatchJob>` (alle Status, neueste zuerst); REST `POST /api/watch-jobs/{id}/pause|resume|cancel` → 204/404. Task 9/10 (Frontend) konsumieren genau diese Routen.
- Consumes: `SetStatusAsync(long id, WatchJobStatus status, string? firedHash, ct) → Task<bool>` (bestehend, user-gescoped).

- [ ] **Step 1: Failing Repo-Test.** In `WatchJobRepositoryTests.cs` ergänzen (hat die Datei bereits einen passenden Insert-Helper, diesen statt `InsertTitledAsync` verwenden):

```csharp
[Fact]
public async Task ListByUserAsync_ReturnsAllStatusesNewestFirst_ScopedToUser()
{
    using var temp = new TempSqliteDb();
    var holder = new UserContextHolder();
    var repo = new WatchJobRepository(temp.AppDb, holder);

    var a = await InsertTitledAsync(repo, "A-Job", now: DateTimeOffset.Parse("2026-07-06T10:00:00Z"));
    await repo.SetStatusAsync(a.Id, WatchJobStatus.Completed, firedHash: null, CancellationToken.None);
    await InsertTitledAsync(repo, "B-Job", now: DateTimeOffset.Parse("2026-07-06T11:00:00Z"));

    var otherUser = new UserContextHolder();
    otherUser.Set("user-c");
    await InsertTitledAsync(new WatchJobRepository(temp.AppDb, otherUser), "C-Job",
        now: DateTimeOffset.Parse("2026-07-06T12:00:00Z"));

    var jobs = await repo.ListByUserAsync(100, CancellationToken.None);

    jobs.Select(j => j.Title).Should().ContainInOrder("B-Job", "A-Job").And.NotContain("C-Job");
    jobs.Should().Contain(j => j.Status == WatchJobStatus.Completed);
}

private static Task<WatchJob> InsertTitledAsync(WatchJobRepository repo, string title, DateTimeOffset now)
    => repo.InsertAsync(
        title: title,
        goal: "Ziel",
        kind: WatchJobKind.WebAvailability,
        spec: new WatchJobSpec(new[] { "q" }, Array.Empty<string>(), "frage?", "kriterium"),
        schedule: new WatchJobSchedule(60, 1800),
        notify: new WatchJobNotify(new[] { "webpush" }, FireOnce: true),
        budget: new WatchJobBudget(null, null),
        nextDueAt: now.AddMinutes(5),
        now: now,
        CancellationToken.None);
```

- [ ] **Step 2: Repository.** In `WatchJobRepository.cs` neben `ListActiveByUserAsync`:

```csharp
/// <summary>Alle Jobs des Users (alle Status), neueste zuerst — Grundlage der Watcher-UI.</summary>
public async Task<IReadOnlyList<WatchJob>> ListByUserAsync(int limit, CancellationToken ct)
{
    using var conn = _db.OpenConnection();
    var rows = await conn.QueryAsync<WatchJobRow>(new CommandDefinition(
        $"SELECT {SelectColumns} FROM watch_jobs " +
        "WHERE user_id = @userId ORDER BY created_at DESC, id DESC LIMIT @limit;",
        new { userId = _user.UserId, limit },
        cancellationToken: ct));
    return rows.Select(MapToDomain).ToList();
}
```

- [ ] **Step 3: Failing Endpoint-Test.** In `WatchJobsEndpointsTests.cs` (Factory-Aufbau wie der bestehende Test der Datei):

```csharp
[Fact]
public async Task PauseResumeCancel_ChangeStatus_AndUnknownIdReturns404()
{
    using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("AutonomousAgent:WatchJobs:Enabled", "true");
        builder.UseSetting("AutonomousAgent:WatchJobs:TickSeconds", "3600");
    });
    var client = factory.CreateClient();

    WatchJob job;
    using (var scope = factory.Services.CreateScope())
    {
        job = await InsertJobAsync(scope.ServiceProvider.GetRequiredService<WatchJobRepository>(), "Steuerbar");
    }

    (await client.PostAsync($"/api/watch-jobs/{job.Id}/pause", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await GetStatusAsync(client, job.Id)).Should().Be("paused");

    (await client.PostAsync($"/api/watch-jobs/{job.Id}/resume", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await GetStatusAsync(client, job.Id)).Should().Be("active");

    (await client.PostAsync($"/api/watch-jobs/{job.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await GetStatusAsync(client, job.Id)).Should().Be("completed");

    (await client.PostAsync("/api/watch-jobs/999999/pause", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
}

private static async Task<string?> GetStatusAsync(HttpClient client, long id)
{
    var jobs = await (await client.GetAsync("/api/watch-jobs")).Content.ReadFromJsonAsync<List<JsonElement>>();
    return jobs!.Single(j => j.GetProperty("id").GetInt64() == id).GetProperty("status").GetString();
}
```

Wichtig: der `cancel`-Schritt setzt voraus, dass `GET /api/watch-jobs` jetzt **alle** Status liefert (Step 4) — vorher schlägt der Test fehl. Genau das ist der failing test.

- [ ] **Step 4: Endpoints.** `WatchJobsEndpoints.cs` komplett:

```csharp
using NauAssist.Backend.Features.WatchJobs;

namespace NauAssist.Backend.Endpoints;

/// <summary>
/// Watch-Jobs für die Watcher-UI: Gesamtliste (alle Status) plus Statuswechsel
/// pause/resume/cancel. Das Anlegen läuft weiterhin über das Chat-Tool create_watch_job.
/// </summary>
public static class WatchJobsEndpoints
{
    public static IEndpointRouteBuilder MapWatchJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/watch-jobs");

        group.MapGet("/", async (WatchJobRepository repo, CancellationToken ct) =>
        {
            // Alle Status — die UI zeigt auch erledigte/pausierte Watcher.
            var items = await repo.ListByUserAsync(100, ct);
            return Results.Ok(items.Select(ToDto));
        });

        group.MapPost("/{id:long}/pause", (long id, WatchJobRepository repo, CancellationToken ct)
            => SetStatusAsync(repo, id, WatchJobStatus.Paused, ct));
        group.MapPost("/{id:long}/resume", (long id, WatchJobRepository repo, CancellationToken ct)
            => SetStatusAsync(repo, id, WatchJobStatus.Active, ct));
        group.MapPost("/{id:long}/cancel", (long id, WatchJobRepository repo, CancellationToken ct)
            => SetStatusAsync(repo, id, WatchJobStatus.Completed, ct));

        return app;
    }

    private static async Task<IResult> SetStatusAsync(
        WatchJobRepository repo, long id, WatchJobStatus status, CancellationToken ct)
    {
        var ok = await repo.SetStatusAsync(id, status, firedHash: null, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    // ToDto + WatchJobDto unverändert aus der bestehenden Datei übernehmen.
}
```

- [ ] **Step 5: Chat-Tool `resume`.** `CancelWatchJobTool.cs`: Schema-Enum auf `["cancel", "pause", "resume"]` erweitern, Description ergänzen (`resume = pausierten Job fortsetzen`), im Mode-Mapping:

```csharp
else if (string.Equals(mode, "resume", StringComparison.OrdinalIgnoreCase))
{
    newStatus = WatchJobStatus.Active;
}
```

Und in `WatchJobToolsTests.cs` einen Test ergänzen: pausieren → `mode: "resume"` → Status `active` (Muster des bestehenden Pause-Tests, Zeile ~146).

- [ ] **Step 6: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS.
- [ ] **Step 7: Commit.** `git commit -m "feat(backend): Watch-Job-Verwaltung — Gesamtliste + pause/resume/cancel (REST & Chat-Tool)"`

## Task 8: Capabilities-Flag `watchJobs`

**Files:**
- Modify: `src/Backend/Endpoints/CapabilitiesEndpoints.cs`
- Test: `src/Backend.Tests/Endpoints/CapabilitiesEndpointTests.cs` (erweitern)

- [ ] **Step 1: Failing Test.** In `CapabilitiesEndpointTests.cs` das `CapsDto` erweitern und zwei Tests ergänzen:

```csharp
private sealed record CapsDto(bool WhatsApp, bool WatchJobs);

[Fact]
public async Task Capabilities_DefaultsToWatchJobsDisabled()
{
    var client = _factory.CreateClient();
    var dto = await (await client.GetAsync("/api/capabilities")).Content.ReadFromJsonAsync<CapsDto>();
    dto!.WatchJobs.Should().BeFalse();
}

[Fact]
public async Task Capabilities_ReflectsWatchJobsFlag()
{
    var factory = _factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("AutonomousAgent:WatchJobs:Enabled", "true");
        builder.UseSetting("AutonomousAgent:WatchJobs:TickSeconds", "3600");
    });
    var dto = await (await factory.CreateClient().GetAsync("/api/capabilities")).Content.ReadFromJsonAsync<CapsDto>();
    dto!.WatchJobs.Should().BeTrue();
}
```

- [ ] **Step 2: Run test to verify it fails.** Run: `dotnet test src/Backend.Tests --filter Capabilities` — Expected: FAIL (`WatchJobs` immer false, Feld fehlt in Response).
- [ ] **Step 3: Endpoint.** `CapabilitiesEndpoints.cs` (`using NauAssist.Backend.Features.WatchJobs;`):

```csharp
app.MapGet("/api/capabilities", (
        IOptions<WhatsAppOptions> whatsApp,
        IOptions<AuthOptions> auth,
        IOptions<WatchJobOptions> watchJobs) =>
    Results.Ok(new CapabilitiesDto(
        whatsApp.Value.Enabled,
        new AuthCapabilitiesDto(auth.Value.Enabled, AuthEndpoints.LoginPath),
        watchJobs.Value.Enabled)))
    .AllowAnonymous();

private sealed record CapabilitiesDto(bool WhatsApp, AuthCapabilitiesDto Auth, bool WatchJobs);
```

- [ ] **Step 4: Build & Test.** Run: `dotnet test src/Backend.Tests` — Expected: PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(backend): Capabilities-Flag watchJobs fürs Frontend"`

## Task 9: Frontend — API-Layer, Query-Keys & Live-Event-Hook

**Files:**
- Create: `src/frontend/src/api/watch-jobs.ts`, `src/frontend/src/api/pushover.ts`, `src/frontend/src/hooks/useProactiveEvents.ts`
- Modify: `src/frontend/src/api/capabilities.ts`, `src/frontend/src/hooks/queries.ts`, `src/frontend/src/App.tsx` (Hook mounten), `src/frontend/src/components/pages/SettingsPage.tsx` (Capabilities-Fallback)

- [ ] **Step 1: Capabilities-Typ.** `api/capabilities.ts` — `watchJobs: boolean;` ins `Capabilities`-Interface. In `SettingsPage.tsx` das Fallback-Objekt (`capsQuery.isError`-Zweig) um `watchJobs: false` ergänzen.
- [ ] **Step 2: `api/watch-jobs.ts`:**

```ts
export type WatchJobStatus =
  | "active"
  | "paused"
  | "fired"
  | "completed"
  | "failed"
  | "expired";

export interface WatchJobDto {
  id: number;
  title: string;
  goal: string;
  kind: string;
  status: WatchJobStatus;
  checkCount: number;
  lastCheckedAt: string | null;
  nextDueAt: string;
  lastSummary: string | null;
  createdAt: string;
}

export async function listWatchJobs(): Promise<WatchJobDto[]> {
  const res = await fetch("/api/watch-jobs");
  if (!res.ok) throw new Error(`Watch-Jobs-Load fehlgeschlagen: HTTP ${res.status}`);
  return (await res.json()) as WatchJobDto[];
}

async function postAction(id: number, action: "pause" | "resume" | "cancel"): Promise<void> {
  const res = await fetch(`/api/watch-jobs/${id}/${action}`, { method: "POST" });
  if (!res.ok) throw new Error(`${action} fehlgeschlagen: HTTP ${res.status}`);
}

export const pauseWatchJob = (id: number) => postAction(id, "pause");
export const resumeWatchJob = (id: number) => postAction(id, "resume");
export const cancelWatchJob = (id: number) => postAction(id, "cancel");
```

- [ ] **Step 3: `api/pushover.ts`:**

```ts
export interface PushoverSettings {
  hasToken: boolean;
  hasUserKey: boolean;
}

export async function getPushoverSettings(): Promise<PushoverSettings> {
  const res = await fetch("/api/settings/pushover");
  if (!res.ok) throw new Error(`Pushover-Settings-Load fehlgeschlagen: HTTP ${res.status}`);
  return (await res.json()) as PushoverSettings;
}

export async function updatePushoverSettings(token: string, userKey: string): Promise<void> {
  const res = await fetch("/api/settings/pushover", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token, userKey }),
  });
  if (!res.ok) throw new Error(`Pushover-Settings-Save fehlgeschlagen: HTTP ${res.status}`);
}
```

- [ ] **Step 4: Query-Keys.** `hooks/queries.ts` — im `queryKeys`-Objekt ergänzen:

```ts
watchJobs: ["watch-jobs"] as const,
```

- [ ] **Step 5: `hooks/useProactiveEvents.ts`:**

```ts
import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { fetchEventSource } from "@microsoft/fetch-event-source";
import { queryKeys } from "@/hooks/queries";

/**
 * Lauscht auf den server-initiierten Event-Stream (/api/events) und hält die
 * betroffenen Queries frisch: proaktive Chat-Nachrichten und feuernde Watch-Jobs
 * erscheinen live in der offenen PWA, ohne Polling.
 */
export function useProactiveEvents() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const ctrl = new AbortController();

    void fetchEventSource("/api/events", {
      signal: ctrl.signal,
      // Auch im Hintergrund-Tab verbunden bleiben — ergänzt Web-Push, ersetzt ihn nicht.
      openWhenHidden: true,
      onmessage(ev) {
        if (ev.event === "chat_message") {
          void queryClient.invalidateQueries({ queryKey: queryKeys.chatHistory });
        } else if (ev.event === "watch_job_fired") {
          void queryClient.invalidateQueries({ queryKey: queryKeys.watchJobs });
          void queryClient.invalidateQueries({ queryKey: queryKeys.chatHistory });
        }
      },
      onerror() {
        // undefined zurückgeben ⇒ eingebautes Retry mit Backoff übernimmt.
      },
    });

    return () => ctrl.abort();
  }, [queryClient]);
}
```

- [ ] **Step 6: Hook mounten.** In `App.tsx` innerhalb der `App`-Komponente (nach den useState-Deklarationen): `useProactiveEvents();` + Import.
- [ ] **Step 7: Verifikation.** In `src/frontend`: Run: `npm run typecheck && npm run lint && npm run build` — Expected: alle grün.
- [ ] **Step 8: Commit.** `git commit -m "feat(frontend): Watch-Jobs-/Pushover-API + Live-Event-Hook für /api/events"`

## Task 10: Frontend — Watcher-Seite + Tab-Navigation

**Files:**
- Create: `src/frontend/src/components/pages/WatchersPage.tsx`
- Modify: `src/frontend/src/App.tsx`, `src/frontend/src/components/nau/Layout.tsx`, `src/frontend/src/components/nau/MobileTabBar.tsx`

**Interfaces:**
- Consumes: `listWatchJobs`/`pauseWatchJob`/`resumeWatchJob`/`cancelWatchJob` + `queryKeys.watchJobs` (Task 9), `Capabilities.watchJobs` (Task 8/9).

- [ ] **Step 1: Navigation erweitern.**

`App.tsx`:
- `export type AppPage = "chat" | "calendar" | "recommendations" | "watchers" | "settings";`
- `PAGE_ORDER`: `{ chat: 0, calendar: 1, recommendations: 2, watchers: 3, settings: 4 }`.
- Capabilities laden (Import `useQuery`, `queryKeys`, `getCapabilities`):

```tsx
const capsQuery = useQuery({ queryKey: queryKeys.capabilities, queryFn: getCapabilities });
const watchersEnabled = capsQuery.data?.watchJobs ?? false;
```

- Render-Zweig in der `Layout`-Rückgabe ergänzen und Prop durchreichen:

```tsx
<Layout current={page} onNavigate={navigate} watchersEnabled={watchersEnabled}>
  <div key={page} className={"h-full min-h-0 " + animClass}>
    {page === "calendar" ? (
      <CalendarPage onNavigate={navigate} />
    ) : page === "recommendations" ? (
      <RecommendationsPage
        focusSuggestionId={focusSuggestionId}
        onFocusHandled={() => setFocusSuggestionId(null)}
      />
    ) : page === "watchers" ? (
      <WatchersPage />
    ) : (
      <ChatView onNavigate={navigate} />
    )}
  </div>
</Layout>
```

`Layout.tsx`: `type TabKey = "chat" | "calendar" | "recommendations" | "watchers";`, Prop `watchersEnabled?: boolean` annehmen und an `<MobileTabBar current={current} onSelect={onNavigate} watchersEnabled={watchersEnabled} />` durchreichen.

`MobileTabBar.tsx`: `TabKey` ebenso erweitern; `Radar` aus `lucide-react` importieren; Tabs dynamisch bauen und **alle** `TABS`-Verwendungen (activeIndex, tabWidth, map) auf `tabs` umstellen:

```tsx
const BASE_TABS: TabDef[] = [
  { key: "chat", label: "CHAT", aria: "Chat", Icon: MessageSquare },
  { key: "calendar", label: "KALENDER", aria: "Kalender", Icon: CalendarDays },
  { key: "recommendations", label: "EMPF.", aria: "Empfehlungen", Icon: Sparkles },
];

const WATCHERS_TAB: TabDef = { key: "watchers", label: "WATCH", aria: "Watcher", Icon: Radar };

interface MobileTabBarProps {
  current: TabKey;
  onSelect: (page: AppPage) => void;
  /** Capabilities-gated: Tab nur zeigen, wenn Watch-Jobs am Backend aktiv sind. */
  watchersEnabled?: boolean;
}

export function MobileTabBar({ current, onSelect, watchersEnabled = false }: MobileTabBarProps) {
  const tabs = watchersEnabled ? [...BASE_TABS, WATCHERS_TAB] : BASE_TABS;
  const activeIndex = tabs.findIndex((t) => t.key === current);
  const tabWidth = 100 / tabs.length;
  // … Rest unverändert, nur TABS → tabs.
}
```

- [ ] **Step 2: `WatchersPage.tsx`:**

```tsx
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/hooks/queries";
import { PageLoader } from "@/components/nau/PageLoader";
import {
  cancelWatchJob,
  listWatchJobs,
  pauseWatchJob,
  resumeWatchJob,
  type WatchJobDto,
  type WatchJobStatus,
} from "@/api/watch-jobs";

const STATUS_LABEL: Record<WatchJobStatus, string> = {
  active: "AKTIV",
  paused: "PAUSIERT",
  fired: "GEFEUERT",
  completed: "ERLEDIGT",
  failed: "FEHLER",
  expired: "ABGELAUFEN",
};

function statusClasses(status: WatchJobStatus): string {
  switch (status) {
    case "active":
      return "border-nau-accent/40 text-nau-accent";
    case "paused":
      return "border-nau-line text-nau-fg-dim";
    case "fired":
    case "completed":
      return "border-emerald-500/40 text-emerald-400";
    default:
      return "border-red-500/40 text-red-400";
  }
}

function formatTime(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("de-DE", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * Watcher-Übersicht: laufende/erledigte Hintergrund-Beobachtungen mit letztem Befund.
 * Angelegt wird per Chat ("sag mir, wenn …"); hier gibt es Pause/Weiter/Stop.
 */
export function WatchersPage() {
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const jobsQuery = useQuery({
    queryKey: queryKeys.watchJobs,
    queryFn: listWatchJobs,
    // Fallback-Polling, falls der /api/events-Stream mal nicht verbunden ist.
    refetchInterval: 60_000,
  });

  const run = async (action: (id: number) => Promise<void>, id: number) => {
    setError(null);
    try {
      await action(id);
      await queryClient.invalidateQueries({ queryKey: queryKeys.watchJobs });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  if (jobsQuery.isPending) return <PageLoader />;

  const jobs = jobsQuery.data ?? [];

  return (
    <div className="h-full overflow-y-auto px-5 py-6 lg:px-12 lg:py-8">
      <h1 className="m-0 mb-1 font-sans text-3xl font-semibold text-nau-fg">Watcher</h1>
      <p className="mb-6 font-mono text-[11px] tracking-mono text-nau-fg-dim">
        {"// HINTERGRUND-BEOBACHTUNGEN · ANLEGEN PER CHAT („sag mir, wenn …“)"}
      </p>

      {jobsQuery.isError && (
        <div className="mb-4 border border-red-500/40 bg-red-500/10 px-4 py-3 font-mono text-[12px] text-red-400">
          {jobsQuery.error instanceof Error ? jobsQuery.error.message : "Laden fehlgeschlagen"}
        </div>
      )}
      {error && (
        <div className="mb-4 border border-red-500/40 bg-red-500/10 px-4 py-3 font-mono text-[12px] text-red-400">
          {error}
        </div>
      )}

      {jobs.length === 0 ? (
        <div className="border border-nau-line bg-nau-bg-alt p-10 text-center font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {"// KEINE WATCHER — IM CHAT ANLEGEN"}
        </div>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-4 p-0">
          {jobs.map((job: WatchJobDto) => (
            <li key={job.id} className="border border-nau-line bg-nau-bg-alt p-5">
              <div className="mb-2 flex flex-wrap items-center gap-3">
                <span className="font-sans text-base font-semibold text-nau-fg">{job.title}</span>
                <span
                  className={
                    "border px-2 py-0.5 font-mono text-[10px] tracking-mono-wide " +
                    statusClasses(job.status)
                  }
                >
                  {STATUS_LABEL[job.status]}
                </span>
              </div>

              <p className="mb-3 text-sm text-nau-fg-dim">{job.goal}</p>

              {job.lastSummary && (
                <p className="mb-3 border-l-2 border-nau-line pl-3 text-sm text-nau-fg">
                  {job.lastSummary}
                </p>
              )}

              <div className="mb-4 flex flex-wrap gap-x-5 gap-y-1 font-mono text-[11px] tracking-mono text-nau-fg-dim">
                <span>CHECKS {job.checkCount}</span>
                <span>ZULETZT {formatTime(job.lastCheckedAt)}</span>
                {job.status === "active" && <span>NÄCHSTER {formatTime(job.nextDueAt)}</span>}
              </div>

              <div className="flex gap-3">
                {job.status === "active" && (
                  <>
                    <ActionButton label="PAUSE" onClick={() => run(pauseWatchJob, job.id)} />
                    <ActionButton label="STOP" onClick={() => run(cancelWatchJob, job.id)} />
                  </>
                )}
                {job.status === "paused" && (
                  <>
                    <ActionButton label="WEITER" onClick={() => run(resumeWatchJob, job.id)} />
                    <ActionButton label="STOP" onClick={() => run(cancelWatchJob, job.id)} />
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ActionButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="cursor-pointer border border-nau-line bg-transparent px-4 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-accent hover:text-nau-accent"
    >
      {label}
    </button>
  );
}
```

- [ ] **Step 3: Verifikation.** In `src/frontend`: Run: `npm run typecheck && npm run lint && npm run build` — Expected: grün. Manuell (optional): Backend mit `AutonomousAgent:WatchJobs:Enabled=true` starten ⇒ vierter Tab „WATCH" erscheint; mit Flag aus ⇒ drei Tabs wie bisher.
- [ ] **Step 4: Commit.** `git commit -m "feat(frontend): Watcher-Seite + Tab-Navigation (capabilities-gated)"`

## Task 11: Frontend — Pushover-Settings-Section

**Files:**
- Create: `src/frontend/src/components/settings/PushoverSection.tsx`
- Modify: `src/frontend/src/components/pages/SettingsPage.tsx`

- [ ] **Step 1: Section-Komponente.** `PushoverSection.tsx`:

```tsx
import { useEffect, useState } from "react";
import { getPushoverSettings, updatePushoverSettings } from "@/api/pushover";

interface PushoverSectionProps {
  anchor: string;
}

/**
 * Pushover-Zugangsdaten (App-Token + User-Key von pushover.net). Der Server gibt
 * Secrets nie zurück — angezeigt wird nur, ob sie hinterlegt sind; die Felder
 * dienen ausschließlich dem (Über-)Schreiben.
 */
export function PushoverSection({ anchor }: PushoverSectionProps) {
  const [configured, setConfigured] = useState<boolean | null>(null);
  const [token, setToken] = useState("");
  const [userKey, setUserKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const s = await getPushoverSettings();
        setConfigured(s.hasToken && s.hasUserKey);
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
        setConfigured(false);
      }
    })();
  }, []);

  const save = async () => {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      await updatePushoverSettings(token.trim(), userKey.trim());
      const isSet = token.trim().length > 0 && userKey.trim().length > 0;
      setConfigured(isSet);
      setToken("");
      setUserKey("");
      setInfo(isSet ? "Pushover-Zugangsdaten gespeichert." : "Pushover-Zugangsdaten entfernt.");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const clear = async () => {
    setToken("");
    setUserKey("");
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      await updatePushoverSettings("", "");
      setConfigured(false);
      setInfo("Pushover-Zugangsdaten entfernt.");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const inputClass =
    "w-full border border-nau-line bg-nau-bg px-3 py-2 font-mono text-sm text-nau-fg " +
    "placeholder:text-nau-fg-dim focus:border-nau-accent focus:outline-none";

  return (
    <section id={anchor} className="flex flex-col gap-4">
      <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">
        {configured === null
          ? "// STATUS: LADE …"
          : configured
            ? "// STATUS: KONFIGURIERT — Watch-Jobs können über Pushover melden"
            : "// STATUS: NICHT KONFIGURIERT — Token & User-Key von pushover.net eintragen"}
      </div>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">APP-TOKEN</span>
        <input
          type="password"
          value={token}
          onChange={(e) => setToken(e.target.value)}
          placeholder="a1b2c3…"
          autoComplete="off"
          className={inputClass}
        />
      </label>

      <label className="flex flex-col gap-1">
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">USER-KEY</span>
        <input
          type="password"
          value={userKey}
          onChange={(e) => setUserKey(e.target.value)}
          placeholder="u9x8y7…"
          autoComplete="off"
          className={inputClass}
        />
      </label>

      <div className="flex gap-3">
        <button
          type="button"
          disabled={busy || token.trim().length === 0 || userKey.trim().length === 0}
          onClick={() => void save()}
          className="cursor-pointer border border-nau-accent px-4 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-accent transition-colors hover:bg-nau-accent hover:text-nau-bg disabled:cursor-not-allowed disabled:opacity-40"
        >
          SPEICHERN
        </button>
        {configured && (
          <button
            type="button"
            disabled={busy}
            onClick={() => void clear()}
            className="cursor-pointer border border-nau-line px-4 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-red-500/60 hover:text-red-400 disabled:opacity-40"
          >
            ENTFERNEN
          </button>
        )}
      </div>

      {info && <p className="font-mono text-[11px] text-emerald-400">{info}</p>}
      {error && <p className="font-mono text-[11px] text-red-400">{error}</p>}
    </section>
  );
}
```

- [ ] **Step 2: In SettingsPage einhängen.** `SettingsPage.tsx`:
- `type SectionKey = "llm" | "calendar" | "persona" | "push" | "pushover" | "imap" | "whatsapp" | "konto";`
- `NAV_META` ergänzen (Icon `BellRing` aus `lucide-react` importieren): `pushover: { label: "Pushover", hint: "Push aufs Handy (extern)", Icon: BellRing },`
- In `buildGroups` die Kanäle erweitern: `const channels: SectionKey[] = ["push", "pushover", "imap"];`
- In `sectionContent`: `case "pushover": return <PushoverSection anchor="section-pushover" />;` + Import.
- [ ] **Step 3: Verifikation.** In `src/frontend`: Run: `npm run typecheck && npm run lint && npm run build` — Expected: grün.
- [ ] **Step 4: Commit.** `git commit -m "feat(frontend): Pushover-Settings-Section"`

---

## Verifikation (Definition of Done, Phase 2)

- [ ] `dotnet build src/Backend` + `dotnet test src/Backend.Tests` grün; in `src/frontend` `npm run typecheck && npm run lint && npm run build` grün.
- [ ] Pushover in den Settings konfiguriert + Job mit `channels: ["webpush","pushover"]`: simulierter Treffer ⇒ Web-Push **und** Pushover-Nachricht, genau einmal.
- [ ] PWA offen, Watch-Job feuert ⇒ proaktive Chat-Nachricht erscheint **ohne Reload** (via `/api/events`); Watcher-Liste aktualisiert sich live.
- [ ] Watcher-Tab: nur sichtbar mit `AutonomousAgent:WatchJobs:Enabled=true`; Pause/Weiter/Stop wirken (Status wechselt, pausierte Jobs tickt der Scheduler nicht).
- [ ] Judge meldet `partialSignal=true` ⇒ `next_due_at` springt auf ~15 s (Hot-Mode); ohne Teilsignal weiterhin Backoff.
- [ ] Mit Flag aus: keine Watch-Job-Tools/-Endpoints, drei Tabs wie bisher; `/api/events` und Pushover-Settings existieren, stören aber nichts.

## Danach (Phase 3, separater Plan)

Weitere Skill-Kinds (Preis-Schwelle, RSS/Feed, generisches `web_change` mit Diff), Quiet-Hours im Schedule, Domain-Allowlist, ggf. Pushover-Priorities/Sounds pro Job.
