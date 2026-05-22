# Docker-Deployment + GHCR-Release-Pipeline

## Ziel

NauAssist soll als Docker-Image bereitgestellt und auf einem Heim-Server / einer anderen Maschine im LAN deployed werden können. Ein einziges Image enthält Backend + (statisches) Frontend. Releases werden automatisch via GitHub Actions auf `ghcr.io/benediktnau/nauassist` gepublished.

## Scope (schmaler Pfad)

- Multi-Stage-`Dockerfile` (frontend-build → backend-build → runtime).
- `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")` in `Program.cs`, damit das Backend das gebaute Frontend mit ausliefert.
- Healthcheck via existierenden `/health`-Endpoint.
- Non-root-User im Image.
- `.dockerignore`, damit `bin/`, `obj/`, `node_modules/` nicht in den Build-Context wandern.
- GitHub-Actions-Workflow `.github/workflows/release.yml` mit Build, Test, GHCR-Push.

Explizit **nicht** in diesem Scope:

- HTTPS-Termination / Reverse Proxy (Caddy/Traefik werden vorgelagert, nicht Teil des Images).
- docker-compose.yml — `docker run` reicht für Test-Deployment; kann später ergänzt werden.
- Auto-Update-Mechanismus (Watchtower o.ä.).
- Multi-Arch — nur `linux/amd64`. arm64 lässt sich später per Buildx-Manifest nachziehen.
- Image-Signing / Vulnerability-Scanning.
- Skalierung (Replicas, Sharding).

## Architektur

```
Build-Time                                Runtime
──────────────────────                    ─────────────────────────────────
[frontend-builder]                        ghcr.io/benediktnau/nauassist
node:22-alpine                            mcr.microsoft.com/dotnet/aspnet:10.0
  npm ci, npm run build         ┐         ├─ /app/NauAssist.Backend (publish)
  → /build/dist/                ├──→      ├─ /app/wwwroot/ (frontend dist)
                                │         ├─ /app/data/   ← VOLUME
[backend-builder]               │           (nauassist.db + google-credentials.json)
mcr.microsoft.com/dotnet/sdk:10.0│
  dotnet test                   │         Listens on http://+:8080
  dotnet publish -c Release -o /publish   Runs as UID 10001 (non-root)
  → /publish/                   ┘         Healthcheck: GET /health
```

Single-Container, kein docker-compose nötig. Frontend wird zur Build-Zeit als statisches Asset ins Backend-Image kompiliert. Zur Laufzeit liefert ASP.NET die Vite-Build-Output-Files aus `/app/wwwroot/` aus; alles Unbekannte fällt auf `index.html` zurück (SPA-Routing). API-Aufrufe `/api/*` werden direkt vom Backend bedient.

Persistenz und Sekrete (DB, Google-OAuth-Credentials) liegen in einem gemounteten Host-Volume `/app/data`. Ollama läuft außerhalb des Containers (Host oder andere LAN-Maschine) und wird via `Ollama__Host`-ENV adressiert.

## Backend-Anpassungen (`Program.cs`)

Vor den Endpoint-Mappings, nach `var app = builder.Build();`:

```csharp
app.UseStaticFiles();
```

Nach allen `app.Map…Endpoints()`-Aufrufen:

```csharp
app.MapFallbackToFile("index.html");
```

Das ist die einzige produktive Code-Änderung. Im Development bleibt das harmlos (kein `wwwroot/` vorhanden → kein Fallback aktiv außer für nicht-API-Routen).

## Dockerfile

