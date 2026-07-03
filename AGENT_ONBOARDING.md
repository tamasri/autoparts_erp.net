# AGENT_ONBOARDING.md — AutoPartsERP

> **READ THIS FIRST.** Any AI agent (or human) starting work on `autoparts_erp.net` must read this file
> completely before writing a single line of code.

---

## 1. Starter Prompt (paste into any agent working on this repo)

```
You are working ONLY on the `autoparts_erp.net` repository.

HARD CONTEXT:
- This is a .NET 9 Clean Architecture ERP for an automotive spare-parts business.
- Stack: ASP.NET Core Minimal APIs + Carter, MediatR (CQRS) with pipeline behaviors,
  EF Core 9 + Dapper on PostgreSQL 16, ASP.NET Identity + JWT RS256, Redis, Hangfire,
  SignalR, Audit.NET, OpenTelemetry, Semantic Kernel + pgvector. Frontend: React 19 +
  Vite 6 + TypeScript + MUI 6 (RTL/Arabic) + Zustand + TanStack Query + i18next.
- Projects: Domain -> Contracts -> Application -> Infrastructure -> Api (Clean Architecture
  dependency rule is absolute). Tests: UnitTests, IntegrationTests, E2ETests. SPA in /frontend.

ABSOLUTE RULES:
1. This project is ISOLATED. Do NOT import any stack, tooling, pattern, env var, port, or
   convention from any Next.js / NestJS / TypeScript-backend project. Those do not exist here.
2. All cross-cutting rules (authorization, idempotency, period-lock, maker-checker, audit) are
   enforced via MediatR pipeline behaviors and marker interfaces on commands. Do NOT reimplement
   them in handlers or endpoints.
3. Business outcomes use Result / Result<T> + Error. Do not throw for expected failures.
4. Database is snake_case (auto-converted in AppDbContext). Reads = Dapper SQL in snake_case
   with AS-aliases; writes/invariants = Domain entity factories + EF/parameterized SQL.
5. Every stateful write must consciously decide: which PermissionCode guards it, is it idempotent,
   is it period-sensitive, does it need maker-checker approval, must it be audited. Encode each via
   the marker interfaces (IAuthorizedRequest, IIdempotentRequest, IPeriodSensitiveRequest,
   IMakerCheckerRequest, IAuditableRequest).
6. Build must pass with TreatWarningsAsErrors=true. Warnings are errors.
7. All UI strings via i18next (ar.json/en.json). Arabic is the primary end-user language; keep RTL.
8. Follow the feature-folder convention under Application/Features/<Domain>/<Action>/.

BEFORE CODING: run a context check — `git log --oneline -20`, `git status`, and read the target
module + its Domain entity + Contracts DTOs. Do not modify code you have not read.

DELIVERY CADENCE: Build -> Stop -> Summarize (Features, Files, Dependencies, Next Steps) -> wait
for approval before the next phase.
```

---

## 2. Confirmed Current State (verified from the codebase)

**Repository:** `https://github.com/tamasri/autoparts_erp.net.git` (remote `origin`, branch `main`).

**Recent history (git log):**
```
fec10f9 fix: login flow + vite proxy + auth store + EF shadow property
e2e881e fix: local run - identity schema migration + constructor bindings
21943b8 feat: phase 3.5 operational core scaffold
6138642 feat: phase 3 operational core
7b13b07 feat: Phase 2 Operational Core - complete
d982f21 feat: Phase 1 Governance Layer - complete scaffold
```
The project has progressed through **Phase 1 (Governance) → Phase 2/3/3.5 (Operational Core)** and a login/local-run
fix pass. There are **substantial uncommitted working-tree changes** (new frontend pages, endpoints, `DemoDataSeeder.cs`,
VPS compose/env templates, local run scripts) — treat the working tree, not just the last commit, as current state.

**What is built and working:**
- Full Clean Architecture solution (5 src projects + 3 test projects) that composes in `Program.cs`.
- Governance pipeline: Validation, Authorization, Idempotency, PeriodLock, MakerChecker behaviors — all registered.
- ~30 Carter API modules across Auth, Users, Roles, Approvals, Audit, Periods, ReasonCodes, Customers, Parties,
  Catalog, Items, Inventory, Receiving, Transfers, CycleCounts, StockAdjustments, IssueOrders, InventoryAlerts,
  Invoices, Payments, Warranty, FxRates, Reports, Barcodes, AI, KPIs, FX.
