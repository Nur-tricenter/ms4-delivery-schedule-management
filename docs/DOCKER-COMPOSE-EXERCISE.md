# Ejercicio de Docker Compose — MS4 (módulo 2, actividad 3)

Objetivo de la actividad: levantar un stack de 3 servicios con Docker
Compose (`api`, `db`, `cache`), configurar healthchecks y `depends_on` con
`condition`, levantar el stack y verificar red/healthchecks/DNS, y escalar
un servicio para observar qué pasa. Todo lo documentado acá corresponde a
comandos realmente ejecutados contra este repo, no a resultados teóricos.

---

## 1. Los 3 servicios

`docker-compose.yml` (raíz del repo) define:

| Servicio   | Imagen                          | Rol                                   | Publica puerto |
|------------|----------------------------------|----------------------------------------|----------------|
| `postgres` | `postgres:17-alpine`             | `db` — base de datos relacional        | `5432:5432`    |
| `cache`    | `redis:7-alpine`                 | `cache` — infraestructura, sin wiring en código C# todavía | (interno, sin publicar) |
| `api`      | build local (`Dockerfile`)       | Api de MS4 (ASP.NET Core / .NET 10)    | `8080:8080`    |

`cache` es infraestructura pura para este ejercicio — el objetivo es
demostrar orquestación de Compose (healthcheck, red, DNS, scaling), no
agregar una feature de caching real a la Api.

## 2. Healthchecks

`postgres`:
```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U $$POSTGRES_USER -d $$POSTGRES_DB"]
  interval: 5s
  timeout: 5s
  retries: 10
  start_period: 5s
```
`pg_isready` pregunta directamente al motor de Postgres si acepta
conexiones — más confiable que solo "el proceso está corriendo". El `$$`
escapa la interpolación de Compose para que la variable se resuelva dentro
del container en tiempo de ejecución, no al parsear el yml.

`cache`:
```yaml
healthcheck:
  test: ["CMD", "redis-cli", "ping"]
  interval: 5s
  timeout: 5s
  retries: 10
  start_period: 5s
```
`redis-cli ping` espera `PONG` — chequeo mínimo pero suficiente para saber
que Redis acepta comandos.

`api` **no tiene** healthcheck HTTP: la imagen runtime
(`mcr.microsoft.com/dotnet/aspnet:10.0`) no trae `curl`/`wget`, e instalar
uno solo para esto no se justificó en esta etapa (detalle completo en
`DOCKER.md`).

## 3. `depends_on` con `condition`

```yaml
api:
  depends_on:
    postgres:
      condition: service_healthy
    cache:
      condition: service_healthy
```
Sin `condition: service_healthy`, `depends_on` solo espera a que el
container arranque (`docker start`), no a que esté listo para recibir
conexiones — la Api podría arrancar antes de que Postgres acepte
conexiones y fallar la migración inicial. Con la condición, Compose espera
a que el healthcheck de cada dependencia pase antes de crear el container
de `api`.

## 4. Levantar el stack

```
docker compose up -d --build
```

Resultado real:
```
Container delivery-schedule-management-cache      Started
Container delivery-schedule-management-postgres   Started
Container delivery-schedule-management-cache      Healthy
Container delivery-schedule-management-postgres   Healthy
Container delivery-schedule-management-api-1      Starting
Container delivery-schedule-management-api-1      Started
```
`api-1` recién arranca *después* de que `cache` y `postgres` reportan
`Healthy` — el orden observado confirma que `depends_on: condition:
service_healthy` está funcionando como se espera.

`docker compose ps`:
```
NAME                                    STATUS                    PORTS
delivery-schedule-management-api-1      Up                        0.0.0.0:8080->8080/tcp
delivery-schedule-management-cache      Up (healthy)              6379/tcp
delivery-schedule-management-postgres   Up (healthy)              0.0.0.0:5432->5432/tcp
```

## 5. Verificación de red

```
docker network ls
```
```
NETWORK ID     NAME                                    DRIVER    SCOPE
0bced1d2a4b7   delivery-schedule-management_default    bridge    local
```
Compose crea automáticamente una red bridge propia del proyecto
(`<project>_default`) y conecta ahí los 3 servicios — están aislados de
otros stacks corriendo en la misma máquina, pero pueden hablarse entre sí
por nombre de servicio.

## 6. Verificación de DNS

Desde dentro del container `api`:
```
docker compose exec api getent hosts postgres
docker compose exec api getent hosts cache
```
```
172.20.0.3      postgres
172.20.0.2      cache
```
Confirma el mecanismo central de Compose: cada servicio es resoluble por
su **nombre de servicio** (no por IP fija) dentro de la red del proyecto —
por eso el connection string de `api` puede usar `Host=postgres` en vez de
una IP, y sigue funcionando aunque Compose recree el container de
`postgres` (y le asigne otra IP).

## 7. Escalar un servicio — qué pasa

