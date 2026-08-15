# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# --- Restore layer: copy only what's needed to resolve NuGet packages, for caching ---
COPY Directory.Build.props Directory.Packages.props global.json ./

COPY src/Api/Api.csproj src/Api/
COPY src/Application/Application.csproj src/Application/
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/ServiceDefaults/ServiceDefaults.csproj src/ServiceDefaults/
COPY src/Shared/Shared.csproj src/Shared/

RUN dotnet restore src/Api/Api.csproj

# --- Build/publish layer: copy full source now that restore is cached ---
COPY src/Api/ src/Api/
COPY src/Application/ src/Application/
COPY src/Domain/ src/Domain/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/ServiceDefaults/ src/ServiceDefaults/
COPY src/Shared/ src/Shared/

RUN dotnet publish src/Api/Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "delivery_schedule_management.Api.dll"]
