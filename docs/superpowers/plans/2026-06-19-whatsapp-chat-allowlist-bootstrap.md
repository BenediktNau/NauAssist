# WhatsApp-Allowlist Bootstrap — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WhatsApp-Chats lassen sich aktiv in die Allowlist holen (Gruppen automatisch, Einzelpersonen per Telefonnummer), das Allowlist-Matching wird gegen JID-Formatvarianten robust, und entdeckte Chats überleben einen Sidecar-Neustart — damit aus „connected, aber keine Suggestions" wieder Suggestions werden.

**Architecture:** Vier gestaffelte, je für sich baubare Schritte. **C** (Backend) macht den Allowlist-Vergleich JID-robust. **A** (Sidecar) lädt Gruppen aktiv per `groupFetchAllParticipating()`. **B** (Sidecar→Backend→Frontend) ergänzt manuelle 1:1-Eingabe per Nummer via `onWhatsApp()` und speichert beide JID-Formen. **D** (Sidecar) persistiert die Chat-Map in `buffer.db`.

**Tech Stack:** .NET 8 / xUnit / FluentAssertions (Backend + Tests); Node/TypeScript + Fastify + Baileys 6.7.23 + better-sqlite3 (Sidecar); React + TypeScript + Vite (Frontend).

## Global Constraints

- **Reihenfolge:** C → A → B → D. C zuerst, weil eine befüllte Allowlist nichts nützt, wenn der Vergleich an Formatvarianten scheitert.
- **Commit-Style:** Reine Subject-Lines, **kein** Body, **kein** `Co-Authored-By`-Trailer (Projekt-Konvention).
- **TDD nur dort, wo Test-Runner existiert:** Backend = xUnit (`src/Backend.Tests`). **Sidecar und Frontend haben kein Test-Script** → kein Test-Framework neu einführen; Verifikation per `typecheck`/`build` + manueller Smoke.
- **Keine destruktiven Vertragsänderungen:** bestehende Endpoints/DTOs bleiben kompatibel; neue Felder/Endpoints sind additiv.
- **JID-Fakt (geprüft in Baileys `lib/WABinary/jid-utils.js`):** `jidDecode` zerlegt `user:device@server` und schneidet `_agent`/`:device` ab. Der User-Teil von `@lid` ist ein **anderer** Identifier als der von `@s.whatsapp.net` — verschiedene Domains dürfen **nicht** gleichgesetzt werden. `@lid`↔Telefon wird nur dadurch gelöst, dass **beide** Formen (aus `onWhatsApp`) gespeichert werden (Task B).

---

## File Structure

| Datei | Verantwortung | Task |
| --- | --- | --- |
| `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppJid.cs` *(neu)* | Kanonische JID-Vergleichsform (Device/Agent strip, lowercase, c.us→s.whatsapp.net) | C |
| `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppObserver.cs:78,82` | Allowlist-Vergleich über normalisierte JIDs | C |
| `src/Backend.Tests/Features/AutonomousAgent/WhatsAppJidTests.cs` *(neu)* | Unit-Tests Normalisierung | C |
| `src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs` | Matching-Test Device-Suffix + lid-Form; FakeSidecar um neue Methode erweitern | C, B |
| `src/sidecar/src/baileys-manager.ts` | `refreshGroups()`, `resolveChat()`, Chat-Map laden/persistieren | A, B, D |
| `src/sidecar/src/index.ts` | neuer Endpoint `POST /sessions/:id/resolve` | B |
| `src/sidecar/src/buffer.ts` | `chats`-Tabelle + `upsertChat()`/`listChats()` | D |
| `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppTypes.cs:25` | Record `WhatsAppResolveResult` | B |
| `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/IWhatsAppSidecarClient.cs` | `ResolveChatAsync` | B |
| `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppSidecarClient.cs` | HTTP-Impl `ResolveChatAsync` | B |
| `src/Backend/Endpoints/SourceAccountsEndpoints.cs:176` | Endpoint `…/whatsapp/session/{sessionId}/resolve` | B |
| `src/frontend/src/api/source-accounts.ts:104` | `WhatsAppResolveDto` + `resolveWhatsAppChat()` | B |
| `src/frontend/src/components/settings/WhatsAppSection.tsx` | Eingabefeld „Chat per Nummer" in Card **und** AddForm | B |

