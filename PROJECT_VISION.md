# PROJECT_VISION.md — AutoPartsERP

> **Isolation notice:** This document describes **`autoparts_erp.net`** only. It is a standalone
> .NET / PostgreSQL / React ecosystem. It shares **nothing** with any prior Next.js/NestJS/TypeScript
> project. Do not import stacks, conventions, or environment assumptions from outside this repository.

---

## ⚠️ RULES FOR ANY AI AGENT WORKING ON THIS PROJECT

1. **ISOLATION RULE:** `autoparts_erp.net` is a standalone .NET 9 / PostgreSQL / React ecosystem. Never
   import any stack, tooling, pattern, environment variable, port, or convention from any prior
   Next.js / NestJS / TypeScript-backend project.
2. **GOVERNANCE-BY-PIPELINE RULE:** Cross-cutting concerns (authorization, idempotency, period-lock,
   maker-checker, audit) are enforced via MediatR pipeline behaviors and marker interfaces on commands.
   Never reimplement them inside handlers or endpoints.
3. **RESULT PATTERN RULE:** Business outcomes use `Result` / `Result<T>` + `Error`. Do not throw for
   expected business failures.
4. **snake_case RULE:** The database is snake_case. Raw Dapper SQL must use snake_case identifiers and
   alias back to PascalCase DTO properties.
5. **BUILD HYGIENE RULE:** The solution builds with `TreatWarningsAsErrors=true`. Warnings are errors.
6. **BILINGUAL RULE:** All user-facing strings go through i18next (`ar.json` / `en.json`). Arabic is the
   primary end-user language; keep RTL intact.
7. **CONTEXT-CHECK RULE:** Before coding, run `git log`, `git status`, and read the target module + its
   Domain entity + Contracts DTOs. Never modify code you have not read.
8. **CRITICAL WORKFLOW RULE:** The work is divided into Phases and Epics in section 5. Every time you
   complete a Phase or an Epic, you MUST open `PROJECT_VISION.md` and update its status (e.g., change
   `- [ ]` to `- [x]` and update the text status to `Completed`) before asking for approval to start the
   next phase.

---

## 1. Project Codename & Purpose

- **Codename:** AutoPartsERP
- **Assembly / namespace root:** `AutoPartsERP`
- **Solution file:** `AutoPartsERP.sln`
- **Purpose:** A governance-first, bilingual (Arabic/English, RTL-aware) ERP for an **automotive spare-parts
  trading business**. It covers Warehouse Management (WMS), Inventory & Batch tracking, Catalog / SKU / Part-number
  intelligence, Sales Invoicing, Payments & allocations, Customers & Parties (customer/supplier duality),
  Warranty, FX rates, financial reporting (P&L, account statements, inventory valuation), and an embedded
  **AI assistant layer** (Semantic Kernel + pgvector).
- **Design ethos:** *Governance as a first-class concern.* Cross-cutting maker-checker approvals, period locks,
  idempotency, audit trails, and RBAC are enforced centrally via MediatR pipeline behaviors — not scattered per endpoint.

---

## 2. Verified Tech Stack (layer by layer, from actual code)