```dockerfile
# syntax=docker/dockerfile:1.7

# ── Frontend Build ──────────────────────────────────────────
FROM node:22-alpine AS frontend-builder
WORKDIR /build
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ── Backend Build ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-builder
WORKDIR /src
COPY NauAssist.sln ./
COPY src/Backend/Backend.csproj src/Backend/
COPY src/Backend.Tests/Backend.Tests.csproj src/Backend.Tests/
RUN dotnet restore NauAssist.sln
COPY src/ src/
RUN dotnet test src/Backend.Tests/Backend.Tests.csproj \
    --configuration Release --no-restore --nologo
RUN dotnet publish src/Backend/Backend.csproj \
    --configuration Release --no-restore --no-self-contained \
    --output /publish

# ── Runtime ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl tzdata \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --gid 10001 nauassist \
 && useradd  --uid 10001 --gid 10001 --shell /usr/sbin/nologin --create-home nauassist \
 && mkdir -p /app/data \
 && chown -R 10001:10001 /app

WORKDIR /app
COPY --from=backend-builder  --chown=10001:10001 /publish/.      ./
COPY --from=frontend-builder --chown=10001:10001 /build/dist/.   ./wwwroot/

USER 10001:10001

ENV ASPNETCORE_URLS=http://+:8080 \
    Persistence__DatabasePath=/app/data/nauassist.db \
    Calendar__GoogleCredentialsPath=/app/data/google-credentials.json \
    Time__Zone=Europe/Berlin

VOLUME ["/app/data"]
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "NauAssist.Backend.dll"]
```

Annahme: Im Repo-Root liegt `NauAssist.sln`. Falls die Solution-Datei woanders heißt, an die Realität anpassen. Falls keine Solution existiert, das `dotnet restore` direkt gegen das `.csproj` laufen lassen.

## `.dockerignore`

```
**/bin
**/obj
**/node_modules
**/.vs
**/.vscode
**/.idea
src/Backend/data
docs
.github
.git
.gitignore
.claude
*.md
```

`src/Backend/data` raus, damit lokale DB / Credentials NICHT im Build-Context landen. Image bekommt einen leeren `/app/data`-Mountpoint.

## Konfiguration via ENV (Container-Defaults überschreibbar)

| ENV-Variable                          | Default im Image                                  | Bedeutung                                       |
| ------------------------------------- | ------------------------------------------------- | ----------------------------------------------- |
| `ASPNETCORE_URLS`                     | `http://+:8080`                                   | Kestrel-Bind                                    |
| `Persistence__DatabasePath`           | `/app/data/nauassist.db`                          | SQLite-Datei                                    |
| `Calendar__GoogleCredentialsPath`     | `/app/data/google-credentials.json`               | OAuth-Client-Secret + Tokens                    |
| `Time__Zone`                          | `Europe/Berlin`                                   | TZ für Slot-Berechnung                          |
| `Ollama__Host`                        | `http://localhost:11434` (aus `appsettings.json`) | LAN-IP des Ollama-Hosts überschreiben          |
| `Gemini__BaseAddress`                 | `https://generativelanguage.googleapis.com/...`   | Selten zu ändern                                |

Provider-Wahl + Gemini-API-Key bleibt zur Laufzeit über die SettingsPage änderbar (DB-persistiert, nicht via ENV).

## Google-OAuth: Erstinstallation

Das `auth`-Subcommand in `Program.cs` läuft interaktiv. Auf der Zielmaschine:

```bash
# Einmalig: Google-OAuth-Client-Secret-JSON ablegen
mkdir -p /opt/nauassist/data
cp ~/google-credentials.json /opt/nauassist/data/

# Auth-Flow interaktiv ausführen
docker run --rm -it \
  -v /opt/nauassist/data:/app/data \
  ghcr.io/benediktnau/nauassist:latest auth

# Token wird in /opt/nauassist/data/ persistiert.
```

Anschließend regulärer Service-Start (siehe unten).

## Service-Start auf Zielmaschine

```bash
docker pull ghcr.io/benediktnau/nauassist:latest

docker run -d --restart unless-stopped \
  --name nauassist \
  -p 8080:8080 \
  -v /opt/nauassist/data:/app/data \
  -e Ollama__Host=http://192.168.1.50:11434 \
  -e Time__Zone=Europe/Berlin \
  ghcr.io/benediktnau/nauassist:latest
```

UI erreichbar unter `http://<docker-host>:8080`. Settings → AI-Provider/Modell/Gemini-Key wie gewohnt via UI.