---

## Task 1 (C): JID-Normalisierung + robustes Allowlist-Matching

**Files:**
- Create: `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppJid.cs`
- Create: `src/Backend.Tests/Features/AutonomousAgent/WhatsAppJidTests.cs`
- Modify: `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppObserver.cs:78,82`
- Modify: `src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs`

**Interfaces:**
- Produces: `static class WhatsAppJid { static string Normalize(string? jid) }` — gibt kanonische Form zurück: User-Teil (ohne `:device`/`_agent`) + `@server`, lowercase, `c.us`→`s.whatsapp.net`; leerer/`null` Input → `""`.

- [ ] **Step 1: Failing test für die Normalisierung schreiben**

`src/Backend.Tests/Features/AutonomousAgent/WhatsAppJidTests.cs`:
```csharp
using FluentAssertions;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;
using Xunit;

namespace NauAssist.Backend.Tests.Features.AutonomousAgent;

public sealed class WhatsAppJidTests
{
    [Theory]
    [InlineData("4915112345678@s.whatsapp.net", "4915112345678@s.whatsapp.net")]
    [InlineData("4915112345678:7@s.whatsapp.net", "4915112345678@s.whatsapp.net")] // Device-Suffix
    [InlineData("4915112345678_1@s.whatsapp.net", "4915112345678@s.whatsapp.net")] // Agent-Suffix
    [InlineData("4915112345678@c.us", "4915112345678@s.whatsapp.net")]              // c.us → s.whatsapp.net
    [InlineData("153737280586099@lid", "153737280586099@lid")]                      // lid bleibt lid
    [InlineData("123456789-1620000000@g.us", "123456789-1620000000@g.us")]          // Gruppe unverändert
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_CanonicalisesJid(string? input, string expected)
    {
        WhatsAppJid.Normalize(input).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag bestätigen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter WhatsAppJidTests`
Expected: FAIL — `WhatsAppJid` existiert nicht (Compile-Fehler).

- [ ] **Step 3: `WhatsAppJid.Normalize` minimal implementieren**

`src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppJid.cs`:
```csharp
namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Bringt eine WhatsApp-JID auf eine kanonische Vergleichsform: Device-/Agent-Suffix
/// entfernen, lowercase, c.us → s.whatsapp.net. Spiegelt Baileys' jidNormalizedUser.
/// Verschiedene Domains (lid vs s.whatsapp.net) bleiben bewusst verschieden — ihr
/// User-Teil ist je Domain ein anderer Identifier (siehe jid-utils.js).
/// </summary>
public static class WhatsAppJid
{
    public static string Normalize(string? jid)
    {
        if (string.IsNullOrWhiteSpace(jid)) return "";
        var at = jid.IndexOf('@');
        if (at < 0) return jid.Trim().ToLowerInvariant();

        var userCombined = jid[..at];
        var server = jid[(at + 1)..].ToLowerInvariant();
        // user:device → user ; user_agent → user
        var user = userCombined.Split(':')[0].Split('_')[0].ToLowerInvariant();
        if (server == "c.us") server = "s.whatsapp.net";
        return $"{user}@{server}";
    }
}
```

