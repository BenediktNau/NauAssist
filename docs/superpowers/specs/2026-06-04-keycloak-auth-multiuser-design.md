# Keycloak-Auth & Multi-User — Design

**Datum:** 2026-06-04
**Status:** Approved (Brainstorming abgeschlossen, Review durch User ausstehend)

## Ziel

Optionale Authentifizierung über **Keycloak** (OIDC) einführen, sodass NauAssist
von mehreren Usern genutzt werden kann. Auth ist **abschaltbar**: ist sie aus,
verhält sich die App bit-genau wie heute (Single-User, kein Login). Keine
Eigen-Authentifizierung — ausschließlich Keycloak.

### Scope (pro User getrennt)
- **Chats** (`messages`, `chat_clear_markers`, `audit_log`)
- **Kalender** (Google-OAuth-Token pro User)
- **Autonomer Agent**: IMAP-Quellen, Suggestions, Scheduler-Lauf pro User

### Global (geteilt, kein User-Bezug)
- `rules`, `app_settings` (LLM-Endpoint, Arbeitszeiten, Google-Client-Credentials, VAPID), Persona

### Bewusst out of scope (dokumentierte Limitierungen)
- **WhatsApp**: Der Sidecar hält eine Baileys-Session. WhatsApp bleibt in dieser
  Iteration **owner-only** (eine Session, dem Default/Owner-User zugeordnet).
  Multi-Session pro User ist ein separates Folge-Feature (Sidecar-Umbau).
- **Keycloak-Hardening**: Lokal/Personal läuft Keycloak mit H2-Datei
  (`start-dev --import-realm`). Umstieg auf Postgres als Later.
- **Persona/Persona-Memory** bleibt global.

## Nicht-Ziele
- Kein Admin-/User-Management-UI in NauAssist (Userverwaltung passiert in Keycloak).
- Keine Rollen/Rechte-Differenzierung zwischen Usern (alle gleichberechtigt).

---

## §1 · Auth-Toggle & Konfiguration

Neuer Config-Block `Auth`, env-getrieben (Coolify-tauglich), gespiegelt am
bestehenden WhatsApp-Opt-in-Muster.

```
Auth__Enabled=true|false            # der eine Schalter
Auth__Issuer=https://auth.domain/realms/nauassist
Auth__Audience=nauassist-api
Auth__ClientId=nauassist-web        # fürs Frontend
Auth__RequireHttpsMetadata=true     # lokal false
```

- `Enabled=false` → heutiges Verhalten, kein Login, kein Keycloak nötig.
- Backend registriert JWT-Bearer-Middleware + `UseAuthentication`/`UseAuthorization`
  **nur** wenn `Enabled=true`.
- Der bestehende **`/api/capabilities`**-Endpoint liefert künftig
  `auth: { enabled, issuer, audience, clientId }`, damit das **Frontend seine
  OIDC-Config zur Laufzeit vom Backend** zieht → kein Frontend-Rebuild pro
  Umgebung (wichtig für Coolify).

`AuthOptions` wird wie die übrigen Options aus der Config gebunden (`builder.Configuration.GetSection("Auth")`).

---

## §2 · Identität im Code (`IUserContext` + Holder)

```csharp
public interface IUserContext      { string UserId { get; } }
public interface IUserContextSetter { void Set(string userId); }

// scoped; Default = "nauassist-default"
public sealed class UserContextHolder : IUserContext, IUserContextSetter
{
    public string UserId { get; private set; } = DefaultUser.Id; // "nauassist-default"
    public void Set(string userId) => UserId = userId;
}
```

Registrierung: scoped, beide Interfaces auf dieselbe Instanz.
`builder.Services.AddHttpContextAccessor()` wird ergänzt.

- **HTTP-Pfad**: Middleware liest `sub` aus `HttpContext.User` und ruft
  `Set(sub)`. Auth aus → Holder bleibt auf Default-User.
- **Background-Pfad** (autonomer Agent): Der Scheduler öffnet pro User einen
  DI-Scope und ruft `IUserContextSetter.Set(userId)` — dort gibt es keinen
  HttpContext.

Damit kommt die Keycloak-`sub` für beide Welten (Request & Background) sauber
in die Repos.

---

## §3 · Keycloak im Compose (Profil `auth`)

Neuer Service in `docker-compose.yml`:

