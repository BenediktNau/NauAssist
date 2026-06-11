# Keycloak-Auth & Multi-User — Design

**Datum:** 2026-06-04
**Status:** Approved
**Update 2026-06-11:** §1, §7, §8 vom SPA-Token-Ansatz (react-oidc-context + PKCE
+ Bearer) auf das **BFF-Pattern** umgestellt — Cookie + serverseitiger OIDC-Flow,
nach Review der Referenz-Implementierung im Abrechner (`../abrechner`,
`Api/Program.cs`). Begründung: NauAssist serviert das Frontend aus dem Backend
(same-origin) → Cookie-Auth ohne CORS-Themen, das komplette Token-Handling im
Frontend entfällt, und der Abrechner-Code (inkl. Coolify-Erfahrungswerte wie
Backchannel-Handler und Proxy-Konfiguration) ist direkt wiederverwendbar.

## Ziel

Optionale Authentifizierung über **Keycloak** (OIDC) einführen, sodass NauAssist
von mehreren Usern genutzt werden kann. Auth ist **abschaltbar**: ist sie aus,
verhält sich die App bit-genau wie heute (Single-User, kein Login). Keine
Eigen-Authentifizierung — ausschließlich Keycloak.

### Scope (pro User getrennt)
- **Chats** (`messages`, `chat_clear_markers`, `audit_log`)
- **Kalender** (Google-OAuth-Token pro User)
- **Kalender-Einstellungen** (Arbeitszeiten, Termindauer, Such-Horizont,
  CalendarId) — *Erweiterung 2026-06-11*: `user_settings`-Tabelle (Migration
  0015), Lesen mit Fallback auf die globalen `app_settings` als Seed/Default,
  Schreiben nur user-scoped
- **Autonomer Agent**: IMAP-Quellen, Suggestions, Scheduler-Lauf pro User

### Global (geteilt, kein User-Bezug)
- `rules`, `app_settings` (LLM-Endpoint, Google-Client-Credentials, VAPID), Persona

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

## §1 · Auth-Toggle & Konfiguration (BFF-Pattern)

Neuer Config-Block `Auth`, env-getrieben (Coolify-tauglich), gespiegelt am
bestehenden WhatsApp-Opt-in-Muster.

```
Auth__Enabled=true|false            # der eine Schalter
Auth__Authority=https://auth.domain # Keycloak-Basis-URL (öffentlich, ohne /realms/...)
Auth__Realm=nauassist
Auth__ClientId=nauassist-web
Auth__ClientSecret=...              # confidential Client (BFF) — Secret nur im Backend
Auth__InternalUrl=                  # optional: http://keycloak:8080 (Backchannel, s.u.)
Auth__RequireHttpsMetadata=true     # lokal false
```

- `Enabled=false` → heutiges Verhalten, kein Login, kein Keycloak nötig.
- **BFF-Pattern** (wie Abrechner): Backend registriert **Cookie-Auth + OIDC-Handler**
  **nur** wenn `Enabled=true`. Der Authorization-Code-Flow läuft komplett
  serverseitig (confidential Client); der Browser bekommt ausschließlich ein
  HttpOnly-Session-Cookie (`nauassist.session`, SameSite=Lax, sliding 8h) —
  **nie ein Token**.
- **BFF-Endpoints** (anonym erreichbar, eigene Gruppe `/auth`):
  - `GET /auth/login?returnUrl=…` → `Challenge` Richtung Keycloak
    (returnUrl-Validierung gegen Open Redirect, wie Abrechner `BffRoutes`)
  - `POST /auth/logout` → Cookie-SignOut + Keycloak-SignOut
  - `GET /auth/me` → `{ isAuthenticated, sub, username, email }`
- **Cookie-Events**: `OnRedirectToLogin` → 401 (statt HTML-Redirect), damit
  API-Calls aus dem Frontend sauber reagieren können.
- **OIDC-Details** (aus dem Abrechner übernommen): `ResponseType=code`,
  `MapInboundClaims=false` (→ `sub` bleibt `sub`), Scopes `openid profile email`,
  `PushedAuthorizationBehavior.Disable` (.NET 10 aktiviert PAR automatisch →
  405 bei Keycloak).
