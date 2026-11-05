# syntax=docker/dockerfile:1
# Sprawl API + the built web app: one process serves the SPA, the API (UI listener :8080) and the device listener
# (:8081, MCP token only). Build from the repository root:
#   docker build --platform linux/amd64 -t ghcr.io/kar6677thik/sprawl:candidate .
FROM --platform=$BUILDPLATFORM node:24.15.0-bookworm-slim@sha256:4e6b70dd6cbfc88c8157ba19aa3d9f9cce6ba4703576d55459e45efcbc9c5f5d AS web
WORKDIR /web
COPY src/Sprawl.Web/package.json src/Sprawl.Web/package-lock.json src/Sprawl.Web/.npmrc ./
RUN npm ci --no-fund --no-audit
COPY src/Sprawl.Web/ ./
RUN npx tsc -p tsconfig.json --noEmit && npx vite build --outDir /web/dist --emptyOutDir

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS api
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/Sprawl.Contracts/Sprawl.Contracts.csproj src/Sprawl.Contracts/packages.lock.json src/Sprawl.Contracts/
COPY src/Sprawl.Domain/Sprawl.Domain.csproj src/Sprawl.Domain/packages.lock.json src/Sprawl.Domain/
COPY src/Sprawl.Api/Sprawl.Api.csproj src/Sprawl.Api/packages.lock.json src/Sprawl.Api/
RUN dotnet restore src/Sprawl.Api/Sprawl.Api.csproj --locked-mode
COPY src/Sprawl.Contracts/ src/Sprawl.Contracts/
COPY src/Sprawl.Domain/ src/Sprawl.Domain/
COPY src/Sprawl.Api/ src/Sprawl.Api/
COPY --from=web /web/dist/ src/Sprawl.Api/wwwroot/
RUN dotnet publish src/Sprawl.Api/Sprawl.Api.csproj --no-restore -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS runtime
WORKDIR /app
# Root-owned and read-only to the app user; state lives only on the /data volume.
COPY --from=api --chown=root:root /publish/ ./
ENV ASPNETCORE_HTTP_PORTS= ASPNETCORE_URLS="http://+:8080;http://+:8081" DOTNET_EnableDiagnostics=0 HOME=/tmp
# Numeric, so kubelet can verify runAsNonRoot without a runAsUser (Orbit catalog renders none).
USER $APP_UID
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "Sprawl.Api.dll"]
