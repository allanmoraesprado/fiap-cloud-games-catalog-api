# FIAP Cloud Games — Catalog API

Catalog microservice for the FIAP Cloud Games Phase 2 platform. Owns the **game
catalog** (CRUD) and **user library reads** on the `fcg_catalog` database.

Independent .NET 8 API. Owns `fcg_catalog` (games + user library) and orchestrates
purchases: `POST /api/library/acquire/{gameId}` publishes **`OrderPlacedEvent`**
(409 if already owned), and CatalogAPI **consumes `PaymentProcessedEvent`** (group
`catalog-service`) to add the game to the library idempotently on approval.
Validates JWTs with the shared secret (no call to UsersAPI). Runs via Docker Compose
and on local Kubernetes (see `k8s/`); for the full system runbook and architecture
docs, see the **`fiap-cloud-games-orchestration`** repository.

### Events

| Direction | Event | Topic | Group / Key |
|---|---|---|---|
| Produce | `OrderPlacedEvent` | `fcg.orders.placed` | key `OrderId` |
| Consume | `PaymentProcessedEvent` | `fcg.payments.processed` | group `catalog-service` |

On `Approved` the game is added to `user_games` (idempotent via the unique
`(UserId, GameId)` index); on `Rejected` nothing is written.

Part of the five-repository solution (`users-api`, `catalog-api`,
`payments-api`, `notifications-api`, `orchestration`).

---

## Responsibilities

- Game CRUD (`/api/games`) — Admin writes, authenticated reads.
- Library reads (`/api/library/my-games`, `/api/library/user/{id}`).
- Validate JWTs locally (shared secret). **CatalogAPI never calls UsersAPI.**

Owns `games` and `user_games` on `fcg_catalog`. `user_games.UserId` is a plain
Guid taken from the JWT — there is **no users table and no cross-service FK**.

---

## Tech

.NET 8 · ASP.NET Core (Controllers) · EF Core 8 + Npgsql (write model) · Dapper
(read model) · **Redis distributed cache** (`IDistributedCache`, cache-aside, Phase 3) ·
JWT Bearer (validation only) · Swagger · Serilog · xUnit/Moq/FluentAssertions.
Single-project layout with internal folders.

---

## Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/games` | Authenticated | List active games (Dapper) — **cached** |
| GET | `/api/games/{id}` | Authenticated | Get a game — **cached** |
| POST | `/api/games` | Admin | Create → 201 — invalidates the list cache |
| PUT | `/api/games/{id}` | Admin | Update — invalidates list + game cache |
| DELETE | `/api/games/{id}` | Admin | Soft delete (`IsActive=false`) → 204 — invalidates list + game cache |
| POST | `/api/library/acquire/{gameId}` | Authenticated | Start purchase → **202** `{ orderId, status }`; **409** if already owned; publishes `OrderPlacedEvent` |
| GET | `/api/library/my-games` | Authenticated | Caller's library — **cached** per user |
| GET | `/api/library/user/{userId}` | Admin | A user's library — **cached** per user |
| GET | `/health` | public | Liveness |
| GET | `/swagger` | public | Swagger UI |

Seeded: 4 sample games. Library is empty until the purchase flow (M5) writes it.

---

## JWT (shared secret)

CatalogAPI **validates** tokens minted by UsersAPI. The `Jwt` values must match
UsersAPI **per environment**:

| Variable | Meaning | Local default |
|---|---|---|
| `ConnectionStrings__Postgres` | `fcg_catalog` connection string | `Host=localhost;Port=5432;Database=fcg_catalog;Username=fcg;Password=fcg` |
| `JWT__SECRETKEY` | **Same** shared signing key as UsersAPI | dev placeholder |
| `JWT__ISSUER` | Token issuer | `FiapCloudGames` |
| `JWT__AUDIENCE` | Token audience | `FiapCloudGames` |

Only local/development placeholders are committed.

---

## Redis cache (Phase 3)

