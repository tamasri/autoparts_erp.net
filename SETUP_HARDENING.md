# SETUP_HARDENING.md — AutoPartsERP

> **Isolation notice:** Setup for **`autoparts_erp.net`** (.NET 9 / PostgreSQL / React) only.

---

## 1. Prerequisites (pinned)

| Tool | Required version | Notes |
|---|---|---|
| .NET SDK | **9.0.312** (or newer 9.0 feature band) | Pinned in `global.json` (`rollForward: latestFeature`) |
| Node.js | 20 LTS or 22 LTS | Frontend uses Vite 6 / React 19; `@types/node@22` |
| Docker + Docker Compose | current | Provisions Postgres 16, Redis 7, Seq, pgAdmin |
| PostgreSQL | 16 (via Docker) | Must support `ltree` + `pgvector` extensions |

**Pin the SDK** — `global.json` (already present):
```json
{ "sdk": { "version": "9.0.312", "rollForward": "latestFeature" } }
```
Verify locally: `dotnet --version` should resolve to a 9.0.x band ≥ 9.0.312.

---

## 2. One-command dev startup

### Option A — Windows all-in-one (recommended on this machine)
From the repo root, this starts Docker infra, restores+builds the backend, installs frontend deps, and launches
API (`:5000`) + Vite (`:5173`), then waits on health checks:
```powershell
./run-local.ps1
```
Useful switches: `-SkipDocker`, `-SkipBuild`, `-SkipFrontendInstall`.
Logs land in `scripts/logs/` (`api.out.log`, `api.err.log`, `frontend.*`). Stop with `./scripts/stop-local.ps1`.

### Option B — Manual / cross-platform (3 terminals)
```bash
# 1) Infrastructure (Postgres 16, Redis 7, Seq, pgAdmin)
docker compose -f docker-compose.dev.yml up -d

# 2) API (auto-migrates + seeds on boot) -> http://localhost:5000
dotnet restore AutoPartsERP.sln --configfile NuGet.Config
dotnet run --project src/AutoPartsERP.Api --launch-profile Development

# 3) Frontend -> http://localhost:5173  (proxies /api and /hubs to :5000)
cd frontend
npm install
npm run dev
```

### Entry URLs
| Surface | URL |
|---|---|
| API base | http://localhost:5000 |
| API docs (Scalar) | http://localhost:5000/scalar/v1 |
| OpenAPI | http://localhost:5000/openapi/v1.json |
| Health | http://localhost:5000/health (`/health/live`, `/health/ready`) |
| Metrics (Prometheus) | http://localhost:5000/metrics |
| Hangfire dashboard | http://localhost:5000/hangfire |
| SignalR hub | ws://localhost:5000/hubs/erp |
| Frontend SPA | http://localhost:5173 |
| Seq logs | http://localhost:5341 |
| pgAdmin | http://localhost:5050 (admin@erp.local / admin) |

**Default admin login (dev seed):** `admin@autoparts.local` / `Admin@123456`.

---

## 3. EF Core migrations

Migrations **auto-apply on API startup** (non-`Testing`). To manage them manually:
```bash
# install the CLI once, matching the pinned SDK band
dotnet tool install --global dotnet-ef --version 9.*

# add a migration (startup = Api, project = Infrastructure)
dotnet ef migrations add <Name> \
  --project src/AutoPartsERP.Infrastructure \
  --startup-project src/AutoPartsERP.Api

# apply explicitly
dotnet ef database update \
  --project src/AutoPartsERP.Infrastructure \
  --startup-project src/AutoPartsERP.Api
```
> Migrations must respect snake_case naming and the `ltree` / `pgvector` column types already in the schema.

---

## 4. Tool version pinning guidelines

- **.NET SDK:** keep `global.json` authoritative; bump deliberately in a `chore:` commit.
- **NuGet:** `.csproj` uses floating minor bands (e.g. `MediatR 12.*`, `Npgsql...PostgreSQL 9.*`). For reproducible
  builds, consider adding a `Directory.Packages.props` (Central Package Management) and/or committing a
  `packages.lock.json` (`RestorePackagesWithLockFile=true`). Restore uses repo-local `NuGet.Config`.
