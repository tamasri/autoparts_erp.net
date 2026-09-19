# AGENT_ONBOARDING.md — AutoPartsERP

> **READ THIS FIRST.** Any AI agent (or human) starting work on `autoparts_erp.net` must read this file
> completely before writing a single line of code. Companion docs: `PROJECT_VISION.md` (what exists / roadmap),
> `ENGINEERING_PLAYBOOK.md` (rules), `SETUP_HARDENING.md` (run, deploy, harden), `docs/FEATURE_GAP_AND_ROADMAP.md`
> (gap analysis + phased plan). *Last verified against the code: 2026-09-19.*

---

## 1. Starter Prompt (paste into any agent working on this repo)

```
You are working ONLY on the `autoparts_erp.net` repository.

HARD CONTEXT:
- .NET 9 Clean Architecture ERP for an automotive spare-parts business (Arabic-first, RTL).
- Backend: ASP.NET Core Minimal APIs + Carter, MediatR (CQRS) with pipeline behaviors, EF Core 9 (writes,
  migrations) + Dapper (reads) on PostgreSQL 16, ASP.NET Identity + JWT RS256, Redis, Hangfire, SignalR,
  Audit.NET, OpenTelemetry.
- Accounting: ERPNext runs as a HEADLESS accounting engine. This app is the system of record for items,
  inventory, warehouse ops, warranty and governance; ERPNext is the system of record for the ledger (chart of
  accounts, payments, purchase invoices, taxes, financial reports). The ONLY door between them is
  `IErpNextClient`. Users never open ERPNext's UI — every accounting screen must live inside OUR frontend.
- Frontend: React 19 + Vite 6 + TypeScript + react-router 7 + Zustand + axios + sonner, styled with our own
  "Vex" design system (`frontend/src/styles/theme.css`, classes `vex-*`, `btn-*`, `badge*`). It does NOT use
  MUI, TanStack Query, react-hook-form or Zod even though some are still listed in package.json (dead deps).
- Projects: Domain -> Contracts -> Application -> Infrastructure -> Api (dependency rule is absolute).
  Tests: UnitTests, IntegrationTests (need Docker/Testcontainers), E2ETests (empty). SPA in /frontend.

ABSOLUTE RULES:
1. ISOLATED project. Import nothing from any Next.js / NestJS / TS-backend project.
2. Cross-cutting rules (authorization, idempotency, period-lock, maker-checker, audit) are enforced by
   MediatR pipeline behaviors + marker interfaces on commands. Never reimplement them in handlers/endpoints.
3. Business outcomes use Result / Result<T> + Error. Do not throw for expected failures.
4. DB is snake_case. Reads = Dapper SQL with AS-aliases; writes/invariants = Domain factories + EF/parameterized SQL.
5. Every stateful write decides: permission, idempotency, period-sensitivity, maker-checker, audit.
6. Api/Application/Infrastructure build with TreatWarningsAsErrors=true. (Domain and Contracts do NOT yet —
   known debt, do not rely on it.)
7. UI strings: Arabic is the end-user language and RTL must stay intact. i18next is initialised
   (`frontend/src/i18n`) but NO screen uses it yet — strings are hardcoded Arabic in the TSX. New screens follow
   the existing hardcoded-Arabic convention until an externalisation pass is scheduled; never mix languages.
8. Feature-folder convention under Application/Features/<Domain>/<Action>/.
9. LISTS ARE SERVER-PAGED. Never fetch "a big page" and filter/aggregate in the browser. Use
   `usePagedList` + `<Pagination>` for lists and server-side endpoints for every total/KPI.
10. AI NEVER WRITES core data. It reads through permission-checked tools and proposes; execution goes
    through the existing approval (maker-checker) flow.
11. NEVER put secrets in code, commits, docs or chat. Keys live in env / .env.vps on the server only.
12. Background jobs must do real work or be off. A job that records "completed" without doing anything is a bug.

BEFORE CODING: `git log --oneline -20`, `git status`, and read the target module + Domain entity + Contracts DTOs.
Do not modify code you have not read.

DELIVERY: build + unit tests locally -> commit (Conventional Commits, with the Co-Authored-By line the session
requires) -> push to `main` -> confirm the GitHub Actions run is GREEN (`gh run list`) -> report. If a run
goes red, fixing it is the next task. Update PROJECT_VISION.md status whenever an Epic/Phase item completes.
```