Read operations use **Redis** as a **distributed cache** with **cache-aside** semantics:
the first request loads from PostgreSQL and stores the JSON result in Redis; while the entry
is valid the next requests are served from Redis. Writes **invalidate** the affected keys.
If Redis is unavailable the request is served from PostgreSQL, a warning is logged and the
API keeps working (no retry storm: `abortConnect=false`, 1 s timeouts).

| Key (prefix `fcg:catalog:`) | Endpoint | TTL | Invalidated by |
|---|---|---|---|
| `games:active` | `GET /api/games` | 60 s | create / update / delete game |
| `game:{id}` | `GET /api/games/{id}` | 60 s | update / delete of that game |
| `library:{userId}` | `GET /api/library/my-games`, `GET /api/library/user/{id}` | 60 s | approved `PaymentProcessedEvent` that adds a game to that user's library |

Rejected payments and idempotent "already owned" events do not touch the cache. Tokens,
sessions and authorization decisions are never cached.

Diagnostics: every cached read adds the response header **`X-FCG-Cache: HIT | MISS | BYPASS`**
(disable with `Redis__ExposeOutcomeHeader=false`) and logs `Cache HIT/MISS/SET/INVALIDATED/BYPASS`.

| Variable | Meaning | Local default |
|---|---|---|
| `Redis__Enabled` | `false` → no-op cache (every read BYPASS) | `true` |
| `Redis__ConnectionString` | StackExchange.Redis configuration (`redis:6379,...` in Compose) | `localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000` |
| `Redis__DefaultTtlSeconds` | Absolute TTL of every entry | `60` |
| `Redis__ExposeOutcomeHeader` | Emit the diagnostic header | `true` |

Code: `Infrastructure/Caching` (`RedisCatalogCache`, `CachedGameQueryService` decorator over
the Dapper read model, `CacheKeys`), invalidation in `GameService` and
`PurchaseCompletionService`, header in `Middleware/CacheOutcomeHeaderMiddleware`.

---

## Metrics (Phase 3)

`GET /metrics` (direct port only, not routed by Kong) exposes the default HTTP metrics from
`prometheus-net.AspNetCore` plus custom counters with low-cardinality labels only (no user,
game or order ids):

| Metric | Labels | Meaning |
|---|---|---|
| `fcg_events_published_total` | `topic`, `result` | `OrderPlacedEvent` publications |
| `fcg_events_consumed_total` | `topic`, `result` = `processed` \| `malformed` | `PaymentProcessedEvent` consumption |
| `fcg_catalog_payments_consumed_total` | `status` = `approved` \| `rejected` \| `other` | Payment events by status |
| `fcg_catalog_library_grants_total` | `result` = `added` \| `already_owned` \| `duplicate` \| `rejected` | Library grant outcomes |
| `fcg_cache_requests_total` | `outcome` = `hit` \| `miss` \| `bypass` | Cached reads |
| `fcg_cache_invalidations_total` | `target` = `games` \| `game` \| `library` | Cache invalidations |

Scraped by Prometheus and shown in the Grafana "FCG Overview" dashboard (orchestration repo,
`docs/observability.md`).

---

## Run locally (uses the M0 Postgres)

1. Start the infrastructure (orchestration repo): `docker compose up -d` (includes Redis; if
   you run without Redis set `Redis__Enabled=false`).
2. Run CatalogAPI:
   ```bash
   dotnet run --project src/CatalogApi --urls http://localhost:8082
   ```
   Applies the EF migration and seeds sample games on startup.
3. Run UsersAPI (`:8080`) to obtain a JWT; because the secret is shared, that
   token is accepted here.
4. Open `http://localhost:8082/swagger`.

## Test

```bash
dotnet test
```

## Docker

```bash
docker build -t fcg-catalog-api .
docker run --rm -p 8082:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;Database=fcg_catalog;Username=fcg;Password=fcg" \
  -e JWT__SECRETKEY="dev-only-change-me-please-min-32-characters-placeholder" \
  -e Redis__ConnectionString="host.docker.internal:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000" \
  fcg-catalog-api
```