- [ ] **Step 4: Normalisierungs-Test grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter WhatsAppJidTests`
Expected: PASS (8 Theory-Fälle).

- [ ] **Step 5: Failing Observer-Test (Device-Suffix matcht Allowlist)**

In `src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs` ergänzen:
```csharp
    [Fact]
    public async Task Poll_NormalisesJid_MatchesDespiteDeviceSuffix()
    {
        using var db = new TempSqliteDb();
        var fake = new FakeSidecar
        {
            Page = new WhatsAppMessagePage(new[]
            {
                Msg(2, "4915112345678:7@s.whatsapp.net", "Hast du Freitag Zeit?"),
            }, Cursor: 2),
        };
        var (observer, accounts, cursors) = Build(db, fake);
        var account = await AddAccountAsync(accounts, "4915112345678@s.whatsapp.net");
        await cursors.SetAsync(SourceKey, account.Id, "1", Now, CancellationToken.None);

        var signals = await observer.PollAsync(CancellationToken.None);

        signals.Should().HaveCount(1);
        signals[0].SourceRef.Should().Be("4915112345678:7@s.whatsapp.net"); // Rohwert bleibt im Signal
    }
```

- [ ] **Step 6: Observer-Test laufen lassen, Fehlschlag bestätigen**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter WhatsAppObserverTests`
Expected: FAIL — `Poll_NormalisesJid_MatchesDespiteDeviceSuffix` liefert 0 Signale (Ordinal-Vergleich matcht den Device-Suffix nicht).

- [ ] **Step 7: Observer-Matching auf Normalisierung umstellen**

`src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppObserver.cs`, Zeile 78 ersetzen:
```csharp
                var allow = account.Allowlist
                    .Select(WhatsAppJid.Normalize)
                    .ToHashSet(StringComparer.Ordinal);
```
und Zeile 82 ersetzen:
```csharp
                    if (!allow.Contains(WhatsAppJid.Normalize(m.ChatId))) continue; // nur freigegebene Chats (JID-normalisiert)
```

- [ ] **Step 8: Gesamte WhatsApp-Suite grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "WhatsAppJidTests|WhatsAppObserverTests"`
Expected: PASS (alle bestehenden + 1 neuer Observer-Test + 8 Normalisierungs-Fälle). Die bestehenden Tests nutzen Allowlist `"chatA"` (kein `@`) → `Normalize("chatA") == "chata"`; eingehend `Msg(2,"chatA",…)` → `"chata"` → matcht weiterhin.

- [ ] **Step 9: Commit**

```bash
git add src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppJid.cs \
        src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppObserver.cs \
        src/Backend.Tests/Features/AutonomousAgent/WhatsAppJidTests.cs \
        src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs
git commit -m "WhatsApp C: Allowlist-Matching JID-normalisiert (Device-Suffix, c.us)"
```

---

## Task 2 (A): Gruppen aktiv laden — `groupFetchAllParticipating()`

**Files:**
- Modify: `src/sidecar/src/baileys-manager.ts` (neue `refreshGroups()`, Aufruf in `connection==="open"` und am Anfang von `listChats`)

**Interfaces:**
- Consumes: `session.chats: Map<string,string>` (chatId→Name), `session.sock` (Baileys-Socket).
- Produces: `private async refreshGroups(id: string): Promise<void>` — mergt alle teilnehmenden Gruppen (`jid → subject`) in `session.chats`.

> Kein Test-Runner im Sidecar → Verifikation per `typecheck`/`build` + Smoke (keine `.test.ts`).

- [ ] **Step 1: `refreshGroups()` implementieren**

In `src/sidecar/src/baileys-manager.ts` als Methode der Klasse (z. B. direkt vor `listChats`):
```ts
  /** Lädt alle teilnehmenden Gruppen aktiv (ohne auf Live-Events zu warten). */
  private async refreshGroups(id: string): Promise<void> {
    const s = this.sessions.get(id);
    if (!s?.sock || s.state !== "connected") return;
    try {
      const groups = await s.sock.groupFetchAllParticipating();
      for (const [jid, meta] of Object.entries(groups)) {
        s.chats.set(jid, meta.subject || jid);
      }
      this.logger.info({ id, count: Object.keys(groups).length }, "groups refreshed");
    } catch (e) {
      this.logger.warn({ id, err: e }, "group refresh failed");
    }
  }
