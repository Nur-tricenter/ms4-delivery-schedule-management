# Arranque con un solo comando en Docker — justificación

Este documento se escribe en español por solicitud explícita del usuario (excepción puntual a la
regla de "todo en inglés" de `CLAUDE.md`, que sigue aplicando al resto del repositorio).

Este documento explica el **porqué** de `Dockerfile`, `docker-compose.yml` y `.dockerignore`
en la raíz del repo. Es una vía de despliegue añadida *junto a* el flujo de desarrollo con Aspire
existente (`dotnet run --project src/AppHost`), no un reemplazo — el AppHost de Aspire no está
contenerizado en sí mismo. Usa `docker compose up` cuando quieras levantar toda la pila (Api +
Postgres) con un solo comando y sin el SDK de .NET instalado en el host (por ejemplo, demos
rápidas, revisores que no usan .NET).

## Tags de la imagen base: `10.0`

La solución apunta a `net10.0` (`Directory.Build.props`) con el SDK `10.0.201` fijado en
`global.json`. El Dockerfile usa `mcr.microsoft.com/dotnet/sdk:10.0` para compilar/publicar y
`mcr.microsoft.com/dotnet/aspnet:10.0` para ejecutar — tags coincidentes evitan desajustes de
TFM/runtime.

## Por qué `dotnet publish` pasa un `-o /app/publish` explícito

`Directory.Build.props` define `<ArtifactsPath>` (el layout centralizado de "artifacts output" del
SDK). Sin un `-o` explícito, `dotnet publish` escribiría la salida bajo ese artifacts path en lugar
de una ubicación predecible, rompiendo el paso `COPY --from=build /app/publish .` en la etapa de
runtime. `-o` siempre sobrescribe `ArtifactsPath`, así que debe pasarse explícitamente.

## Por qué los `.csproj` se copian antes que el resto del código fuente

El Dockerfile copia solo los seis archivos `.csproj` (`Api`, `Application`, `Domain`,
`Infrastructure`, `ServiceDefaults`, `Shared`) más `Directory.Build.props`,
`Directory.Packages.props` y `global.json`, ejecuta `dotnet restore`, y solo después copia el
código fuente completo. Es el truco estándar de cacheo de capas de Docker: mientras no cambie
ningún `.csproj`/archivo central de versiones de paquetes, la capa de restore (normalmente lenta)
se reutiliza en rebuilds aunque cambie el código de la aplicación.

Se copian los seis proyectos porque todos son transitivamente necesarios para compilar
`Api.csproj`: `Api` → `Application`, `Infrastructure`, `ServiceDefaults`; `Infrastructure` →
`Application`, `Shared`; `Application` → `Domain`. No hay `.sln` en este repo, así que el build
apunta directamente a `src/Api/Api.csproj`.

## Por qué `ASPNETCORE_ENVIRONMENT=Development` en `docker-compose.yml`

`Api/Program.cs` solo ejecuta `InitialiseDatabaseAsync()` (migraciones de EF Core + datos semilla —
3 pacientes de demo) cuando `IsDevelopment()` es true. Forzar `Development` en el servicio `api`
del compose es la única forma de obtener una base de datos totalmente migrada y con datos
precargados con un solo `docker compose up`, sin pasos manuales. El seeding es idempotente
(`ApplicationDbContextInitialiser.TrySeedAsync` se salta si la tabla `Calendars` ya tiene filas),
así que repetir `up` es seguro.

## Por qué la cadena de conexión se inyecta vía `environment:` y no se hornea en la imagen

La imagen solo hornea `ASPNETCORE_URLS=http://+:8080` (intrínseco a cómo escucha) y por lo demás se
mantiene agnóstica al entorno (estilo 12-factor). La cadena de conexión real a Postgres la
suministra `docker-compose.yml` como `ConnectionStrings__delivery-schedule-management-db`. El doble
guión bajo es el separador de sección de variables de entorno de ASP.NET Core para la clave de
configuración `ConnectionStrings:delivery-schedule-management-db` (el nombre con guiones viene de
la constante `Services.Database` en `src/Shared/Services.cs`); los guiones dentro del nombre son
caracteres literales y no interactúan con `__`. Se usa la forma de lista `environment:`
(`- KEY=VALUE`) en lugar de la forma de mapa para evitar cualquier ambigüedad al parsear una clave
que contiene tanto `__` como `-`.

`EnrichNpgsqlDbContext` (la integración de cliente de Aspire, que se sigue usando aquí) opera sobre
una cadena de conexión ADO.NET Npgsql plana, no sobre una URI de service-discovery de Aspire — así
que una cadena normal `Host=postgres;Port=5432;...` funciona correctamente incluso sin que Aspire
orqueste los contenedores.

## Por qué Postgres tiene healthcheck pero el contenedor de la Api no

`postgres` tiene un healthcheck de `pg_isready`, y `api` espera a que esté listo vía
`depends_on: postgres: condition: service_healthy` — esto es lo que realmente importa para el
orden correcto de arranque (la Api no debe intentar migrar contra un Postgres que aún no acepta
conexiones). La imagen de runtime `mcr.microsoft.com/dotnet/aspnet:10.0` no trae `curl` ni `wget`,
así que añadir un healthcheck HTTP real para la Api implicaría instalar un paquete solo para eso.
Dado que esto es infraestructura intencionalmente mínima y aditiva, no se tomó ese trade-off.

