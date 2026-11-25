# syntax=docker/dockerfile:1
# Build context: the repository root. Build with: docker build -f src/CinePosto.Scraper/Dockerfile .

# The SDK version matches global.json, so the image builds with the same SDK as CI and local development.
ARG DOTNET_SDK_VERSION=10.0.300
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src

# Restore first, from project files only, so this layer is cached until a dependency changes.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/CinePosto.Domain/CinePosto.Domain.csproj src/CinePosto.Domain/
COPY src/CinePosto.Infrastructure/CinePosto.Infrastructure.csproj src/CinePosto.Infrastructure/
COPY src/CinePosto.Connectors/CinePosto.Connectors.csproj src/CinePosto.Connectors/
COPY src/CinePosto.Scraper/CinePosto.Scraper.csproj src/CinePosto.Scraper/
RUN dotnet restore src/CinePosto.Scraper/CinePosto.Scraper.csproj

COPY src/CinePosto.Domain/ src/CinePosto.Domain/
COPY src/CinePosto.Infrastructure/ src/CinePosto.Infrastructure/
COPY src/CinePosto.Connectors/ src/CinePosto.Connectors/
COPY src/CinePosto.Scraper/ src/CinePosto.Scraper/
RUN dotnet publish src/CinePosto.Scraper/CinePosto.Scraper.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# The scraper has no web server, so the plain runtime image is enough. It is the Debian variant (not chiseled
# or Alpine) because live mode needs the tzdata package to resolve Europe/Rome.
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app

# The database lives in /data. Creating it here, owned by the unprivileged user, lets a new named volume
# inherit that ownership.
RUN mkdir -p /data && chown "$APP_UID:$APP_UID" /data

ENV CINEPOSTO_DB_PATH=/data/cineposto.db

COPY --from=build /app/publish ./

# SCRAPER_MODE has no default on purpose: the caller must choose between fixtures and live.
USER $APP_UID
ENTRYPOINT ["dotnet", "CinePosto.Scraper.dll"]