```

- [ ] **Step 2: Beim Connect einmal Gruppen laden**

Im `connection === "open"`-Block (aktuell `baileys-manager.ts:115`) nach dem Logger-Aufruf ergänzen:
```ts
        this.logger.info({ id, phone: s.phone }, "session connected");
        void this.refreshGroups(id);
```

- [ ] **Step 3: `listChats` Gruppen on demand auffrischen**

`listChats` von synchron auf `async` umstellen, Gruppen vor der Rückgabe laden:
```ts
  async listChats(id: string): Promise<Array<{ chatId: string; name: string }> | null> {
    const s = this.sessions.get(id);
    if (!s) return null;
    await this.refreshGroups(id);
    return [...s.chats.entries()]
      .filter(([chatId]) => chatId !== "status@broadcast")
      .map(([chatId, name]) => ({ chatId, name }));
  }
```

- [ ] **Step 4: Aufrufer in `index.ts` auf `await` anpassen**

`src/sidecar/src/index.ts:47` (Route `GET /sessions/:id/chats`):
```ts
  const chats = await manager.listChats(id);
```

- [ ] **Step 5: Typecheck + Build grün**

Run: `cd src/sidecar && npm run typecheck && npm run build`
Expected: keine TS-Fehler (insb. `groupFetchAllParticipating` auf dem Socket-Typ vorhanden; `listChats` jetzt `Promise`).

- [ ] **Step 6: Commit**

```bash
git add src/sidecar/src/baileys-manager.ts src/sidecar/src/index.ts
git commit -m "WhatsApp A: Gruppen aktiv via groupFetchAllParticipating laden"
```

---

## Task 3 (B): Manuelle Chat-Eingabe per Nummer — `onWhatsApp()`

Drei separat reviewbare Schichten (3a Sidecar, 3b Backend, 3c Frontend).

### Task 3a (B-Sidecar): `resolveChat` + `POST /sessions/:id/resolve`

**Files:**
- Modify: `src/sidecar/src/baileys-manager.ts`
- Modify: `src/sidecar/src/index.ts`

**Interfaces:**
- Produces: `async resolveChat(id, phone): Promise<{ chatId: string; lid: string | null; exists: boolean } | null>` — `null` wenn Session nicht verbunden; `exists:false` wenn die Nummer kein WhatsApp hat.
- Produces (HTTP): `POST /sessions/:id/resolve { phone } → 200 { chatId, lid, exists } | 400 { error:"phone_required" } | 409 { error:"session_not_connected" }`.

- [ ] **Step 1: `resolveChat` implementieren**

In `src/sidecar/src/baileys-manager.ts` (z. B. nach `sendMessage`):
```ts
  /** Validiert eine Telefonnummer per onWhatsApp und liefert kanonische JID + LID. */
  async resolveChat(
    id: string,
    phone: string,
  ): Promise<{ chatId: string; lid: string | null; exists: boolean } | null> {
    const s = this.sessions.get(id);
    if (!s?.sock || s.state !== "connected") return null;
    const digits = phone.replace(/\D/g, "");
    if (!digits) return { chatId: "", lid: null, exists: false };

    const res = await s.sock.onWhatsApp(digits);
    const hit = res?.[0];
    if (!hit?.exists) return { chatId: "", lid: null, exists: false };

    const chatId = hit.jid;
    const lid = typeof hit.lid === "string" ? hit.lid : null;
    // Sofort in der Auswahlliste sichtbar machen (Name = Nummer als Fallback).
    if (!s.chats.has(chatId)) s.chats.set(chatId, digits);
    return { chatId, lid, exists: true };
  }