- **Node/npm:** add an `.nvmrc` / `engines` field to pin Node 20|22. Commit `frontend/package-lock.json` for
  deterministic installs (`npm ci` in CI).
- **Docker images:** already pinned by tag (`postgres:16-alpine`, `redis:7-alpine`). Prefer digest pinning for prod.

---

## 5. Database setup checklist

- [ ] `docker compose -f docker-compose.dev.yml up -d` is healthy (`pg_isready` passes).
- [ ] Database `autoparts_erp`, user `erp_user`, password `erp_secret_dev` exist (compose creates them).
- [ ] Extensions available: **`ltree`** and **`pgvector`** (add `CREATE EXTENSION` in a migration if a bare
      Postgres image is used instead of a vector-enabled one).
- [ ] API boots and applies all 6 migrations (Identity, Governance, Party+Outbox, Operational Core, Inventory WMS,
      AI Foundation) with no errors.
- [ ] Seed ran: admin user present; demo data (`DemoDataSeeder`) present in dev.
- [ ] Hangfire schema created (jobs visible at `/hangfire`).
- [ ] Redis reachable (`/health/ready` green).

---

## 6. Environment variables / configuration checklist

Config is read from `appsettings*.json` and can be overridden by environment variables (double-underscore
section syntax). Keys observed in code:

| Key | Purpose | Dev default |
|---|---|---|
| `Database__ConnectionString` (or `ConnectionStrings__DefaultConnection`) | PostgreSQL | `Host=localhost;Port=5432;Database=autoparts_erp;Username=erp_user;Password=erp_secret_dev` |
| `Redis__ConnectionString` (or `ConnectionStrings__Redis`) | Redis | `localhost:6379,abortConnect=false` |
| `Jwt__Issuer` | JWT issuer | `AutoPartsERP` |
| `Jwt__Audience` | JWT audience | `AutoPartsERP-Client` |
| `Jwt__PrivateKeyPemBase64` | RS256 signing key (**secret**) | dev key in appsettings — **replace in prod** |
| `Jwt__PublicKeyPemBase64` | RS256 validation key | dev key in appsettings |
| `Jwt__AccessTokenExpiryMinutes` | access token TTL | `15` |
| `Jwt__RefreshTokenExpiryDays` | refresh token TTL | `7` |
| `AllowedOrigins` | CORS origins | `http://localhost:5173` |
| `Seq__Url` | Serilog Seq sink | `http://localhost:5341` |
| `ASPNETCORE_ENVIRONMENT` | env selector | `Development` (use `Testing` for integration tests) |

### Production hardening checklist
- [ ] **Rotate JWT keys** — never ship the dev PEM keys; inject `Jwt__PrivateKeyPemBase64` / `PublicKeyPemBase64`
      from a secret manager. Do not commit prod keys.
- [ ] **Tighten CORS** — replace `AllowAnyOrigin()` with the real origin list from `AllowedOrigins`.
- [ ] **Secrets out of `appsettings`** — DB password, Redis, JWT via env/secret store (`.env.prod.template` /
      `.env.vps.template` exist as starting points; keep real `.env` files untracked).
- [ ] **Disable Scalar/OpenAPI publicly** (or auth-gate) in prod if not desired.
- [ ] **Secure the Hangfire dashboard** (`HangfireAuthorizationFilter` exists — enforce real auth in prod).
- [ ] **HTTPS/Nginx** — terminate TLS at `nginx/` reverse proxy (`docker-compose.prod.yml` / `docker-compose.vps.yml`).
- [ ] **Migrations in prod** — decide between auto-migrate-on-boot vs a controlled `dotnet ef database update` step.
- [ ] **Backups** — schedule Postgres backups for `autoparts_erp` (financial + inventory data).