- **Backchannel über Compose-Netz**: Ist `Auth__InternalUrl` gesetzt, gehen
  Metadata-/Token-Requests über `http://keycloak:8080` statt über die öffentliche
  Domain (Abrechner-`KeycloakBackchannelHandler` 1:1 übernehmen). Vermeidet
  TLS-Hairpin durch den Coolify-Proxy; Browser-Redirects nutzen weiter die
  öffentliche `Authority`.
- Der bestehende **`/api/capabilities`**-Endpoint liefert künftig nur noch
  `auth: { enabled, loginUrl }` — das Frontend braucht **keine OIDC-Config mehr**
  (kein Rebuild pro Umgebung; noch Coolify-freundlicher als der alte Ansatz).
- **CSRF**: SameSite=Lax deckt state-ändernde Cross-Site-Requests ab (Cookies
  werden nur bei Top-Level-GET-Navigation mitgesendet); explizites Antiforgery
  wie im Abrechner = optionales Hardening (Later).
- Hinter dem Proxy: `UseForwardedHeaders` (X-Forwarded-Proto/Host), sonst stimmen
  Redirect-URIs nicht.

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
  `Set(sub)`. Auth aus → Holder bleibt auf Default-User. Funktioniert mit dem
  Cookie-Principal identisch wie mit Bearer (`MapInboundClaims=false` → der
  `sub`-Claim bleibt unverändert erhalten).
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
    - KC_HTTP_ENABLED=true
    - NAUASSIST_TEST_USER=${NAUASSIST_TEST_USER:-test}
    - NAUASSIST_TEST_PASSWORD=${NAUASSIST_TEST_PASSWORD:-test}
  restart: unless-stopped
```

- Über `COMPOSE_PROFILES=auth` (analog `whatsapp`) zuschaltbar.
- **Coolify**: `KC_HOSTNAME` = öffentliche Auth-(Sub-)Domain (in Coolify dem
  `keycloak`-Service als Domain zuweisen; Zertifikate macht der Coolify-Proxy),
  `KC_PROXY_HEADERS=xforwarded` + `KC_HTTP_ENABLED=true` hinter dem Proxy
  (TLS terminiert am Proxy; bei `start-dev` ist HTTP eh an, bei späterem
  Prod-Mode `start` Pflicht — Abrechner-Staging macht es genau so). Alles über
  env → derselbe Stack lokal & auf Coolify.
- Die NauAssist-App bekommt zusätzlich `Auth__InternalUrl=http://keycloak:8080`,
  damit der OIDC-Backchannel übers interne Compose-Netz läuft (kein Hairpin
  durch den Proxy, siehe §1).
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
  User-Context-Middleware (nach erfolgreicher Authentifizierung).
- **Schutz**: Alle `/api/*`-Endpoints `RequireAuthorization()` wenn Auth an;
  `/api/health`, `/api/capabilities` und die `/auth/*`-BFF-Endpoints bleiben
  offen (Frontend braucht Capabilities **vor** dem Login; Login/Logout/me sind
  per Definition anonym erreichbar).
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
- **Confidential-Client** `nauassist-web` (Standard-Flow, `publicClient: false`,
  zusätzlich PKCE S256 als Hardening — wie der Abrechner-Client). Redirect-URIs
  zeigen auf den **OIDC-Callback des Backends** (`<App-URL>/signin-oidc`,
  Post-Logout `<App-URL>/signout-callback-oidc`), aus env bzw. mit lokalem
  Default `http://localhost:8080/*`.
- **Client-Secret**: im Export `${NAUASSIST_OIDC_SECRET}` (Keycloak ersetzt
  env-Platzhalter beim Realm-Import — derselbe Mechanismus wie beim Test-User).
  Dasselbe Secret bekommt die App als `Auth__ClientSecret`. Fallback, falls die
  Substitution Probleme macht: Placeholder importieren und Secret einmalig in
  der Admin-Konsole setzen (so handhabt es der Abrechner in Staging).
- ~~Audience-Mapper~~ entfällt — es gibt kein Access-Token im Browser mehr,
  das Backend validiert den Code-Flow direkt gegen Keycloak.
- **Vorab angelegter Test-User** (Username/Passwort aus
  `NAUASSIST_TEST_USER`/`NAUASSIST_TEST_PASSWORD`, Default `test`/`test`, mit
  E-Mail) → nach `docker compose --profile auth up` sofort einloggbar.

Weitere User legt der Betreiber in der Keycloak-Admin-Konsole an.

---

## §8 · Frontend