```

- [ ] **Step 2: Endpoint registrieren**

In `src/sidecar/src/index.ts` nach der `…/send`-Route:
```ts
app.post("/sessions/:id/resolve", async (req, reply) => {
  const { id } = req.params as { id: string };
  const body = (req.body ?? {}) as { phone?: string };
  if (!body.phone) return reply.code(400).send({ error: "phone_required" });
  const result = await manager.resolveChat(id, body.phone);
  if (!result) return reply.code(409).send({ error: "session_not_connected" });
  return result;
});
```

- [ ] **Step 3: Typecheck + Build grün**

Run: `cd src/sidecar && npm run typecheck && npm run build`
Expected: keine TS-Fehler (`onWhatsApp` Rückgabe `{ jid, exists, lid }[] | undefined`).

- [ ] **Step 4: Commit**

```bash
git add src/sidecar/src/baileys-manager.ts src/sidecar/src/index.ts
git commit -m "WhatsApp B/Sidecar: resolveChat per onWhatsApp + /resolve-Endpoint"
```

### Task 3b (B-Backend): Client-Methode + Helfer-Endpoint

**Files:**
- Modify: `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppTypes.cs:25`
- Modify: `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/IWhatsAppSidecarClient.cs`
- Modify: `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppSidecarClient.cs`
- Modify: `src/Backend/Endpoints/SourceAccountsEndpoints.cs:176`
- Modify: `src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs` (FakeSidecar)

**Interfaces:**
- Consumes: Sidecar `POST /sessions/:id/resolve`.
- Produces: `record WhatsAppResolveResult(string ChatId, string? Lid, bool Exists)`; `Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct)`; `POST /api/source-accounts/whatsapp/session/{sessionId}/resolve { phone }`.

- [ ] **Step 1: DTO-Record ergänzen**

`src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppTypes.cs` (nach `WhatsAppMessagePage`):
```csharp
/// <summary>Ergebnis einer onWhatsApp-Auflösung: kanonische JID + optionale LID-Form.</summary>
public sealed record WhatsAppResolveResult(string ChatId, string? Lid, bool Exists);
```

- [ ] **Step 2: Interface erweitern**

`src/Backend/Features/AutonomousAgent/Sources/WhatsApp/IWhatsAppSidecarClient.cs`, in das Interface:
```csharp
    Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct);
```

- [ ] **Step 3: FakeSidecar im Test um die Methode ergänzen (Compile-Fix)**

`src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs`, in `FakeSidecar`:
```csharp
        public Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct) =>
            Task.FromResult(new WhatsAppResolveResult($"{phone}@s.whatsapp.net", null, true));
```

- [ ] **Step 4: Tests bauen — bestätigt, dass die Suite ohne Impl nicht kompiliert**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter WhatsAppObserverTests`
Expected: FAIL (Build) — `WhatsAppSidecarClient` implementiert das Interface noch nicht vollständig.

- [ ] **Step 5: Client-Methode implementieren**

`src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppSidecarClient.cs` (nach `SendAsync`):
```csharp
    public async Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.PostAsJsonAsync(
            $"sessions/{Uri.EscapeDataString(sessionId)}/resolve",
            new { phone },
            ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<WhatsAppResolveResult>(JsonOpts, ct))
               ?? new WhatsAppResolveResult("", null, false);
    }
```

- [ ] **Step 6: Helfer-Endpoint mappen**

`src/Backend/Endpoints/SourceAccountsEndpoints.cs`, im `MapWhatsAppSourceEndpoints`-Block nach der `…/session/{sessionId}/chats`-Route (Zeile 176):
```csharp
        // Telefonnummer → kanonische JID (+ LID) für die manuelle Chat-Auswahl.
        group.MapPost("/whatsapp/session/{sessionId}/resolve", async (
            string sessionId,
            ResolveChatPayload? body,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Phone))
            {
                return Results.BadRequest(new { error = "phone_required" });
            }
            try
            {
                return Results.Ok(await client.ResolveChatAsync(sessionId, body.Phone, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });
```
Und das Payload-Record zu den anderen privaten Records (bei `StartWhatsAppPayload`, ~Zeile 304) ergänzen:
```csharp
    private sealed record ResolveChatPayload(string? Phone);
```

- [ ] **Step 7: Backend-Suite grün**

Run: `dotnet test src/Backend.Tests/Backend.Tests.csproj`
Expected: PASS (Build sauber, alle Tests grün).

- [ ] **Step 8: Commit**