```yaml
keycloak:
  image: quay.io/keycloak/keycloak:26.x
  profiles: ["auth"]
  command: start-dev --import-realm
  volumes:
    - keycloak-data:/opt/keycloak/data
    - ./keycloak/realm-nauassist.json:/opt/keycloak/data/import/realm-nauassist.json:ro
  environment:
    - KEYCLOAK_ADMIN=${KEYCLOAK_ADMIN:-admin}
    - KEYCLOAK_ADMIN_PASSWORD=${KEYCLOAK_ADMIN_PASSWORD:-admin}
    - KC_HOSTNAME=${KC_HOSTNAME:-}
    - KC_PROXY_HEADERS=xforwarded
    - NAUASSIST_TEST_USER=${NAUASSIST_TEST_USER:-test}
    - NAUASSIST_TEST_PASSWORD=${NAUASSIST_TEST_PASSWORD:-test}
  restart: unless-stopped
```

- Über `COMPOSE_PROFILES=auth` (analog `whatsapp`) zuschaltbar.
- **Coolify**: `KC_HOSTNAME` = öffentliche Auth-(Sub-)Domain, `KC_PROXY_HEADERS=xforwarded`
  hinter Traefik. Alles über env → derselbe Stack lokal & auf Coolify.
- H2-Datei im Volume (personal/dev). Postgres-Hardening = Later.

`.env.example` wird um den Auth-Block ergänzt (analog WhatsApp-Block), mit
auskommentierten Zeilen + Hinweis „Standard: DEAKTIVIERT".

---

## §4 · Datenmodell & Migration (`0014_multi_user.sql`)

Neue Tabelle:

```sql
CREATE TABLE users (
    id            TEXT PRIMARY KEY,   -- Keycloak sub; Default-User: 'nauassist-default'
    username      TEXT NULL,
    email         TEXT NULL,
    created_at    TEXT NOT NULL,
    last_seen_at  TEXT NULL
);
INSERT INTO users(id, username, created_at)
VALUES ('nauassist-default', 'default', <now>);
```

`user_id TEXT NOT NULL DEFAULT 'nauassist-default'` ergänzen auf:
`messages`, `chat_clear_markers`, `audit_log`, `suggestions`, `source_accounts`,
Source-Cursor-Tabelle, `web_push`, `reply_metadata`.

- Indizes um `user_id` erweitern (z.B. `idx_messages_session_created` →
  `(user_id, session_id, created_at)`).
- **Bestehende Daten** wandern über den Default-Wert lückenlos auf den Owner.
- **Google-Token**: liegt im `SqliteDataStore` bereits unter Key
  `"nauassist-default"` → wird automatisch der Default-User-Token, **keine DDL**.
- **Global** (kein `user_id`): `rules`, `app_settings`, Persona-Tabellen.

> SQLite: `ALTER TABLE ... ADD COLUMN ... NOT NULL DEFAULT '...'` ist zulässig.
> Index-Umbau via DROP/CREATE in derselben Migration.

---

## §5 · User-Provisioning & Auth-Wiring

- **Provisioning**: Beim ersten authentifizierten Request Upsert in `users`
  (`sub`, `preferred_username`, `email`, `last_seen_at`). Keycloak bleibt
  Source-of-Truth, kein eigenes Admin-UI. Der Upsert läuft in der
  User-Context-Middleware (nach erfolgreicher Token-Validierung).
- **Schutz**: Alle `/api/*`-Endpoints `RequireAuthorization()` wenn Auth an;
  `/api/health` und `/api/capabilities` bleiben offen (Frontend braucht
  Capabilities **vor** dem Login).
- **Repos**: bekommen `IUserContext` injiziert → `WHERE user_id = @UserId` in
  allen per-User-Queries; Inserts setzen `user_id = ctx.UserId`.
- **Chat-Session**: `ChatEndpoints` behält `session_id = "default"`;
  Eindeutigkeit ist jetzt `(user_id, session_id)` → minimaler Frontend-Eingriff.
- **GoogleAuthService**: Konstante `UserId = "nauassist-default"` entfällt; der
  Token-Key kommt aus `IUserContext.UserId`. Connect/Disconnect/IsConnected
  arbeiten damit pro User.

---

## §6 · Autonomer Agent pro User

- **IMAP/Suggestions/Kalender pro User**: `source_accounts`, Source-Cursor und
  Suggestions bekommen `user_id`. Der Scheduler iteriert die `users`-Tabelle,
  öffnet pro User einen DI-Scope, setzt `IUserContextSetter.Set(userId)` und
  verarbeitet dessen Quellen; geschrieben wird in den Kalender dieses Users
  (Token via `IUserContext`).
- User ohne Kalender-Token werden übersprungen (`NotAuthenticatedException`
  existiert bereits) — kein Abbruch des Gesamtlaufs.