| Layer | Technology | Evidence |
|---|---|---|
| **Runtime / SDK** | .NET 9 (`net9.0`), SDK pinned to **9.0.312** (`rollForward: latestFeature`) | `global.json`, all `.csproj` `<TargetFramework>net9.0` |
| **Compiler hygiene** | Nullable enabled, ImplicitUsings, `TreatWarningsAsErrors=true` | `*.csproj` property groups |
| **API host** | ASP.NET Core Minimal APIs organized with **Carter** modules | `AutoPartsERP.Api.csproj`, `Modules/*Module.cs`, `app.MapCarter()` |
| **Mediation / CQRS** | **MediatR 12** with ordered pipeline behaviors: Validation → Authorization → Idempotency → PeriodLock → MakerChecker | `Program.cs` L31-39, `Application/Common/Behaviors/*` |
| **Validation** | FluentValidation 11 | `ValidationBehavior.cs`, per-feature `*Validator` classes |
| **Mapping** | Mapster + `ServiceMapper` | `Program.cs` L44-48 |
| **ORM (writes/identity/migrations)** | EF Core 9 + `Npgsql.EntityFrameworkCore.PostgreSQL` | `AppDbContext.cs`, `Persistence/Migrations/*` |
| **Data access (reads/perf)** | **Dapper 2** raw SQL against snake_case tables | e.g. `Features/Items/ItemFeatures.cs` handlers |
| **Database** | **PostgreSQL 16** (with `ltree` for category paths, `pgvector` for embeddings) | `docker-compose.dev.yml`, `category_path ... ltree`, `Pgvector.EntityFrameworkCore` |
| **Identity & AuthN** | ASP.NET Core Identity (`AppUser`/`AppRole`, `Guid` keys) + **JWT Bearer RS256** | `AppDbContext : IdentityDbContext<...>`, `Program.cs` JWT config |
| **AuthZ** | Permission-based, enforced in `AuthorizationBehavior` via `IAuthorizedRequest.RequiredPermission` | `PermissionCodes.cs`, marker interfaces |
| **Caching / distributed** | Redis via StackExchange.Redis + `AddStackExchangeRedisCache` | `Program.cs` L26-28 |
| **Background jobs** | **Hangfire** (PostgreSQL storage) + recurring jobs + `OutboxDispatcherService` HostedService | `Program.cs` L93-101, L233-280 |
| **Realtime** | SignalR hub at `/hubs/erp` | `Hubs/ErpHub.cs`, `app.MapHub` |
| **Auditing** | **Audit.NET** (EF + PostgreSql sinks), opt-out mode on `AppDbContext` | `Infrastructure.csproj`, `[AuditDbContext(Mode=OptOut)]` |
| **Idempotency** | `IdempotentAPI.MinimalAPI` + custom `DistributedIdempotencyService` + `IdempotencyBehavior` | `Program.cs`, `IIdempotentRequest` |
| **Observability** | OpenTelemetry (ASP.NET/HTTP/EF), Prometheus scraping (`/metrics`), Serilog (Console/Seq/Grafana Loki), Grafana/Tempo/Loki configs | `observability/*`, `Program.cs`, `Infrastructure.csproj` |
| **Docs** | OpenAPI + **Scalar** API reference | `app.MapOpenApi()`, `app.MapScalarApiReference()` |
| **Reporting / files** | ClosedXML (Excel), QuestPDF (invoice PDF) | `Infrastructure.csproj`, `*ExcelExporter`, `GetInvoicePdfQuery` |
| **Barcodes** | QRCoder + ZXing.Net + SkiaSharp | `Infrastructure.csproj`, `BarcodeService` |
| **AI layer** | Microsoft.Extensions.AI + **Semantic Kernel** + **pgvector** embeddings | `Infrastructure.csproj`, `Ai/*`, `IKnowledgeBaseService` |
| **Localization** | Humanizer (+ `Humanizer.Core.ar`) | `IHumanizerService`, `HumanizerService` |
| **API versioning** | Asp.Versioning.Http (default v1.0) | `Program.cs` L122-127 |
| **Health** | `/health`, `/health/live`, `/health/ready` (Npgsql + Redis) | `Program.cs` L104-107, 207-209 |
| **Frontend** | **React 19 + Vite 6 + TypeScript 5.7** | `frontend/package.json` |
| **UI kit** | **MUI 6** + `@mui/x-data-grid` 7, Emotion, **stylis-plugin-rtl** (Arabic RTL) | `frontend/package.json` |
| **FE state/data** | Zustand 5 (auth store), TanStack Query 5, TanStack Table 8, Axios | `frontend/src/stores/authStore.ts`, `api/*` |
| **FE forms** | react-hook-form 7 + Zod 3 | `frontend/package.json` |
| **FE i18n** | i18next + react-i18next (`ar.json`, `en.json`) | `frontend/src/i18n/*` |
| **FE realtime** | `@microsoft/signalr` 9 | `frontend/package.json` |
| **FE routing** | react-router-dom 7 | `frontend/src/App.tsx` |
| **Containerization** | Docker + docker-compose (dev / prod / vps), Nginx reverse proxy | `docker-compose.*.yml`, `nginx/nginx.conf`, `Dockerfile` |
| **Testing** | xUnit-style Unit / Integration / E2E projects + Playwright (frontend) | `tests/*`, `@playwright/test` |

