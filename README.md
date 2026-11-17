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

Part of the six-repository solution (`users-api`, `catalog-api`, `payments-api`,
`notifications-function`, `notifications-api` [Phase 2 history], `orchestration`). In Phase 3
it is reached through the **Kong API Gateway** (`/api/games/*` and `/api/library/*`
JWT-protected at the edge and here).

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
**Elasticsearch 9** game search (`Elastic.Clients.Elasticsearch`, Phase 4) ·
JWT Bearer (validation only) · Swagger · Serilog · xUnit/Moq/FluentAssertions.
Single-project layout with internal folders.

---

## Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/games` | Authenticated | List active games (Dapper) — **cached** |
| GET | `/api/games/search?q=&genre=&page=&pageSize=` | Authenticated | **Full-text fuzzy search** (Elasticsearch), relevance-ordered, paginated; 400 on invalid input, 503 when the engine is unavailable |
| POST | `/api/games/search/reindex?recreate=` | Admin | Rebuild the search index from PostgreSQL |
| GET | `/api/games/{id}` | Authenticated | Get a game — **cached** |
| POST | `/api/games` | Admin | Create → 201 — invalidates the list cache, indexes the game |
| PUT | `/api/games/{id}` | Admin | Update — invalidates list + game cache, re-indexes the game |
| DELETE | `/api/games/{id}` | Admin | Soft delete (`IsActive=false`) → 204 — invalidates list + game cache, hides it from search |
| POST | `/api/library/acquire/{gameId}` | Authenticated | Start purchase → **202** `{ orderId, status }`; **409** if already owned; publishes `OrderPlacedEvent` |
| GET | `/api/library/my-games` | Authenticated | Caller's library — **cached** per user |
| GET | `/api/library/user/{userId}` | Admin | A user's library — **cached** per user |
| GET | `/health` | public | Liveness |
| GET | `/swagger` | public | Swagger UI |

Seeded: 4 sample games. Library is empty until the purchase flow writes it.

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

## Game search (Phase 4, Elasticsearch)

`GET /api/games/search` answers from a dedicated **Elasticsearch** index (`fcg-games`), never
from PostgreSQL:

- **Query**: fuzzy `multi_match` (`fuzziness: AUTO`, first letter must match) over `title` (×3),
  `genre` (×2) and `description`; an exact title phrase/term adds a strong boost, so exact
  matches rank first while typos such as `Cosmik Odisey` still find *Cosmic Odyssey*. Text is
  lower-cased and accent-folded (`asciifolding`). Active games only; ordered by `_score`,
  ties by title.
- **Input**: `q` 2–100 chars (required), optional `genre` (exact, case-insensitive),
  `page` ≥ 1, `pageSize` 1–50, at most the first 10 000 results — otherwise **400**.
- **Response**: `{ query, genre, page, pageSize, total, tookMs, items: [{ id, title, description, genre, price, releaseDate, score }] }`.
- **Engine unavailable**: **503** with `Retry-After`; `/health` does not depend on the engine,
  so the rest of the API keeps serving.

**Index synchronization** (PostgreSQL is the source of truth, no distributed transaction):

1. Create/update/delete commit to PostgreSQL first, then upsert the document (best effort,
   `refresh=wait_for` so the admin reads their own write). A failure is logged and counted, never
   returned to the caller.
2. `SearchIndexReconciler` (background job) re-indexes every game whose `UpdatedAt` is newer
   than its last successful run minus an overlap. The first run — and any run that finds the
   index missing (e.g. after data loss) — does a **full load**: this is the initial indexing.
3. `POST /api/games/search/reindex` (Admin) rebuilds everything on demand; `recreate=true`
   drops the index first (mapping changes).

Soft-deleted games stay in the index with `isActive=false` and are filtered out. Upserts are
idempotent (document id = game id), so several replicas can run the job concurrently.