- **WhatsApp**: bleibt **owner-only** (eine Sidecar-Session, dem Default/Owner-User
  zugeordnet). Multi-Session pro User = Folge-Feature. WhatsApp-Quellen werden
  beim Provisioning **nicht** automatisch pro User angelegt.

---

## §7 · Seeding (Test-User)

Realm-Export `keycloak/realm-nauassist.json`, via `--import-realm` gemountet,
enthält deklarativ:

- Realm `nauassist`
- Public-Client `nauassist-web` (Standard-Flow + PKCE, Redirect-URIs/Web-Origins
  aus env bzw. mit lokalen Defaults `http://localhost:8080/*`)
- Audience-Mapper, der `nauassist-api` als `aud` ins Access-Token schreibt
- **Vorab angelegter Test-User** (Username/Passwort aus
  `NAUASSIST_TEST_USER`/`NAUASSIST_TEST_PASSWORD`, Default `test`/`test`, mit
  E-Mail) → nach `docker compose --profile auth up` sofort einloggbar.

Weitere User legt der Betreiber in der Keycloak-Admin-Konsole an.

---

## §8 · Frontend

- Lib: `react-oidc-context` (+ `oidc-client-ts`).
- OIDC-Config wird zur **Laufzeit** aus `/api/capabilities` gelesen
  (`auth.issuer`, `auth.clientId`) → kein Rebuild pro Umgebung.
- Flow: Auth an & nicht eingeloggt → Redirect zu Keycloak (Auth-Code + PKCE).
  Access-Token in-memory + Refresh-Token-Handling durch die Lib.
- `api/client.ts`: hängt **Bearer-Header** an alle Calls. 401 → Silent-Refresh,
  bei Fehlschlag Re-Login.
- Logout-Button (ruft Keycloak-Logout + lokales Token-Clearing).
- Auth aus (`capabilities.auth.enabled === false`) → **keinerlei Login-UI**,
  exakt wie heute.

---

## §9 · Error Handling

| Situation | Verhalten |
|-----------|-----------|
| Auth an, kein/ungültiges Token | 401 → Frontend leitet zum Login |
| Token abgelaufen mid-session | 401 → Silent-Refresh; scheitert → Re-Login |
| Keycloak beim Start nicht erreichbar | JWKS wird lazy beim ersten Request geladen; bis dahin 401/503 (dokumentiert) |
| Auth aus | Alle Requests laufen als Default-User durch |
| Scheduler: User ohne Kalender-Token | User wird übersprungen, Lauf geht weiter |

---

## §10 · Testing

- **Backend-Tests brauchen kein Keycloak**: `IUserContext` ist injizierbar →
  Tests setzen eine fixe User-ID.
- **Neue Isolations-Tests**: zwei User-Kontexte, Daten-Trennung asserten
  (Chats, Kalender-Token, Suggestions sehen sich gegenseitig nicht).
- **Toggle-Test**: „Auth aus → Default-User" liefert `nauassist-default`.
- **Provisioning-Test**: erster authentifizierter Zugriff legt `users`-Zeile an.
- Token-Signatur-Validierung = Standard-JWT-Bearer-Middleware, nicht eigens
  getestet.
- **E2E/manuell**: `docker compose --profile auth up`, Login mit Seed-User,
  zweiter User → getrennte Chats/Kalender verifizieren.

---

## Betroffene/neue Artefakte (Überblick)

**Neu**
- `src/Backend/Features/Infrastructure/Auth/` — `AuthOptions`, `IUserContext`,
  `UserContextHolder`, User-Context-Middleware, `UserRepository`, `DefaultUser`
- `src/Backend/Features/Infrastructure/Persistence/Migrations/0014_multi_user.sql`
- `keycloak/realm-nauassist.json`
- Frontend: OIDC-Provider-Wiring, Login/Logout-UI, Token-Interceptor

**Geändert**
- `Program.cs` (Auth-Wiring conditional, HttpContextAccessor, UserContext-DI)
- `docker-compose.yml`, `.env.example` (Keycloak-Service + Auth-Block)
- `CapabilitiesEndpoints.cs` (auth-Block)
- Alle per-User-Repos (`MessageRepository`, `ChatClearMarkerRepository`,
  `AuditLogRepository`, `SuggestionRepository`, `SourceAccountRepository`,
  `SourceCursorRepository`, Web-Push-Repo) → `IUserContext`-Filter
- `GoogleAuthService` (UserId aus Context statt Konstante)
- Autonomer-Agent-Scheduler (Iteration über `users`, Scope pro User)
- `ChatEndpoints` (Session weiterhin "default", user-scoped)
- `frontend/src/api/client.ts`, `frontend/src/api/capabilities.ts`