## GitHub-Actions-Workflow

`.github/workflows/release.yml`:

```yaml
name: release

on:
  push:
    branches: [main]
    tags: ['v*']

permissions:
  contents: read
  packages: write

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}   # benediktnau/NauAssist → lowercase via metadata-action

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=ref,event=branch
            type=ref,event=tag
            type=sha,prefix=sha-,format=short
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=raw,value=latest,enable=${{ startsWith(github.ref, 'refs/tags/v') }}
          flavor: |
            latest=false

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          file: ./Dockerfile
          platforms: linux/amd64
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

Tests laufen innerhalb des Backend-Build-Stages im Dockerfile — kein separater `dotnet test`-Step im Workflow nötig. Falls ein Test schlägt fehl, schlägt `docker build` fehl, und nichts wird gepushed.

### Resultierende Tags

| Trigger                       | Tags                                                              |
| ----------------------------- | ----------------------------------------------------------------- |
| `push` → `main`               | `:main`, `:sha-<short>`                                           |
| `push` → tag `v0.1.0`         | `:0.1.0`, `:0.1`, `:latest`, `:sha-<short>`, `:v0.1.0`            |

## Erstes Release

Manueller Bootstrap nach Merge dieses Specs:

```bash
git tag v0.1.0
git push origin v0.1.0
```

→ Pipeline läuft, Image steht unter `ghcr.io/benediktnau/nauassist:0.1.0` und `:latest`.

## Sichtbarkeit GHCR-Package

Per Default ist ein neues GHCR-Package privat (nur Repo-Owner kann pullen). Für persönliche Test-Deployment ist das in Ordnung — `docker login ghcr.io` mit Personal Access Token (Scope `read:packages`) reicht.

Falls später öffentlich gewünscht: GitHub → Profil → Packages → `nauassist` → Settings → "Change visibility" → public.

## Tests / Verifikation

Backend-Test-Suite (177 Tests) läuft im Dockerfile-Build. Zusätzlich manuell nach erstem Release:

1. `docker pull ghcr.io/benediktnau/nauassist:latest` auf Zielmaschine.
2. `docker run … auth` → Google-Flow vollständig durchklicken, Token erscheint in `/opt/nauassist/data/`.
3. Regulär starten, `curl http://localhost:8080/health` → `200 OK`.
4. Browser `http://<docker-host>:8080` → SettingsPage öffnet sich.
5. Test-Chat mit Ollama-Provider (LAN-IP) — Tool-Calls funktionieren.
6. Provider auf Gemini umstellen → Anfrage funktioniert.
7. Container neu starten → DB + Settings + Token überleben (Volume-Persistenz).

## Migration / Risiken

- **DB-Permissions:** Aktuell setzt `DbInitializer` `0600` auf die DB-Datei. Im Container läuft das als UID 10001 — funktioniert, solange der Host-Mount für 10001 schreibbar ist. Doku ergänzt: `chown -R 10001:10001 /opt/nauassist/data` auf Host vor erstem Start.
- **Token-Persistenz:** Google-OAuth-Token landet in derselben `google-credentials.json` (Library-Default). Liegt im Volume → überlebt Container-Recreate. Falls die Token-Datei vom Library getrennt liegt: Volume-Pfad ggf. nachziehen.
- **Solution-Datei vs. einzelne csproj:** Falls keine `.sln` vorhanden, `dotnet restore`/`test`/`publish` direkt gegen die `.csproj` ausführen. Im Implementation-Plan wird das vorab geprüft.
- **`UseStaticFiles` ohne `wwwroot`:** Im Development-Modus existiert `wwwroot/` nicht. `UseStaticFiles()` ist dann no-op. `MapFallbackToFile("index.html")` würde im Dev-Modus auf eine nicht-existente Datei zeigen — fängt aber nur unbekannte Routen ab. Tests laufen weiter, Vite-Dev-Proxy unverändert. Ggf. nur in `Production`-Environment aktivieren — entscheidet sich im Plan.
