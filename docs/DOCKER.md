# Docker one-click startup — rationale

All docs and code in this repository are in English (see `CLAUDE.md`).

This document explains the **why** behind `Dockerfile`, `docker-compose.yml`, and `.dockerignore`
at the repo root. It is a deployment path added *alongside* the existing Aspire dev flow
(`dotnet run --project src/AppHost`), not a replacement for it — Aspire's AppHost is not itself
containerized. Use `docker compose up` when you want the whole stack (Api + Postgres) running with
a single command and no .NET SDK installed on the host (e.g. quick demos, non-.NET reviewers).

## Base image tags: `10.0`

The solution targets `net10.0` (`Directory.Build.props`) with SDK `10.0.201` pinned in `global.json`.
The Dockerfile uses `mcr.microsoft.com/dotnet/sdk:10.0` to build/publish and
`mcr.microsoft.com/dotnet/aspnet:10.0` to run — matching tags avoid TFM/runtime mismatches.

## Why `dotnet publish` passes an explicit `-o /app/publish`

`Directory.Build.props` sets `<ArtifactsPath>` (the SDK's centralized "artifacts output" layout).
Without an explicit `-o`, `dotnet publish` would write output under that artifacts path instead of
a predictable location, breaking the `COPY --from=build /app/publish .` step in the runtime stage.
`-o` always overrides `ArtifactsPath`, so it must be passed explicitly.

## Why `.csproj` files are copied before the rest of the source

The Dockerfile copies only the six `.csproj` files (`Api`, `Application`, `Domain`,
`Infrastructure`, `ServiceDefaults`, `Shared`) plus `Directory.Build.props`,
`Directory.Packages.props`, and `global.json`, runs `dotnet restore`, and only then copies full
source. This is a standard Docker layer-caching trick: as long as no `.csproj`/central-package-version
file changes, the (usually slow) restore layer is reused on rebuilds even when application code
changes.

All six projects are copied because they're all transitively required to build `Api.csproj`:
`Api` → `Application`, `Infrastructure`, `ServiceDefaults`; `Infrastructure` → `Application`,
`Shared`; `Application` → `Domain`. There is no `.sln` in this repo, so the build targets
`src/Api/Api.csproj` directly.

## Why `ASPNETCORE_ENVIRONMENT=Development` in `docker-compose.yml`

`Api/Program.cs` only runs `InitialiseDatabaseAsync()` (EF Core migrations + seed data — 3 demo
patients) when `IsDevelopment()` is true. Forcing `Development` in the compose `api` service is the
only way to get a fully migrated, pre-seeded database with a single `docker compose up` and no
manual steps. Seeding is idempotent (`ApplicationDbContextInitialiser.TrySeedAsync` skips if the
`Calendars` table already has rows), so repeated `up` runs are safe.

## Why the connection string is injected via `environment:`, not baked into the image

The image only bakes in `ASPNETCORE_URLS=http://+:8080` (intrinsic to how it listens) and stays
otherwise environment-agnostic (12-factor style). The actual Postgres connection string is supplied
by `docker-compose.yml` as `ConnectionStrings__delivery-schedule-management-db`. The double
underscore is ASP.NET Core's environment-variable section separator for the config key
`ConnectionStrings:delivery-schedule-management-db` (the hyphenated name itself comes from the
`Services.Database` constant in `src/Shared/Services.cs`); the hyphens inside the name are literal
characters and don't interact with `__`. The `environment:` list form (`- KEY=VALUE`) is used
instead of the mapping form to avoid any ambiguity parsing a key that contains both `__` and `-`.

`EnrichNpgsqlDbContext` (Aspire's client integration, still used here) operates on a plain ADO.NET
Npgsql connection string under the hood, not an Aspire service-discovery URI — so a normal
`Host=postgres;Port=5432;...` string works correctly even without Aspire orchestrating the containers.

## Why Postgres has a healthcheck but the Api container doesn't

`postgres` has a `pg_isready` healthcheck, and `api` waits on it via
`depends_on: postgres: condition: service_healthy` — this is what actually matters for correct
startup ordering (the Api must not try to migrate against a Postgres that isn't accepting
connections yet). The `mcr.microsoft.com/dotnet/aspnet:10.0` runtime image doesn't ship `curl` or
`wget`, so adding a real HTTP healthcheck for the Api would mean installing a package purely for
that purpose. Given this is intentionally minimal, additive infra, that trade-off wasn't taken.

## Why Postgres data uses a named volume

`postgres-data` persists the database across `docker compose down` / `up` cycles. Data is only
wiped with `docker compose down -v` (or by deleting the volume directly).

## Note: `System.Security.Cryptography.Xml` version bump

Building the image surfaced a real issue unrelated to Docker itself: `Directory.Packages.props`
pinned `System.Security.Cryptography.Xml` to `10.0.9`, which has a high-severity DoS advisory
(GHSA-23rf-6693-g89p, CVE-2026-50648, patched in `10.0.10`). NuGet's audit check turns this into a
restore error under `TreatWarningsAsErrors=true` — a fresh container restore always evaluates the
audit, so it surfaces there even in setups where a cached local restore might not. Fixed by bumping
the pin to the patched `10.0.10`, not by suppressing the warning.

## Relationship to the Aspire dev flow

`dotnet run --project src/AppHost` remains the primary development flow (Aspire dashboard, service
discovery, its own Postgres container lifecycle). `docker compose up` is a separate, independent
path for running the built artifact without the .NET SDK — the two are not meant to run at the same
time against the same ports, and neither depends on the other.
