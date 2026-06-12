# Coolify-Hosting für NauAssist

Anleitung, worauf beim Deployment auf Coolify zu achten ist.

## Architektur in Kürze

- **Ein einziger .NET-Container** (`Dockerfile`) serviert **API + gebautes Frontend**
  (`wwwroot`) auf **Port 8080**. Compose ist nicht zwingend nötig — die Kern-App ist
  ein Container.
- **SQLite-DB unter `/app/data/nauassist.db`** hält **alles**: Settings, User, Chats,
  Kalender-Config **und die auto-generierten VAPID-Push-Keys**.
- Läuft als **Non-Root (UID 10001)**, hat einen **`/health`-Healthcheck**.
- Keycloak (Auth) und WhatsApp-Sidecar sind **opt-in** über Compose-Profile + Env.

---

## 1. Minimal-Setup (Single-User, empfohlen zum Start)

Nur die Haupt-App, ohne Auth/WhatsApp:

| Punkt | Wert / Hinweis |
|---|---|
| **Deploy-Typ** | Dockerfile (Repo-Root) oder das `nauassist`-Image |
| **Port** | `8080` → Coolify-Domain darauf mappen |
| **Persistent Volume** | **Pflicht:** `/app/data` als Coolify-Volume mounten. Ohne das sind nach jedem Redeploy alle Daten **und die Push-Keys** weg |
| **Healthcheck** | Pfad `/health` (existiert bereits) |
| **HTTPS** | Coolify/Traefik TLS aktivieren — **Pflicht** für PWA-Installation + Web-Push + Secure-Cookies |
| **Env** | `Time__Zone=Europe/Berlin` (Default ist schon gesetzt) |

> Die App liest bereits `X-Forwarded-Proto/Host` (ForwardedHeaders) und setzt
> Secure-Cookies `SameAsRequest` — hinter dem Coolify-Proxy funktioniert HTTPS damit
> out-of-the-box.

---

## 2. PWA & Web-Push — was HTTPS-seitig zählt

- **PWA-Installation und Push gehen ausschließlich über HTTPS** (oder localhost). Auf
  der Coolify-Domain mit gültigem Zertifikat erscheint „App installieren".
- **VAPID-Keys werden beim ersten Start automatisch generiert** und in der DB
  gespeichert (`VapidBootstrapper`). → **Kein** Env-Var nötig, **aber** das
  `/app/data`-Volume muss persistent sein, sonst wird bei jedem Deploy ein neuer Key
  erzeugt und alle bestehenden Push-Subscriptions sind tot.
- Der Service Worker (`sw.js`) cached die App-Shell versioniert (`nauassist-v1`) — bei
  UI-Updates ggf. `CACHE_VERSION` bumpen, sonst sehen installierte Clients alten
  Shell-Cache.

---

## 3. Auth aktivieren (Keycloak, BFF) — der knifflige Teil

Nur wenn Multi-User/Login gewünscht. Du brauchst **zwei** öffentliche (Sub-)Domains
und Compose-Deployment.

**App (z. B. `app.example.com`):**

```
Auth__Enabled=true
Auth__Authority=https://auth.example.com      # ÖFFENTLICHE Keycloak-URL (Browser-Redirect)
Auth__Realm=nauassist
Auth__ClientId=nauassist-web
Auth__ClientSecret=<secret>                    # als Coolify-Secret
Auth__InternalUrl=http://keycloak:8080         # internes Compose-Netz (Backchannel, kein TLS-Hairpin)
Auth__RequireHttpsMetadata=true
```

**Keycloak (eigene Domain `auth.example.com`, eigener Service):**

```
KC_HOSTNAME=https://auth.example.com
KC_PROXY_HEADERS=xforwarded
KC_HTTP_ENABLED=true
KEYCLOAK_ADMIN / KEYCLOAK_ADMIN_PASSWORD       # als Secrets
NAUASSIST_PUBLIC_URL=https://app.example.com   # steuert die Realm-Redirect-URIs!
NAUASSIST_OIDC_SECRET=<gleiches Auth__ClientSecret>
```

Fallstricke hier:

- **Keycloak braucht eine eigene Domain** und muss vom **Browser direkt erreichbar**
  sein (Login-Redirect). Die App spricht intern über `keycloak:8080` mit ihr → beide
  Container müssen im **selben Coolify-Netzwerk** liegen (Compose-Deployment erledigt
  das).
- **`NAUASSIST_PUBLIC_URL` muss exakt der App-Domain entsprechen** — daraus werden
  `redirectUris`/`webOrigins`/`post.logout`-URIs im Realm-Import gebaut. Stimmt das
  nicht, schlägt der Login mit „Invalid redirect URI" fehl.
- **`start-dev --import-realm` + H2** ist Entwicklungs-/Personal-Modus, **nicht
  produktionshart**. Für echten Betrieb später: `start` statt `start-dev`, Postgres
  statt H2. Das **`keycloak-data`-Volume muss persistent** sein, sonst sind nach
  Redeploy alle User weg.
- Profil aktivieren: `COMPOSE_PROFILES=auth` (bzw. `auth,whatsapp`).

---

## 4. WhatsApp-Sidecar (optional)

```
COMPOSE_PROFILES=whatsapp          # bzw. auth,whatsapp
WHATSAPP_ENABLED=true
WHATSAPP_SHARED_SECRET=<secret>
```

- Eigener Container, **nur intern** (`expose: 3000`, kein Host-Port) — nicht nach außen
  geben.
- Braucht **persistentes `/data`-Volume** (Baileys-Session), sonst QR-Login bei jedem
  Deploy neu.

---

## 5. Build & Images — Achtung

- Das `Dockerfile` läuft **`dotnet test` während des Builds** und baut Frontend (Node)
  + Backend (.NET SDK) multi-stage. Das ist **RAM-/CPU-hungrig** — auf einem kleinen
  Coolify-Server kann der Build OOM/timeouten. Ggf. größere Build-Ressourcen oder Image
  vorab in CI nach GHCR pushen und nur pullen.
- Die `docker-compose.yml` referenziert `ghcr.io/benediktnau/...`-Images. Sind die
  **privat**, in Coolify **GHCR-Registry-Credentials** hinterlegen.

---

## 6. Secrets-Hygiene

In Coolify als **Secrets** (nicht plain Env): `Auth__ClientSecret` /
`NAUASSIST_OIDC_SECRET`, `KEYCLOAK_ADMIN_PASSWORD`, `WHATSAPP_SHARED_SECRET`. Nichts
davon ins Repo.

---

## Quick-Checkliste

- [ ] `/app/data` persistent gemountet (sonst Datenverlust + tote Push-Keys)
- [ ] HTTPS/TLS aktiv (PWA + Push + Cookies)
- [ ] Port 8080, Healthcheck `/health`
- [ ] Auth: zwei Domains, `NAUASSIST_PUBLIC_URL` == App-Domain, `keycloak-data`
      persistent, `RequireHttpsMetadata=true`
- [ ] Profile via `COMPOSE_PROFILES`
- [ ] Secrets als Coolify-Secrets
- [ ] Build-Ressourcen ausreichend (oder Prebuilt-Image)
