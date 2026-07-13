# FIAP Cloud Games — Catalog API

Catalog microservice for the FIAP Cloud Games Phase 2 platform. Owns the **game
catalog** (CRUD) and **user library reads** on the `fcg_catalog` database.

> **Milestone status: M3.** Game CRUD + library **read** endpoints. Validates
> JWTs issued by UsersAPI using the **same shared secret** (no call to UsersAPI).
> The purchase flow — `POST /api/library/acquire/{gameId}` producing
> `OrderPlacedEvent` — is added in M4. Promotions are deferred. No Kubernetes yet.

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
(read model) · JWT Bearer (validation only) · Swagger · Serilog ·
xUnit/Moq/FluentAssertions. Single-project layout with internal folders.

---

## Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/games` | Authenticated | List active games (Dapper) |
| GET | `/api/games/{id}` | Authenticated | Get a game |
| POST | `/api/games` | Admin | Create → 201 |
| PUT | `/api/games/{id}` | Admin | Update |
| DELETE | `/api/games/{id}` | Admin | Soft delete (`IsActive=false`) → 204 |
| GET | `/api/library/my-games` | Authenticated | Caller's library |
| GET | `/api/library/user/{userId}` | Admin | A user's library |
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

## Run locally (uses the M0 Postgres)

1. Start the M0 infrastructure (orchestration repo): `docker compose up -d`.
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
  fcg-catalog-api
```