```
docker compose up -d --scale api=3
```

Resultado real:
```
Container delivery-schedule-management-api-1  Running
Container delivery-schedule-management-api-3  Creating
Container delivery-schedule-management-api-2  Creating
Container delivery-schedule-management-postgres  Healthy
Container delivery-schedule-management-cache  Healthy
Container delivery-schedule-management-api-2  Starting
Error response from daemon: driver failed programming external connectivity
on endpoint delivery-schedule-management-api-2 (...): Bind for 0.0.0.0:8080
failed: port is already allocated
```

**Qué pasó:** `api-1` sigue `Up` normal. `api-2` y `api-3` se crean pero
`api-2` nunca llega a `Running` — se queda en estado `Created` — porque
`ports: "8080:8080"` es un *binding fijo* al puerto 8080 del host. Solo un
container puede reclamar ese puerto a la vez, así que la 2ª réplica
choca al intentar publicarlo. `postgres` y `cache` no se ven afectados
(no estaban en el `--scale`), siguen sanos con 1 réplica cada uno.

**Por qué importa:** un puerto de host fijo es, en la práctica, un límite
duro de una sola réplica. Requisito previo necesario, ya resuelto en este
compose: **quitar `container_name` fijo de `api`** — si se mantiene, la
2ª réplica falla antes incluso de llegar al conflicto de puerto (Compose
no permite dos containers con el mismo nombre). `postgres` y `cache`
mantienen `container_name` fijo porque nunca se escalan.

Para escalar `api` de verdad (fuera de alcance de este ejercicio, dejado
como conclusión):
- Quitar el mapeo fijo (`"8080:8080"` → `"8080"`) y dejar que Docker
  asigne un puerto efímero del host por réplica, o
- Poner un reverse proxy / load balancer delante de las réplicas, que sea
  el único que publica un puerto al host.

Para volver a 1 réplica sana y limpiar los containers atascados en
`Created`: `docker compose up -d --scale api=1`.

## 8. Publicar la imagen en Docker Hub

```
docker login
docker tag delivery-schedule-management-api:latest franklin120/delivery-schedule-management:latest
docker push franklin120/delivery-schedule-management:latest
```
Resultado: imagen publicada en
`docker.io/franklin120/delivery-schedule-management:latest`,
digest `sha256:b8aa8c7b1fed56369bb0b7c918aaff048c3bc3b628f636ab4920d1cf4adf543b`.

`docker login` se corrió con login web (device code) — nunca se manejaron
credenciales dentro de la sesión del agente, cada login/push requiere
sesión autenticada del usuario en su propia máquina.

## 9. Secrets fuera del repo (`.env`)

El connection string y las creds de Postgres ya no están hardcodeadas en
`docker-compose.yml` — se parametrizaron con `${VAR:-default}` (default =
mismas creds dummy locales de siempre, así que `docker compose up` sigue
funcionando igual sin ningún `.env` presente). `.env` (real, con
credenciales) está en `.gitignore` y nunca se commitea; `.env.example`
(sí trackeado) documenta qué variables existen. Detalle completo,
incluida la nota técnica del `$$` en el healthcheck, en
[`DOCKER.md`](./DOCKER.md#configuración-vía-env-credsconnection-string-parametrizados).

## 10. Limpieza de imágenes no usadas

```
docker image prune -f --filter "dangling=true"
```
Eliminó ~180 imágenes `<none>` (dangling, sin tag, sin ningún container
asociado — sobrantes de builds viejos de otros proyectos en la misma
máquina, algunos de hasta 23 meses). De ~90 imágenes bajó a 18. Se
conservó a propósito cualquier imagen con al menos un container asociado
(corriendo o parado), y cualquier imagen con tag pero 0 containers
(`pgvector`, `redis:alpine`, `n8n`, `pgadmin`, `postgres:16.2`, `alpine`) —
decisión conservadora del usuario, no relacionada con este repo
específicamente sino con el estado general de Docker en la máquina.

---

## Resumen para explicar la actividad

1. **3 servicios**: `api` (build local), `postgres` (`db`), `cache`
   (`redis`).
2. **Healthchecks** en `postgres` (`pg_isready`) y `cache` (`redis-cli
   ping`); `api` sin healthcheck HTTP por limitación de la imagen base.
3. **`depends_on: condition: service_healthy`** en `api` para ambas
   dependencias — arranca solo después de que estén realmente listas, no
   solo "iniciadas".
4. **Red**: Compose crea una bridge network propia del proyecto; los 3
   servicios se resuelven entre sí por **nombre de servicio** (DNS interno
   de Compose), verificado con `getent hosts`.
5. **Scaling**: escalar `api` a 3 réplicas falla por el puerto fijo
   (`8080:8080`) — comportamiento esperado y documentado, no un bug.
   `container_name` se quitó de `api` (no de `postgres`/`cache`) para que
   el intento de escalar llegue siquiera a ese punto.