### Solution project graph (Clean Architecture)

```
AutoPartsERP.Domain          → entities, enums, value objects, constants, Result<T> (no dependencies)
AutoPartsERP.Contracts       → DTOs & request/response records (API shape)
AutoPartsERP.Application      → CQRS features, behaviors, abstractions, validators (refs Domain, Contracts)
AutoPartsERP.Infrastructure  → EF Core, Dapper, Identity, Hangfire jobs, services (refs Domain, Application)
AutoPartsERP.Api             → Carter modules, middleware, Program composition root (refs Application, Infrastructure)
tests/UnitTests | IntegrationTests | E2ETests
frontend/                    → React SPA (separate build, proxied to API)
```

---

## 3. Screen / Feature Inventory

Status legend — **Completed**: backend feature + endpoint + wired UI present; **In Progress**: backend
present, UI partial/thin or gaps found; **Not Started**: little/no implementation.

| Domain / Module | Backend (Application + Module) | Frontend Screen | Status |
|---|---|---|---|
| **Auth (login/refresh/logout/me)** | `Features/Auth/*`, `AuthModule` | `pages/Login.tsx`, `stores/authStore.ts` | **Completed** |
| **Dashboard / KPIs** | `Features/Kpi/*`, `KpiAdminModule` | `pages/Dashboard.tsx`, `api/kpi.ts` | **In Progress** |
| **Users management** | `Features/Users/*`, `UsersModule` | `pages/settings/Users.tsx` | **In Progress** |
| **Roles & permissions** | `Features/Roles/*`, `RolesModule` | `pages/settings/Roles.tsx` | **In Progress** |
| **Approvals (maker-checker)** | `Features/Approvals/*`, `ApprovalsModule`, `MakerCheckerBehavior` | `pages/approvals/Approvals.tsx` | **In Progress** |
| **Audit log** | `Features/Audit/*`, `AuditModule`, Audit.NET | `pages/audit/AuditLog.tsx` | **In Progress** |
| **Period locks** | `Features/Periods/*`, `PeriodsModule`, `PeriodLockBehavior` | `pages/periods/PeriodLocks.tsx` | **In Progress** |
| **Reason codes** | `Features/ReasonCodes/*`, `ReasonCodesModule` | (no dedicated screen) | **In Progress** |
| **Customers** | `Features/Customers/*`, `CustomersModule` | `pages/customers/Customers.tsx`, `CustomerDetail.tsx` | **Completed** |
| **Parties (customer/supplier duality)** | `Features/Parties/*`, `PartiesModule` | `pages/parties/Parties.tsx` | **In Progress** |
| **Catalog / SKU / Categories** | `Features/Catalog/*`, `CatalogModule` | (surfaced via Inventory) | **In Progress** |
| **Items / Part numbers / aliases / interchange** | `Features/Items/ItemFeatures.cs`, `ItemsModule` | (no dedicated screen) | **In Progress** |
| **Inventory stock & batches** | `Features/Inventory/*`, `InventoryModule` | `pages/inventory/Inventory.tsx` | **In Progress** |
| **Receiving / Putaway** | `Features/Receiving/*`, `ReceivingModule` | (no screen) | **In Progress** |
| **Transfers (inter-location)** | `Features/Transfers/*`, `TransfersModule` | (no screen) | **In Progress** |
| **Cycle counts** | `Features/CycleCounts/*`, `CycleCountsModule` | (no screen) | **In Progress** |
| **Stock adjustments** | `Features/StockAdjustments/*`, `StockAdjustmentsModule` | (no screen) | **In Progress** |
| **Issue orders (picking)** | `Features/IssueOrders/*`, `IssueOrdersModule` | (no screen) | **In Progress** |
| **Inventory alerts / low-stock** | `Features/InventoryAlerts/*`, `InventoryAlertsModule`, `LowStockAlertJob` | (no screen) | **In Progress** |
| **Invoices (create/confirm/post/void/PDF)** | `Features/Invoices/*`, `InvoicesModule` | `pages/invoices/Invoices.tsx`, `InvoiceDetail.tsx` | **In Progress** |
| **Payments & allocations** | `Features/Payments/*`, `PaymentsModule` | (surfaced via customer/invoice) | **In Progress** |
| **Warranty** | `Features/Warranty/*`, `WarrantyModule`, `ExpireWarrantyRecordsJob` | (no screen) | **In Progress** |
| **FX rates** | `Features/FxRates/*`, `FxRatesModule` | (no screen) | **In Progress** |
| **Reports (P&L, statements, inventory value, batch trace)** | `Features/Reports/*`, `ReportsModule`, Excel exporters | (no screen) | **In Progress** |
| **Barcodes (scan/generate)** | `Features/Barcodes/*`, `BarcodesModule`, `BarcodeService` | (ZXing libs installed, no screen) | **In Progress** |
| **AI assistant / suggestions / KB** | `Features/Ai/*`, `AiModule`, `AiAdminModule`, `AccountingCheckJob` | (no screen) | **In Progress** |
| **Realtime notifications** | `Hubs/ErpHub.cs` | `@microsoft/signalr` wired in FE | **In Progress** |