```bash
git add src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppTypes.cs \
        src/Backend/Features/AutonomousAgent/Sources/WhatsApp/IWhatsAppSidecarClient.cs \
        src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppSidecarClient.cs \
        src/Backend/Endpoints/SourceAccountsEndpoints.cs \
        src/Backend.Tests/Features/AutonomousAgent/WhatsAppObserverTests.cs
git commit -m "WhatsApp B/Backend: ResolveChat-Endpoint + Client durchgereicht"
```

### Task 3c (B-Frontend): Eingabefeld „Chat per Nummer hinzufügen"

**Files:**
- Modify: `src/frontend/src/api/source-accounts.ts:104` (WhatsApp-Block)
- Modify: `src/frontend/src/components/settings/WhatsAppSection.tsx` (Card-Sektion + AddForm-Sektion)

**Interfaces:**
- Consumes: `POST /api/source-accounts/whatsapp/session/{sessionId}/resolve`.
- Produces: `interface WhatsAppResolveDto { chatId: string; lid: string | null; exists: boolean }`; `resolveWhatsAppChat(sessionId, phone): Promise<WhatsAppResolveDto>`.

> Kein Frontend-Test-Runner → Verifikation per `npm run build` + manueller Smoke.

- [ ] **Step 1: API-Funktion + DTO ergänzen**

`src/frontend/src/api/source-accounts.ts` im WhatsApp-Block (nach `listWhatsAppChatsForAccount`):
```ts
export interface WhatsAppResolveDto {
  chatId: string;
  lid: string | null;
  exists: boolean;
}

export async function resolveWhatsAppChat(
  sessionId: string,
  phone: string,
): Promise<WhatsAppResolveDto> {
  const res = await fetch(
    `/api/source-accounts/whatsapp/session/${encodeURIComponent(sessionId)}/resolve`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ phone }),
    },
  );
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.detail ?? body.error ?? `WhatsApp-Resolve fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as WhatsAppResolveDto;
}
```

- [ ] **Step 2: Resolve-Logik in der Account-Card ergänzen**

In `WaAccountCard` (`WhatsAppSection.tsx`): Import `resolveWhatsAppChat` und `WhatsAppResolveDto` aus `@/api/source-accounts` ergänzen. Neue States nach `error`:
```ts
  const [phoneInput, setPhoneInput] = useState("");
  const [resolving, setResolving] = useState(false);
```
Handler nach `toggle` ergänzen (fügt beide JID-Formen in die Allowlist, damit `@lid`-adressierte Nachrichten matchen — vgl. Task C/Global Constraints):
```ts
  const addByPhone = async () => {
    const sessionId = account.credentials.sessionId;
    if (!sessionId || !phoneInput.trim()) return;
    setResolving(true);
    setError(null);
    try {
      const r = await resolveWhatsAppChat(sessionId, phoneInput.trim());
      if (!r.exists) { setError("Nummer hat kein WhatsApp."); return; }
      setChats((prev) => {
        const next = prev ? [...prev] : [];
        if (!next.some((c) => c.chatId === r.chatId)) {
          next.push({ chatId: r.chatId, name: phoneInput.trim() });
        }
        return next;
      });
      setDraftAllowlist((prev) => {
        const add = [r.chatId, r.lid].filter((x): x is string => !!x && !prev.includes(x));
        return add.length ? [...prev, ...add] : prev;
      });
      setPhoneInput("");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setResolving(false);
    }
  };
```

- [ ] **Step 3: Eingabefeld ins Card-Markup einsetzen**

In der `expanded`-Sektion direkt vor dem `loadChats`-Button-Container (vor `<div className="flex items-center gap-3">`, ~Zeile 234):
```tsx
          <div className="mb-3 flex items-center gap-2">
            <input
              type="text"
              inputMode="tel"
              value={phoneInput}
              onChange={(e) => setPhoneInput(e.target.value)}
              placeholder="Nummer, z. B. 4915112345678"
              className="min-h-10 flex-1 border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono text-nau-fg"
            />
            <button
              type="button"
              onClick={addByPhone}
              disabled={resolving || !phoneInput.trim()}
              className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg transition-colors hover:border-nau-accent hover:text-nau-accent disabled:opacity-50"
            >
              {resolving ? "PRÜFE …" : "HINZUFÜGEN"}
            </button>
          </div>
