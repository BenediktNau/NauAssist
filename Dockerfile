# syntax=docker/dockerfile:1.7

# ── Frontend Build ─────────────────────────────────────────────
FROM node:26-alpine AS frontend-builder
WORKDIR /build
# Base-Image bringt npm 10.x mit; lokal wird mit npm 11 entwickelt — angleichen,
# damit Optional-Dep-Auflösung (z. B. @rolldown/binding-wasm32-wasi → @emnapi/*)
# konsistent ist und `npm ci` nicht "Missing from lock file" wirft.
RUN npm install -g npm@11
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend/ ./
RUN npm run build

# ── Backend Build + Tests ──────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-builder
WORKDIR /src

COPY src/Backend/Backend.csproj             src/Backend/
COPY src/Backend.Tests/Backend.Tests.csproj src/Backend.Tests/
RUN dotnet restore src/Backend.Tests/Backend.Tests.csproj

COPY src/Backend/        src/Backend/
COPY src/Backend.Tests/  src/Backend.Tests/
RUN dotnet test src/Backend.Tests/Backend.Tests.csproj \
      --configuration Release --no-restore --nologo
RUN dotnet publish src/Backend/Backend.csproj \
      --configuration Release --no-restore --no-self-contained \
      --output /publish

# ── Runtime ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl tzdata \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --gid 10001 nauassist \
 && useradd  --uid 10001 --gid 10001 --shell /usr/sbin/nologin --create-home nauassist \
 && mkdir -p /app/data \
 && chown -R 10001:10001 /app

WORKDIR /app
COPY --from=backend-builder  --chown=10001:10001 /publish/.     ./
COPY --from=frontend-builder --chown=10001:10001 /build/dist/.  ./wwwroot/

USER 10001:10001

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Persistence__DatabasePath=/app/data/nauassist.db \
    Time__Zone=Europe/Berlin

VOLUME ["/app/data"]
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "NauAssist.Backend.dll"]