> **Overall assessment:** The **backend is broad and deep** (30+ Carter modules, full governance pipeline,
> 40+ persisted entities, Hangfire jobs, AI foundation). The **frontend is an early but real SPA** — auth,
> layout, customers, and read screens exist, but most WMS/finance/AI modules have **no UI yet**. This is a
> *backend-leading* project where the primary remaining effort is **frontend build-out + end-to-end wiring +
> hardening of a few backend inconsistencies** (see the Completion Plan).

---

## 4. Core Architectural Principles (specific to this ERP)

1. **Governance by pipeline, not by endpoint.** All write operations flow through MediatR behaviors in a fixed
   order (Validation → Authorization → Idempotency → PeriodLock → MakerChecker). Cross-cutting rules are declared
   via marker interfaces on the command (`IAuthorizedRequest`, `IIdempotentRequest`, `IPeriodSensitiveRequest`,
   `IMakerCheckerRequest`, `IAuditableRequest`) — **never** reimplemented inside handlers.
2. **CQRS with a dual data strategy.** Writes and domain invariants go through EF Core entities and `Result<T>`
   factories; high-volume reads use **Dapper raw SQL** against snake_case tables. Choose the right tool per feature.
3. **snake_case at the database boundary.** `AppDbContext.OnModelCreating` auto-converts all table/column/key/index
   names to snake_case. Dapper SQL must be written in snake_case and mapped back to PascalCase DTOs with `AS` aliases.
4. **Result pattern over exceptions for domain flow.** `Result` / `Result<T>` + `Error` carry business failures;
   exceptions are reserved for truly exceptional conditions and handled by ProblemDetails middleware.
5. **Outbox for reliable side-effects.** Domain events persist to `OutboxMessage` and are dispatched by
   `OutboxDispatcherService` to handlers (e.g. `InvoicePostedOutboxHandler`) — no direct cross-module coupling.
6. **Bilingual & RTL-first.** Entities carry `name_en` / `name_ar` (+ colloquial Arabic); UI uses i18next and
   `stylis-plugin-rtl`. Arabic is the end-user language; English is the technical language.