---

## 2. Confirmed Current State (verified 2026-09-19)

**Repository:** `https://github.com/tamasri/autoparts_erp.net.git`, branch `main`. The owner has explicitly
authorised direct pushes to `main`; the working tree was clean at the last check.

**Live environment (dev/staging):** a Lightnode VPS — Ubuntu 24.04, 1 vCPU / 2 GB RAM (+ swap) — running the app
behind nginx with a self-signed certificate on the bare IP (no domain yet). PostgreSQL 16 runs on the **host**
(not in Docker); the `api` and `redis` containers run from `docker-compose.vps.yml`. ERPNext (frappe_docker
`pwd.yml`) runs on the same host on port 8080. The server must be upgraded before production (see roadmap).
Deploy with `./scripts/deploy-vps.sh` only (see SETUP_HARDENING.md).

**What is built and working (verified in the running system):**
- Governance pipeline (Validation → Authorization → Idempotency → PeriodLock → MakerChecker) — incl. a working
  approval **replay** (`IApprovalReplayContext`) so an approved request actually executes.
- ~28 Carter modules; 12 raw-SQL migrations (ids `202401010000xx`); 60+ tables.
- Auth (JWT RS256), users/roles/permissions backend, audit log, period locks, approvals.
- Two product models unified: `skus` + `inventory_stock` (operational, drives invoices) linked to
  `items` + `inventory_balances` (WMS) via `items.sku_id`, kept in step by SQL functions run from Hangfire
  (`sync_items_from_skus`, `sync_inventory_balances_from_stock`). Reverse sync (WMS → stock) is NOT done.
- **ERPNext hand-off is live:** `ErpNextClient` syncs Items, Customers, Suppliers and posted Sales Invoices
  (submitted, so GL entries post). Results are recorded in `erpnext_sync_log`. Company currency is **USD**
  (owner decision). Payments, purchase invoices and reports are not synced yet.
- Frontend screens (all RTL Arabic): Login, Dashboard, KPI, Customers (+detail), Parties (+combined statement),
  Invoices (+workspace, detail), FX rates, Items list + **item card** (details/edit, stop-ship, stock, aliases,
  interchanges, prices), Inventory, Receiving, Transfers, Cycle counts, Adjustments, Issue orders, Inventory
  alerts, Approvals, Audit log, Period locks, Users (list), Roles, **Accounting Sync**.
- Server-side paging on: invoices, customers, parties, inventory, users, approvals, audit, ERPNext sync log,
  items. Still unpaged in the UI: receiving, transfers, cycle counts, adjustments, issue orders, FX rates.
- CI (`.github/workflows/deploy.yml`): build + unit + integration tests on every push; the deploy job only
  runs when the VPS secrets are configured (they are not yet, so CI is build/test only) and it is GREEN.

**What is stubbed or missing (do not assume it works):**
- **AI is a stub.** `AiService.ChatAsync` echoes the user's message; nothing generates suggestions;
  `AccountingCheckJob` writes a fake "completed" row; no screen calls `/ai/*`. See PROJECT_VISION §6.
- Backend with no screen: AI, Payments, Warranty, Reports, Barcodes, Catalog categories, batches, user/role
  editors, reason codes. Tables with no API: `party_contacts`, `party_addresses`, `party_notes`,
  `attribute_schemas`, `item_reorder_settings`, `barcode_scan_logs`.
- Dashboard/KPI numbers are computed in the browser from a sample page — wrong at scale (Phase 0 fix).
- No purchase invoices, bank/cash accounts, POS, public invoice links, e-mail/SMS, CRM extras, backups UI.

**Bootstrap admin:** created once by `DatabaseSeeder` from `Seed:AdminEmail/AdminUsername/AdminPassword`.
In Production the app refuses to start without a real password (the deploy script generates one into
`.env.vps`). A dev-only fallback exists in Development. There is **no** universal default password any more.

---

## 3. Decisions already made by the owner (do not re-litigate)