| Variable | Meaning | Local default |
|---|---|---|
| `Elasticsearch__Enabled` | `false` → search answers 503, index writes skipped | `true` |
| `Elasticsearch__Url` | Cluster endpoint (`http://elasticsearch:9200` in Compose) | `http://localhost:9200` |
| `Elasticsearch__CloudId` | Elastic Cloud ID (used instead of the URL when set together with an API key) | empty |
| `Elasticsearch__ApiKey` | Base64 API key for managed clusters — **secret**, never committed | empty |
| `Elasticsearch__IndexName` | Index name | `fcg-games` |
| `Elasticsearch__RequestTimeoutSeconds` | Per-request timeout | `5` |
| `Elasticsearch__ReconcileIntervalSeconds` / `__ReconcileOverlapSeconds` | Reconciliation period / overlap | `30` / `30` |

Code: `Infrastructure/Search` (`ElasticGameSearchService`, `ElasticGameIndexer` with the index
mapping, `SearchIndexReconciler`), `Application/Services/GameSearchSync`, validation in
`Application/Validators/GameSearchValidator`.

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
| `fcg_search_requests_total` | `outcome` = `results` \| `empty` \| `invalid` \| `unavailable` | Game searches |
| `fcg_search_duration_seconds` | — | Search round-trip histogram |
| `fcg_search_index_operations_total` | `operation` = `upsert` \| `bulk` \| `reconcile` \| `reindex`, `result` = `success` \| `failure` | Index writes |

Scraped by Prometheus and shown in the Grafana "FCG Overview" dashboard (orchestration repo,
`docs/observability.md`).

---

## Run locally (uses the Compose Postgres)

1. Start the infrastructure (orchestration repo): `docker compose up -d` (includes Redis and
   Elasticsearch; without them set `Redis__Enabled=false` / `Elasticsearch__Enabled=false`).
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
dotnet test                                     # unit + integration
dotnet test --filter "Category!=Integration"    # unit only (no Docker needed)
dotnet test --filter "Category=Integration"     # real Elasticsearch through Testcontainers
```

Integration tests start a disposable `elasticsearch:9.5.5` container (Docker required; override
the image with `FCG_TEST_ELASTICSEARCH_IMAGE`) and cover exact, misspelled and relevance-ordered
searches, empty results, pagination, genre filter, create/update/delete → index, full reindex,
lost-index recovery and an engine outage followed by recovery.

## Docker

```bash
docker build -t fcg-catalog-api .
docker run --rm -p 8082:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;Database=fcg_catalog;Username=fcg;Password=fcg" \
  -e JWT__SECRETKEY="dev-only-change-me-please-min-32-characters-placeholder" \
  -e Redis__ConnectionString="host.docker.internal:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000" \
  fcg-catalog-api
```

---

## Cloud deployment (Phase 4: AKS + CI/CD)

- **Manifests**: `k8s/azure/` (Deployment, Service, ConfigMap) rendered by the pipeline with the ACR
  login server and the **commit SHA** as image tag. `k8s/` keeps the local (Docker Desktop) manifests.
- **Pipeline**: `.github/workflows/ci-cd.yml` calls the shared workflow of the orchestration repository
  (`dotnet-service.yml`): CI on every push/PR (build, tests, Docker build, Trivy scan); CD on `main` only
  (OIDC login to Azure, push to the private ACR, rolling update on AKS with automatic rollback, smoke test
  through the public Kong gateway).
- **Secrets**: none in the manifests. On AKS the Azure Key Vault CSI driver mounts the service's secrets as
  files (Workload Identity) and the API loads them as configuration when `SecretsPath` is set
  (`Configuration/RuntimeConfiguration.cs`; file `Jwt__SecretKey` -> key `Jwt:SecretKey`). Without
  `SecretsPath` (local runs) nothing changes.
- **Version**: the image carries `APP_VERSION` (commit SHA); every response has `X-App-Version`.
- In the cloud the search engine is Elastic Cloud: `Elasticsearch__Url` and `Elasticsearch__ApiKey` are mounted from Key Vault.

Details: orchestration repository, `docs/phase4/` (CICD.md, KEYVAULT.md, PROVISIONING.md).