```

- [ ] **Step 4: Freigabe-Zähler um die LID-Form bereinigen**

`WhatsAppSection.tsx:201` ersetzen (LID-Form nicht doppelt zählen):
```tsx
        // {account.allowlist.filter((c) => !c.endsWith("@lid")).length} Chat(s) freigegeben
```

- [ ] **Step 5: Gleiches Eingabefeld in der AddForm**

Im AddForm-Teil (zweite Komponente, ab ~Zeile 295) analog: lokale States `phoneInput`/`resolving`, ein `addByPhone`, das `resolveWhatsAppChat(sessionId, …)` mit der hier in State liegenden `sessionId` nutzt, neue Chats in `setChats` und beide Formen in `setAllowlist` schreibt. Das Eingabefeld kommt in den CHATS-Block vor der Chat-Liste (vor `{chats && chats.length > 0 ? …}`, ~Zeile 449), Markup identisch zu Step 3.

- [ ] **Step 6: Build grün**

Run: `cd src/frontend && npm run build`
Expected: keine TS-Fehler, Build erfolgreich.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/api/source-accounts.ts \
        src/frontend/src/components/settings/WhatsAppSection.tsx
git commit -m "WhatsApp B/Frontend: Chat per Nummer hinzufuegen (Card + AddForm)"
```

---

## Task 4 (D): Chat-Map persistieren

**Files:**
- Modify: `src/sidecar/src/buffer.ts` (neue Tabelle + Methoden)
- Modify: `src/sidecar/src/baileys-manager.ts` (beim Start laden, bei Discovery schreiben)

**Interfaces:**
- Produces (buffer): `upsertChat(sessionId: string, chatId: string, name: string): void`; `listChats(sessionId: string): Array<{ chatId: string; name: string }>`.
- Consumes (manager): obige Methoden des injizierten `MessageBuffer`.

> Kein Test-Runner im Sidecar → Verifikation per `typecheck`/`build` + Smoke.

- [ ] **Step 1: `chats`-Tabelle + Methoden im Buffer**

`src/sidecar/src/buffer.ts`, im Konstruktor-`exec` zusätzlich zur `messages`-Tabelle:
```sql
      CREATE TABLE IF NOT EXISTS chats (
        session_id  TEXT NOT NULL,
        chat_id     TEXT NOT NULL,
        name        TEXT NOT NULL,
        PRIMARY KEY (session_id, chat_id)
      );
```
Und als Methoden der Klasse `MessageBuffer` (nach `maxSeq`):
```ts
  upsertChat(sessionId: string, chatId: string, name: string): void {
    this.db
      .prepare(
        `INSERT INTO chats (session_id, chat_id, name) VALUES (?, ?, ?)
         ON CONFLICT(session_id, chat_id) DO UPDATE SET name = excluded.name`,
      )
      .run(sessionId, chatId, name);
  }

  listChats(sessionId: string): Array<{ chatId: string; name: string }> {
    const rows = this.db
      .prepare(`SELECT chat_id AS chatId, name FROM chats WHERE session_id = ?`)
      .all(sessionId) as Array<{ chatId: string; name: string }>;
    return rows;
  }
```

- [ ] **Step 2: Beim Session-Start persistierte Chats laden**

`src/sidecar/src/baileys-manager.ts`, in `startSession` nach dem Anlegen/Holen der Session (nach `this.sessions.set(id, session)` bzw. direkt vor `if (session.starting) return;`):
```ts
    for (const c of this.buffer.listChats(id)) {
      if (!session.chats.has(c.chatId)) session.chats.set(c.chatId, c.name);
    }
```

- [ ] **Step 3: Entdeckte Chats mitschreiben**

