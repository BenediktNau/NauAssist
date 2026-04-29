# NauAssist API — multi-stage Build (.NET 10)
# Build-Stage holt nur csproj/slnx zum Cachen der Restore-Schicht und
# kopiert erst dann die Quellen — damit ändert ein reiner Code-Commit
# nicht die NuGet-Cache-Schicht.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Restore-Schicht (Manifest + csprojs)
COPY src/NauAssist.slnx ./src/NauAssist.slnx
COPY src/Common/Common.csproj           ./src/Common/Common.csproj
COPY src/Memory/Memory.csproj           ./src/Memory/Memory.csproj
COPY src/Tools/Tools.csproj             ./src/Tools/Tools.csproj
COPY src/AICore/AICore.csproj           ./src/AICore/AICore.csproj
COPY src/Voice/Voice.csproj             ./src/Voice/Voice.csproj
COPY src/Extensions/Extensions.csproj   ./src/Extensions/Extensions.csproj
COPY src/Api/Api.csproj                 ./src/Api/Api.csproj
COPY src/Tests/Tests.csproj             ./src/Tests/Tests.csproj
RUN dotnet restore src/NauAssist.slnx -p:Configuration=Release

# 2) Quellen kopieren und publishen
COPY src/ ./src/
RUN dotnet publish src/Api/Api.csproj \
      -c Release \
      -o /publish \
      -p:UseAppHost=false \
      --no-restore

# Runtime-Stage — schlank, non-root, mit wget für den Healthcheck
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# wget ist im aspnet-Image nicht enthalten; benötigt für den compose-Healthcheck.
# Einmalige Installation, dann Cache aufräumen.
RUN apt-get update \
 && apt-get install --no-install-recommends -y wget \
 && rm -rf /var/lib/apt/lists/*

# Eigener User und Verzeichnislayout. /app enthält die binäre Kernwelt;
# Laufzeitstate liegt unter /var/nauassist und wird per Compose gemountet.
RUN useradd --create-home --uid 10001 nauassist \
 && mkdir -p /app /var/nauassist/extensions /var/nauassist/data /var/nauassist/logs /var/nauassist/models \
 && chown -R nauassist:nauassist /app /var/nauassist

WORKDIR /app
COPY --from=build --chown=nauassist:nauassist /publish ./

USER nauassist

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Paths__BaseDirectory=/var/nauassist \
    Paths__CoreRoot=/app \
    Paths__ExtensionsRoot=/var/nauassist/extensions \
    Paths__DataRoot=/var/nauassist/data \
    Paths__LogsRoot=/var/nauassist/logs \
    Paths__ModelsRoot=/var/nauassist/models

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=20s \
  CMD wget -qO- http://127.0.0.1:8080/health >/dev/null 2>&1 || exit 1

ENTRYPOINT ["dotnet", "NauAssist.Api.dll"]