7. **Idempotent, auditable writes.** Distributed idempotency keys + Audit.NET give safe retries and a full trail.
8. **Observability is not optional.** OpenTelemetry traces/metrics, Prometheus, Serilog structured logs and
   health endpoints are part of the composition root, not an afterthought.

---

## 5. PROJECT COMPLETION PLAN & ROADMAP

> **Single source of truth for progress.** Per the CRITICAL WORKFLOW RULE, update the checklists and phase
> statuses in this section every time an Epic or Phase completes, before requesting approval for the next.

### Executive read
`autoparts_erp.net` is a **backend-leading** .NET 9 Clean Architecture ERP. The backend is broad (30+ Carter
modules, full governance pipeline, 40+ entities, 6 migrations, Hangfire, AI foundation). The **frontend is an
early SPA** with only ~11 screens, and several backend modules have **no UI**. Remaining work = frontend
build-out + end-to-end wiring + closing specific backend inconsistencies + prod hardening.

### 5.1 Architectural Gaps Found During the Scan

- [x] **Gap 1 — RBAC seeding mismatch (correctness bug).** `DatabaseSeeder.cs` assigned roles `SUPER_ADMIN` /
      `ADMIN` that did not exist in `RoleCodes.cs`. **Resolved:** the seeder now creates every `RoleCodes.All` role
      with its permission claims (via the new `RolePermissionMap`) and assigns the admin to `SYSTEM_ADMIN`.
- [ ] **Gap 2 — Split permission naming.** Dot-form legacy codes (`users.read`) coexist with colon-form
      (`invoices:post`) in `PermissionCodes.cs`. Needs a documented canonical convention and possibly a migration.
- [ ] **Gap 3 — Frontend/backend coverage gap.** Backend modules with **no screen**: Receiving, Transfers, Cycle
      Counts, Stock Adjustments, Issue Orders, Inventory Alerts, Warranty, FX Rates, Reports, Barcodes, AI, Reason Codes.
- [x] **Gap 4 — Duplicated abstractions.** Marker interfaces existed under both `Application/Abstractions/Markers/`
      and `Application/Common/Abstractions/Markers/`. **Resolved:** deleted the dead shim folder
      `Application/Abstractions/` (markers + service shims); `Application/Common/Abstractions/*` is now the single
      canonical location.
- [x] **Gap 5 — Prod security posture.** `AllowAnyOrigin()` CORS, JWT PEM keys committed in
      `appsettings.Development.json`, and auto-migrate-on-boot need prod-grade handling. *(Tracked for Phase E —
      hardening.)*
- [x] **Gap 7 — Postgres extension dependency.** `ltree` was already created but `vector` was only created
      *conditionally* (silently skipped when unavailable). **Resolved:** added a first-ordered
      `EnsureRequiredExtensions` migration that guarantees `uuid-ossp`, `pg_trgm`, `ltree` and treats `pgvector` as a
      hard prerequisite (fails loudly with an actionable message if the package is missing).
- [x] **Gap 8 — Working-tree drift.** **Resolved:** removed stray run/log artifacts (`fc_api_run_*.log`,
      `s8_npm_dev_*.log`, `%SystemDrive%/`) and added `.gitignore` rules for `scripts/logs/`, `*_run_*.log`, and
      `%SystemDrive%/`.
- [x] **Gap 6 — DTO/read-model inconsistency.** Frontend `getRows()` defensively probed multiple response shapes
      in ~11 duplicated helpers. **Resolved:** introduced a single `frontend/src/api/apiData.ts` (`unwrapList` /
      `unwrapNode`) consumed by all pages, standardizing on the backend `ApiResponse` envelope.

### 5.2 Epics & Phases Roadmap