## Por qué los datos de Postgres usan un volumen con nombre

`postgres-data` persiste la base de datos entre ciclos de `docker compose down` / `up`. Los datos
solo se borran con `docker compose down -v` (o eliminando el volumen directamente).

## Nota: bump de versión de `System.Security.Cryptography.Xml`

Al construir la imagen surgió un problema real, no relacionado con Docker en sí:
`Directory.Packages.props` fijaba `System.Security.Cryptography.Xml` en `10.0.9`, versión con un
aviso de DoS de severidad alta (GHSA-23rf-6693-g89p, CVE-2026-50648, corregido en `10.0.10`). El
chequeo de auditoría de NuGet convierte esto en un error de restore bajo
`TreatWarningsAsErrors=true` — un restore limpio en un contenedor siempre evalúa la auditoría, así
que aparece ahí incluso en configuraciones donde un restore local cacheado podría no mostrarlo. Se
corrigió subiendo el pin a la versión parcheada `10.0.10`, no suprimiendo el warning.

## Tercer servicio: `cache` (Redis)

Se agregó `cache` (`redis:7-alpine`) como tercer servicio del stack, con
healthcheck vía `redis-cli ping` (mismo patrón de `interval`/`timeout`/
`retries`/`start_period` que `postgres`). `api` ahora depende de **ambos**
(`postgres` y `cache`) con `condition: service_healthy`. Es infraestructura
pura para este ejercicio de Compose — la Api no lo consume todavía en código
(no hay `IDistributedCache`/cliente Redis wireado); agregar eso sería una
feature nueva, fuera de alcance acá.

## Por qué se quitó `container_name` de `api`

`postgres` y `cache` mantienen `container_name` fijo porque nunca se
escalan. `api` lo tenía también, pero un `container_name` fijo choca al
crear una segunda réplica (Compose no puede tener dos containers con el
mismo nombre) — así que escalar `api` fallaría por nombre duplicado antes
de siquiera llegar al problema real que se quería observar. Sin
`container_name`, Compose nombra las réplicas automáticamente
(`..._api-1`, `..._api-2`, ...).

## Qué pasa al escalar `api` (`docker compose up -d --scale api=3`)

Resultado real observado:

```
Error response from daemon: driver failed programming external connectivity
on endpoint delivery-schedule-management-api-2 (...): Bind for 0.0.0.0:8080
failed: port is already allocated
```

`api-1` arranca y queda `Up` normalmente. `api-2` y `api-3` se crean pero
quedan atascados en estado `Created` (nunca llegan a `Running`) porque
`ports: "8080:8080"` es un *binding fijo* al puerto 8080 del host — solo un
container puede reclamar ese puerto a la vez. `postgres` y `cache` no se
tocan (no estaban en `--scale`) y siguen `healthy` con 1 réplica cada uno,
sin verse afectados por el fallo de `api`.

Esto es intencional como lección: un puerto de host fijo es, en la
práctica, un límite duro de una sola réplica. Para escalar `api` de verdad
harían falta cualquiera de estas dos cosas (no aplicadas acá, quedan fuera
de este ejercicio):
- Quitar el mapeo fijo (`"8080:8080"` → `"8080"`) y dejar que Docker asigne
  un puerto efímero del host por réplica.
- Poner un reverse proxy / load balancer delante de las réplicas, que sea
  el único que publica un puerto al host.

Para volver a 1 réplica sana y limpiar los containers atascados en
`Created`: `docker compose up -d --scale api=1`.

## Configuración vía `.env` (creds/connection string parametrizados)

`docker-compose.yml` ya no hornea las creds de Postgres ni el connection
string de la Api como literales — usa `${VAR:-default}`, con el mismo valor
que antes como default. Sin ningún `.env` presente, `docker compose up`
funciona exactamente igual que antes (defaults = las creds dummy locales
`admin`/`password`).

`.env` está en `.gitignore` — nunca se commitea. `.env.example` (sí
trackeado) documenta las variables disponibles: `POSTGRES_DB`,
`POSTGRES_USER`, `POSTGRES_PASSWORD`, `DB_CONNECTION_STRING`. Para apuntar
`api` a Neon en vez del `postgres` local: copiar `.env.example` a `.env` y
reemplazar `DB_CONNECTION_STRING` por el connection string real de Neon
(agregando `SSL Mode=Require;Trust Server Certificate=true`, que Neon
exige) — el servicio `postgres` local puede seguir corriendo en paralelo
sin problema, sencillamente nadie le apunta.

Nota técnica: el healthcheck de `postgres` (`pg_isready -U $$POSTGRES_USER
-d $$POSTGRES_DB`) usa `$$` — el doble signo de pesos escapa la
interpolación de Compose para que la variable se resuelva en shell *dentro*
del container en tiempo de ejecución (donde sí existe como env var), no en
tiempo de parseo del yml (donde no aplica, es el healthcheck test, no una
env var de compose).

## Relación con el flujo de desarrollo de Aspire

`dotnet run --project src/AppHost` sigue siendo el flujo de desarrollo principal (dashboard de
Aspire, service discovery, su propio ciclo de vida del contenedor de Postgres). `docker compose up`
es una vía separada e independiente para ejecutar el artefacto ya compilado sin el SDK de .NET —
las dos no están pensadas para correr al mismo tiempo contra los mismos puertos, y ninguna depende
de la otra.
