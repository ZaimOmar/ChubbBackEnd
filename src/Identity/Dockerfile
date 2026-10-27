# syntax=docker/dockerfile:1
# Build context is the repository root (docker-compose sets it), so Contracts and the
# central build props are reachable: docker build -f src/Identity/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:8.0.427-bookworm-slim@sha256:ff39a765f04619c8d146796fc09b38499ae5984f4a2a4c3a0a57d0022855daee AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until a dependency changes.
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./
COPY src/Contracts/*.csproj src/Contracts/
COPY src/Identity/*.csproj src/Identity/
RUN dotnet restore src/Identity/Identity.csproj

COPY src/Contracts src/Contracts
COPY src/Identity src/Identity
RUN dotnet publish src/Identity/Identity.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.33-bookworm-slim@sha256:63a1e42746a20795273c219f1577a8826644f12f67d829e818d8ccec92915493 AS final
WORKDIR /app
COPY --from=build /app/publish .

# The base image ships a non-root "app" user; never run as root. Listens on 8080 (.NET 8 default).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Identity.dll"]