| Topic | Decision |
|---|---|
| Accounting | ERPNext as headless engine; all accounting screens inside our UI |
| Inventory model | Option A — unify `items`/`skus`; skus stay the operational source for now |
| Company currency | **USD** |
| AI provider | OpenAI-compatible hosted API: **Groq (free tier) preferred, DeepSeek as cheap alternative**; keys only on the server |
| Sham Cash | API integration comes later — build only the abstraction/plumbing now (`IPaymentGateway`, webhook endpoint, public payment page shell) |
| E-mail / SMS | Free solutions: SMTP for e-mail; SMS via a pluggable channel (see ROADMAP §7) |
| Git | Push straight to `main`; GitHub Actions must always be green |
| Secrets | Never in chat/commits; anything ever pasted in chat is considered exposed and must be rotated |

---

## 4. Environment Quirks (all found the hard way)

1. **Dev ports:** API `http://localhost:5000`; Vite `5173` (its proxy targets `localhost:5000` for `/api`, `/hubs`);
   dev Docker infra: Postgres 5432, Redis 6379, Seq 5341, pgAdmin 5050 (`docker-compose.dev.yml`).
2. **Dev DB:** `Host=localhost;Port=5432;Database=autoparts_erp;Username=erp_user;Password=erp_secret_dev`
   (dev compose only). Production credentials come from `.env.vps`.
3. **Postgres extensions:** `uuid-ossp`, `pg_trgm`, `ltree` are required; `vector` (pgvector) is needed for
   embeddings — see `EnsureRequiredExtensions`. On the VPS, Postgres is on the host, so extensions must be
   installed there.
4. **snake_case everywhere in SQL.** `AppDbContext` applies entity configurations **first** and the snake_case
   rewrite **second** (reversing this breaks column names). Raw Dapper SQL: snake_case + `AS` aliases.
5. **Dapper + positional records:** constructor matching needs the *exact* CLR type of each column. `DateOnly`
   and `DateTimeOffset` need the handlers in `DapperTypeHandlers` (registered first thing in `Program.cs`).
   Always alias columns. A `date` column vs a `string` property silently fails materialisation.
6. **Postgres UNIQUE treats NULLs as distinct** — `ON CONFLICT` on a nullable column never fires; use
   update-then-insert.
7. **Migrations are raw SQL** (`migrationBuilder.Sql`) with ids `202401010000NN`. Add new ones with the next
   number; never edit an applied one. Verify against a fresh database, not just an upgraded one.
8. **`Testing` environment** skips Hangfire, the dashboard, auto-migrate and seeding. Integration tests rely on
   it and need Docker (Testcontainers) — they cannot run on a machine without Docker; CI runs them.
9. **Auto-migrate + seed on boot** outside `Testing`.
10. **Hangfire:** every job class is tagged `[Queue("governance")]` and the server MUST list that queue
    (`AddHangfireServer(o => o.Queues = { "default", "governance" })`) — omitting it silently disabled every
    job for months. The `/hangfire` dashboard needs a Bearer JWT of a `SYSTEM_ADMIN`; a plain browser visit
    gets 401. Operate jobs from the in-app **Accounting Sync** screen instead.
11. **Docker on Linux:** the api container reaches host services via `host.docker.internal`, which needs
    `extra_hosts: host.docker.internal:host-gateway`. ERPNext lives on another Docker network; the api reaches it
    through the host port. `ufw` must allow 5432 from `172.16.0.0/12` for container → host Postgres.
12. **nginx:** `/health` is restricted to 127.0.0.1; unrouted paths fall back to the SPA `index.html`
    (so `https://ip/erp` "works" but is just the SPA). `nginx.conf` is bind-mounted — after a `git pull` the
    container must be recreated through the deploy script, not `docker compose up` by hand (which also loses
    `--env-file` and boots the api with blank config).
13. **`docker compose up --build` does NOT rebuild the frontend** (it is built on the host into `frontend/dist`).
    Always deploy with `./scripts/deploy-vps.sh`.
14. **JWT keys** for dev live in `appsettings.Development.json` (git-ignored patterns exist); production keys
    come from `.env.vps`.
15. **Windows dev machine:** Git Bash has no `python`/`jq`; use PowerShell or `node` for JSON. Pasting
    multi-line text into noVNC corrupts it — do server work over SSH. Never disable SSH password auth without a
    verified working key (this locked the owner out once).
16. **SDK pin:** `global.json` = 9.0.312 with `rollForward: latestMajor`.
17. **Local secrets/noise:** `ADMIN PASSWORD.txt`, `*password*.txt`, `appsettings.Development.json`,
    `scripts/logs/`, `*_run_*.log` are git-ignored. One of them was committed once and had to be purged — check
    `git status` before every commit.