- 40+ persisted entities; 6 EF migrations (Identity, Governance, Party+Outbox, Operational Core, Inventory WMS, AI).
- Identity + JWT RS256 auth; login flow wired to the React SPA through the Vite proxy.
- Hangfire recurring jobs (approval expiry, idempotency cleanup, summary refreshes, warranty expiry, low-stock,
  AI accounting check) + Outbox dispatcher.
- Observability, health checks, Scalar API docs, SignalR hub.
- React SPA with auth store, app layout, and screens for Dashboard, Customers (+detail), Invoices (+detail),
  Inventory, Parties, Users, Roles, Approvals, Audit, Period Locks.

**What is thin / missing (high level — see completion plan for detail):**
- Frontend UI is missing for many built backend modules: Receiving, Transfers, Cycle Counts, Stock Adjustments,
  Issue Orders, Inventory Alerts, Warranty, FX Rates, Reports, Barcodes/scanning, AI assistant, Reason Codes.
- RBAC seeding is inconsistent (see quirks below).
- Reporting/finance and AI modules have backend surface but no end-user screens.

**Default seeded admin (dev only):** username `admin`, email `admin@autoparts.local`, password `Admin@123456`.

---

## 3. Environment Quirks (ACTUALLY found in this .NET repo)

1. **Ports (non-negotiable in dev):**
   - API listens on **`http://localhost:5000`** (`launchSettings.json` → profile `Development`).
   - Frontend Vite dev server on **`5173`**, and its proxy hard-targets **`localhost:5000`** for `/api` and `/hubs`
     (`frontend/vite.config.ts`). If you change the API port, you must change the proxy too.
   - Docker infra: PostgreSQL **5432**, Redis **6379**, Seq **5341** (UI), pgAdmin **5050** (`docker-compose.dev.yml`).

2. **Database connection (dev):** `Host=localhost;Port=5432;Database=autoparts_erp;Username=erp_user;Password=erp_secret_dev`
   (`appsettings.Development.json`). The compose file provisions exactly this DB/user/password — they must match.

3. **PostgreSQL extensions required:** the schema uses **`ltree`** (category paths) and **`pgvector`** (AI embeddings).
   The database must have these extensions enabled or migrations/queries will fail.

4. **snake_case everywhere in SQL.** `AppDbContext.OnModelCreating` rewrites all identifiers to snake_case. Any raw
   Dapper SQL must use snake_case table/column names and alias back to PascalCase DTO properties.

5. **`Testing` environment shortcuts:** when `ASPNETCORE_ENVIRONMENT == "Testing"`, the app **skips** Hangfire,
   the Hangfire dashboard, auto-migration, and seeding. Integration tests rely on this.

6. **Auto-migrate + seed on startup:** outside `Testing`, `Program.cs` runs `dbContext.Database.Migrate()` then
   `DatabaseSeeder.SeedAsync` and `DemoDataSeeder.SeedAsync`. A reachable, migratable Postgres is required to boot.

7. **JWT is RS256 with keys in `appsettings.Development.json`** (base64 PEM public+private) for local dev **only**.
   Token lifetime 15 min access / 7 day refresh. Production keys must come from env/secret store.

8. **Warnings are build errors** (`TreatWarningsAsErrors=true`) across all projects — CI and local build will fail
   on any warning.

9. **SDK is pinned** to `9.0.312` via `global.json` (`rollForward: latestFeature`). Use a matching .NET 9 SDK.

10. **Windows local-run scripts are heavily environment-normalized.** `scripts/start-local.ps1` explicitly resets
    `SystemRoot`, `TEMP`, `NUGET_PACKAGES=C:\NuGetPackages`, `PATH`, etc., and launches API + Vite via absolute
    tool paths (`C:\Program Files\dotnet\dotnet.exe`, `C:\Program Files\nodejs\node.exe`). This is a sandbox quirk;
    on a normal machine you can run the tools directly (see SETUP_HARDENING.md).

11. **NuGet restore uses a repo-local `NuGet.Config`** — restore with `--configfile NuGet.Config` if the scripts do.

12. **CORS is wide-open (`AllowAnyOrigin`) in the current composition** — acceptable for dev, must be tightened for prod.

13. **Untracked run/log artifacts** exist in the tree (`fc_api_run_*.log`, `s8_npm_dev_*.log`, `scripts/logs/*`,
    a stray `%SystemDrive%/` folder). Do not commit these; they are local noise.