Durch das BFF-Pattern **keine OIDC-Bibliothek, kein Token-Handling** — das
Frontend kennt nur das Session-Cookie (läuft bei same-origin automatisch mit).

- **AuthProvider** (analog Abrechner `shared/lib/auth/AuthProvider.tsx`):
  liest beim Boot `capabilities.auth.enabled`; wenn an → `GET /auth/me`.
  Nicht eingeloggt → `window.location = '/auth/login?returnUrl=<aktuelle Route>'`.
- `api/client.ts`: **kein** Bearer-Header. Antwortet ein Call mit 401
  (Session abgelaufen) → Redirect auf `/auth/login` (Keycloak-SSO-Session
  loggt i.d.R. transparent wieder ein, ohne Passwort-Prompt).
- Logout-Button: `POST /auth/logout` (beendet Cookie- und Keycloak-Session).
- Auth aus (`capabilities.auth.enabled === false`) → **keinerlei Login-UI**,
  exakt wie heute.

---

## §9 · Error Handling

| Situation | Verhalten |
|-----------|-----------|
| Auth an, keine/ungültige Session | 401 (`OnRedirectToLogin` → Statuscode statt HTML-Redirect) → Frontend leitet auf `/auth/login` |
| Session abgelaufen mid-session | 401 → Redirect auf `/auth/login`; solange die Keycloak-SSO-Session lebt, läuft der Re-Login transparent ohne Passwort-Prompt |
| Keycloak beim Start nicht erreichbar | OIDC-Metadata wird lazy beim ersten Login-Versuch geladen; bis dahin schlägt nur der Login fehl (5xx), `Enabled=false`-Betrieb ist nie betroffen |
| Auth aus | Alle Requests laufen als Default-User durch |
| Scheduler: User ohne Kalender-Token | User wird übersprungen, Lauf geht weiter |

---

## §10 · Testing

- **Backend-Tests brauchen kein Keycloak**: `IUserContext` ist injizierbar →
  Tests setzen eine fixe User-ID.
- **Neue Isolations-Tests**: zwei User-Kontexte, Daten-Trennung asserten
  (Chats, Kalender-Token, Suggestions sehen sich gegenseitig nicht).
- **Toggle-Test**: „Auth aus → Default-User" liefert `nauassist-default`.
- **Provisioning-Test**: erster authentifizierter Zugriff legt `users`-Zeile an
  (Integrationstest mit Test-Auth-Handler/fixem `ClaimsPrincipal`, kein Keycloak).
- OIDC-Code-Flow & Cookie-Handling = Standard-ASP.NET-Middleware, nicht eigens
  getestet.
- **E2E/manuell**: `docker compose --profile auth up`, Login mit Seed-User,
  zweiter User → getrennte Chats/Kalender verifizieren.

---

## Betroffene/neue Artefakte (Überblick)

**Neu**
- `src/Backend/Features/Infrastructure/Auth/` — `AuthOptions`, `IUserContext`,
  `UserContextHolder`, User-Context-Middleware, `UserRepository`, `DefaultUser`,
  BFF-Endpoints (`/auth/login|logout|me`), `KeycloakBackchannelHandler`
  (aus Abrechner `Api/Program.cs` portieren)
- `src/Backend/Features/Infrastructure/Persistence/Migrations/0014_multi_user.sql`
- `keycloak/realm-nauassist.json`
- Frontend: `AuthProvider` (capabilities → `/auth/me` → ggf. Login-Redirect),
  Logout-Button

**Geändert**
- `Program.cs` (Cookie+OIDC-Wiring conditional, `UseForwardedHeaders`,
  HttpContextAccessor, UserContext-DI)
- `docker-compose.yml`, `.env.example` (Keycloak-Service + Auth-Block)
- `CapabilitiesEndpoints.cs` (auth-Block: `{ enabled, loginUrl }`)
- Alle per-User-Repos (`MessageRepository`, `ChatClearMarkerRepository`,
  `AuditLogRepository`, `SuggestionRepository`, `SourceAccountRepository`,
  `SourceCursorRepository`, Web-Push-Repo) → `IUserContext`-Filter
- `GoogleAuthService` (UserId aus Context statt Konstante)
- Autonomer-Agent-Scheduler (Iteration über `users`, Scope pro User)
- `ChatEndpoints` (Session weiterhin "default", user-scoped)
- `frontend/src/api/client.ts`, `frontend/src/api/capabilities.ts`