In `baileys-manager.ts` die zentrale Stelle `s.chats.set(...)` durch Persistenz ergänzen. In `rememberChats`:
```ts
      for (const c of chats) {
        if (c.id) {
          const name = c.name ?? c.id;
          s.chats.set(c.id, name);
          this.buffer.upsertChat(id, c.id, name);
        }
      }
```
Analog in den beiden anderen Settern: `contacts.upsert` (`s.chats.set(c.id, name)` → zusätzlich `this.buffer.upsertChat(id, c.id, name)`), im `messages.upsert`-`pushName`-Zweig, in `refreshGroups` (Task A) und in `resolveChat` (Task 3a). Jeweils unmittelbar nach dem `s.chats.set(...)` ein `this.buffer.upsertChat(id, <chatId>, <name>)`.

- [ ] **Step 4: Typecheck + Build grün**

Run: `cd src/sidecar && npm run typecheck && npm run build`
Expected: keine TS-Fehler.

- [ ] **Step 5: Commit**

```bash
git add src/sidecar/src/buffer.ts src/sidecar/src/baileys-manager.ts
git commit -m "WhatsApp D: Chat-Map in buffer.db persistieren (ueberlebt Neustart)"
```

---

## Task 5: Dokumentation in BenediktsMind

> Projekt-Konvention: jede Projektarbeit in BenediktsMind dokumentieren (Doings-Board + Notiz).

- [ ] **Step 1:** Notiz „WhatsApp-Allowlist Bootstrap (C/A/B/D)" anlegen — Problem (connected, keine Suggestions), Ursachen (leere Allowlist + JID-Matching), Lösung je Schritt, Stand.
- [ ] **Step 2:** Eintrag auf dem Doings-Board verlinken/aktualisieren.

---

## Verification (End-to-End)

1. **C:** `dotnet test src/Backend.Tests/Backend.Tests.csproj --filter "WhatsAppJidTests|WhatsAppObserverTests"` grün. Danach reale Termin-Nachricht aus einem freigegebenen Chat → `POST /api/suggestions/poll-now` → Suggestion erscheint.
2. **A:** Im UI „CHATS VERWALTEN → AKTUALISIEREN" zeigt **direkt nach dem Pairing** alle Gruppen, ohne dass jemand geschrieben hat.
3. **B:** Nummer (z. B. `4915112345678`) ins neue Feld → „HINZUFÜGEN" → Chat erscheint wählbar und ist angehakt → „ALLOWLIST SPEICHERN" → Card zeigt „// n Chat(s) freigegeben" > 0.
4. **D:** Sidecar neu starten (`docker compose restart` auf NVEICDOC00) → „CHATS LADEN" zeigt die zuvor entdeckten Chats sofort, ohne neue Live-Events.
5. **Regression/Build:** `dotnet test src/Backend.Tests/Backend.Tests.csproj`; `cd src/sidecar && npm run typecheck && npm run build`; `cd src/frontend && npm run build` — alle grün.
6. **Deploy:** gemäß Deployment-Konvention NVEICDOC00 (`:latest` nur per `v*`-Tag, nicht per main-Push).

## Bekannte Grenze

- **`@lid`-Waisen:** Beim Hinzufügen per Nummer (B) wird neben `…@s.whatsapp.net` auch die `…@lid`-Form in die Allowlist geschrieben. Entfernt man den Chat später per Häkchen, bleibt ggf. der `@lid`-Eintrag als harmloser Waise stehen (matcht dann nichts Sichtbares). Bewusst akzeptiert; der Freigabe-Zähler blendet `@lid`-Einträge aus.

## Verweise

- Recherche/Spec: `docs/superpowers/specs/2026-06-18-whatsapp-chat-allowlist-bootstrap.md`
- Diagnose-Runbook: `~/.claude/plans/happy-forging-hippo.md`
- Ursprüngliches Design: `docs/superpowers/specs/2026-05-30-whatsapp-baileys-sidecar-design.md`
