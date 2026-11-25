# syntax=docker/dockerfile:1
# Build context: the repository root. Build with: docker build -f src/CinePosto.Api/Dockerfile .

# The SDK version matches global.json, so the image builds with the same SDK as CI and local development.
ARG DOTNET_SDK_VERSION=10.0.300
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src

# Restore first, from project files only, so this layer is cached until a dependency changes.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/CinePosto.Domain/CinePosto.Domain.csproj src/CinePosto.Domain/
COPY src/CinePosto.Infrastructure/CinePosto.Infrastructure.csproj src/CinePosto.Infrastructure/
COPY src/CinePosto.Api/CinePosto.Api.csproj src/CinePosto.Api/
RUN dotnet restore src/CinePosto.Api/CinePosto.Api.csproj

COPY src/CinePosto.Domain/ src/CinePosto.Domain/
COPY src/CinePosto.Infrastructure/ src/CinePosto.Infrastructure/
COPY src/CinePosto.Api/ src/CinePosto.Api/
RUN dotnet publish src/CinePosto.Api/CinePosto.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app

# The database lives in /data. Creating it here, owned by the unprivileged user, lets a new named volume
# inherit that ownership.
RUN mkdir -p /data && chown "$APP_UID:$APP_UID" /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    CINEPOSTO_DB_PATH=/data/cineposto.db
EXPOSE 8080

COPY --from=build /app/publish ./

# No secrets are baked in: ADMIN_TOKEN is supplied when the container starts, and the app refuses to start without it.
USER $APP_UID
ENTRYPOINT ["dotnet", "CinePosto.Api.dll"]