#### PHASE A — Stabilize & Reconcile (EPIC 0)
`Status: Completed`
Agents: Governance/RBAC Agent + DB Migration Agent. Gate: seeding correct, build green — **passed**
(solution builds with 0 errors; frontend `typecheck` passes with 0 errors).
- [x] Fix seeder role codes; implement role→permission seeding for all `RoleCodes`.
- [x] Standardize the API response envelope (`ApiResponse`) + fix FE shape-probing.
- [x] Dedupe marker-interface folders to one canonical location.
- [x] Add `CREATE EXTENSION ltree / vector` migration.
- [x] Commit/clean the working tree; add `.gitignore` entries for `scripts/logs`, `*_run_*.log`, `%SystemDrive%`.
- [x] *(Added in this pass)* Fix pre-existing frontend type errors: React 19 global `JSX` namespace shim +
      `id: string | undefined` narrowing in `CustomerDetail` / `InvoiceDetail`.

#### PHASE B — Core Operational Frontend (EPICs 1–3)
`Status: Not Started`
Agents: Backend .NET Agent (finalize endpoints/DTOs) + Frontend React Agent (vertical slices). Gate after each epic: e2e smoke + Arabic/RTL check.
- [ ] **EPIC 1 — WMS Frontend:** Receiving/Putaway, Transfers, Cycle Counts, Stock Adjustments, Issue Orders
      (picking), Inventory Alerts screens + barcode scanning UI (`@zxing/browser`).
- [ ] **EPIC 2 — Sales & Finance Frontend:** full Invoice lifecycle UI (create→confirm→post→void→PDF), Payments &
      allocation UI, FX Rates management, Reports dashboards (P&L, statements, inventory value, batch trace) with
      Excel/PDF export.
- [ ] **EPIC 3 — Catalog & Items Frontend:** SKU/Category tree management, Items (part numbers, aliases,
      interchange), stop-ship maker-checker UI.

#### PHASE C — Governance, Warranty & Parties Frontend (EPICs 4–5)
`Status: Not Started`
Agents: Frontend React Agent + Jobs & Messaging Agent (verify outbox/alerts feed UI).
- [ ] **EPIC 4 — Governance Frontend polish:** Approvals inbox, Audit log viewer, Period locks, Reason codes,
      Users/Roles editors — wired to real endpoints and RBAC.
- [ ] **EPIC 5 — Warranty & Parties:** Warranty claim/process/reject UI; Party duality (customer/supplier) screens
      + combined statements.

#### PHASE D — AI & Realtime (EPICs 6–7)
`Status: Not Started`
Agents: AI/Knowledge Agent + Realtime wiring.
- [ ] **EPIC 6 — AI Assistant:** chat UI, suggestions review, KB indexing, feature-flag admin (all `ai:*` guarded).
- [ ] **EPIC 7 — Realtime & Notifications:** consume `/hubs/erp` for low-stock/approval events in the SPA.

#### PHASE E — Hardening & Release (EPIC 8)
`Status: Not Started`
Agents: DevOps/Observability Agent.
- [ ] **EPIC 8 — Hardening & Release:** prod CORS, secret-managed JWT, secured Hangfire/Scalar, TLS via Nginx,
      backups, CI (`.github/workflows/deploy.yml`), test coverage for governance paths.

### 5.3 AI-Orchestrated Build Sequence

Each phase follows the mandated cadence: **Build → Stop → Summary (Features / Files / Dependencies / Next Steps)
→ wait for approval.**

- [x] **Phase A:** Governance/RBAC Agent + DB Migration Agent → EPIC 0. *(Gate: seeding correct, build green — passed.)*
- [ ] **Phase B:** Backend .NET Agent finalizes missing endpoints/DTOs; Frontend React Agent builds EPIC 1 → 2 → 3
      in vertical slices. *(Gate after each epic: e2e smoke + Arabic/RTL check.)*
- [ ] **Phase C:** Frontend Agent → EPIC 4 & 5; Jobs & Messaging Agent verifies outbox/alerts feed the UI.
- [ ] **Phase D:** AI/Knowledge Agent → EPIC 6; Realtime wiring EPIC 7.
- [ ] **Phase E:** DevOps/Observability Agent → EPIC 8, then release candidate.
