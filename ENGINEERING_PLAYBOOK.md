# ENGINEERING_PLAYBOOK.md — AutoPartsERP

> **Isolation notice:** Rules here apply to **`autoparts_erp.net`** exclusively (.NET 9 / PostgreSQL / React).
> No conventions from any prior Next.js/NestJS project carry over.

---

## 1. Organizational Structure & RBAC

### 1.1 Two role layers exist in code (⚠ reconcile before extending)

The repo currently ships **two overlapping role vocabularies** — this is a known gap to unify:

- **Governance role codes** (`Domain/Constants/RoleCodes.cs`):
  `SYSTEM_ADMIN`, `SECURITY_ADMIN`, `COMPLIANCE_OFFICER`, `APPROVER`, `AUDITOR`, `STANDARD_USER`.
- **Seeder role codes** (`DatabaseSeeder.cs`): assigns the admin user to `SUPER_ADMIN` and `ADMIN`
  — strings that are **not** present in `RoleCodes.All`.

> **Playbook rule:** Treat `RoleCodes.cs` as the source of truth. The seeder must be corrected to use those
> codes (or the canonical role set must be extended and the seeder aligned) before adding new roles.

### 1.2 Canonical business RBAC roles for an Auto-Parts ERP

Design new roles/permission bundles around these operational personas. Permissions already exist as granular
codes in `PermissionCodes.cs` — assemble roles by composing them.

| Role | Persona | Representative permission bundle (from `PermissionCodes`) |
|---|---|---|
| **System Administrator** (`SYSTEM_ADMIN`) | Full platform owner | `system:*`, `users:*`, `roles:*`, all reads |
| **Security Administrator** (`SECURITY_ADMIN`) | IAM only | `users.*`, `roles.*`, `auth.manage` |
| **Compliance Officer** (`COMPLIANCE_OFFICER`) | Governance oversight | `audit.read`, `period-locks.*`, `reason-codes.*`, `approvals.read` |
| **Approver** (`APPROVER`) | Maker-checker second signer | `approvals.review`, `approvals.read`, `*:approve_*` |
| **Auditor** (`AUDITOR`) | Read-only + audit | `audit.read`, all `*:read` |
| **Inventory / Warehouse Manager** | WMS owner | `inventory:*`, `receiving:*`, `transfers:*`, `cycle_counts:*`, `stock_adjustments:*`, `inventory_alerts:*`, `items:*`, `catalog:*`, `barcodes:*` |
| **Warehouse Operator** | Floor tasks | `receiving:create/putaway`, `issue_orders:pick/verify/issue`, `transfers:ship/receive`, `cycle_counts:record`, `barcodes:scan` |
| **Sales Representative** | Front-office sales | `invoices:create/update/read`, `customers:read/create/update`, `payments:read`, `party:read`, `items:read`, `catalog:read` |
| **Sales Supervisor** | Sales approvals | above + `invoices:post/void/price_override/delivery_fee`, `payments:allocate` |
| **Accountant / Finance** | Ledger & reporting | `payments:*`, `fx_rates:manage`, `reports:*`, `period-locks:*`, `invoices:read/post/void` |
| **Warranty Officer** | Claims | `warranty:*`, `items:read`, `customers:read` |
| **Catalog / Data Steward** | Master data | `catalog:*`, `items:*`, `fx_rates:read` |
| **AI Operator** | AI features | `ai:*` |
| **Standard User** (`STANDARD_USER`) | Baseline | assorted `*:read` |

**Permission naming convention (already established, keep consistent):**
- Governance/legacy phase-1 codes use dot form: `users.read`, `approvals.review`.
- All newer domain codes use **colon form**: `resource:action` (e.g. `invoices:post`, `inventory:adjust`).
- New permissions **must** follow the colon form, be added to the relevant nested class **and** to
  `PermissionCodes.All`, then referenced via `RequiredPermission => PermissionCodes.X.Y`.

---

## 2. Development Standards

### 2.1 Git strategy
- **Branch model:** trunk-based with short-lived branches off `main`.
  - `feat/<module>-<short-desc>`, `fix/<area>-<short-desc>`, `chore/…`, `docs/…`, `refactor/…`.
- **Commit messages:** Conventional Commits — matches existing history (`feat:`, `fix:`). Scope by module,
  e.g. `feat(invoices): add post command idempotency`.
- **PR rules:** every PR must build with `TreatWarningsAsErrors=true` (warnings **fail** the build), pass tests,
  and touch only one logical concern. No commits to `main` without review.
- **Never commit secrets.** JWT keys currently live in `appsettings.Development.json` for local dev only —
  production keys come from environment/secret store (see SETUP_HARDENING.md).

### 2.2 .NET coding guidelines
- **Clean Architecture dependency rule is absolute:** `Domain` depends on nothing; `Application` depends on
  `Domain`/`Contracts`; `Infrastructure` depends on `Application`/`Domain`; `Api` composes everything. Never invert.
- **One feature = one folder** under `Application/Features/<Domain>/<Action>/` containing the command/query record,
  its `Validator`, and its `Handler`. (Some modules use a single aggregated `*Features.cs` file — acceptable for
  tightly related handlers, e.g. `ItemFeatures.cs`.)
- **Commands declare cross-cutting needs via marker interfaces**, not handler code:
  `IAuthorizedRequest` (+ `RequiredPermission`), `IIdempotentRequest`, `IPeriodSensitiveRequest`,
  `IMakerCheckerRequest` (+ `RequiresApproval`), `IAuditableRequest` (+ `AuditModule`).
- **Return `Result` / `Result<T>`** for business outcomes; use `Error(code, message)`. Do not throw for expected
  business failures.
- **Reads:** prefer Dapper with explicit SQL in snake_case; map columns with `AS PascalCase`. **Writes / invariants:**
  go through Domain entity factory methods (e.g. `Item.Create(...)`) and EF or parameterized SQL.
- **Nullable reference types on, ImplicitUsings on.** Respect `GlobalUsings.cs` per project; don't re-import.
- **Endpoints** are Carter modules implementing `ICarterModule`; they should be thin — validate/shape input,
  send the MediatR request, map `Result` to HTTP via the shared `ResultMappingExtensions` / `ApiResponse`.
- **Async everywhere**, always thread `CancellationToken`.
- **snake_case discipline:** never hardcode PascalCase table names in SQL; the DB is snake_case by convention.

### 2.3 Frontend guidelines
- React 19 function components + hooks only. Data fetching via TanStack Query; auth/session via the Zustand
  `authStore`. Forms via react-hook-form + Zod. Tables via `@mui/x-data-grid` / TanStack Table.
- **All user-facing strings via i18next** (`ar.json` / `en.json`); Arabic is primary, keep RTL working
  (`stylis-plugin-rtl`). No hardcoded UI text.
- API calls go through the shared Axios client (`api/client.ts`) and typed endpoint modules (`api/endpoints/*`).
  The Vite dev proxy forwards `/api` and `/hubs` to `http://localhost:5000`.

### 2.4 Testing rules
- Three test projects exist: **UnitTests** (domain/handlers), **IntegrationTests** (API + DB via `Program`
  partial class / `Testing` environment), **E2ETests**. Frontend E2E uses Playwright.
- The API detects `Environment == "Testing"` to skip Hangfire, migrations, seeding, and the dashboard — write
  integration tests against that environment.
- Every new command/query needs: at least one validator unit test + one handler/integration test covering the
  happy path and one governance failure (unauthorized / period-locked / needs-approval) where applicable.

---

## 3. AI Agent Definitions

Agents operating on this repo must stay in their lane and obey the governance pipeline. Each has hard limits.

### Agent: **Backend .NET Feature Agent**
- **Scope:** `Application/Features/*`, `Contracts/*`, corresponding Carter module in `Api/Modules/*`.
- **Must:** follow the feature-folder pattern, use marker interfaces for cross-cutting rules, return `Result<T>`,
  add validators, reference `PermissionCodes`.
- **Must NOT:** touch `Program.cs` ordering of behaviors, invent new roles, bypass authorization, write business
  logic inside Carter endpoints, or reference Infrastructure types from Application.
- **Starter prompt seed:** *"Implement `<Action>` for `<Domain>` as a MediatR command/query with validator and
  handler, guarded by `PermissionCodes.<X>`, returning `Result<T>`. Add the Carter endpoint that sends it."*

### Agent: **DB Migration Agent**
- **Scope:** `Infrastructure/Persistence/Migrations/*`, entity configs, `AppDbContext` DbSets.
- **Must:** create EF Core migrations that respect snake_case, `ltree`, and `pgvector`; keep migrations additive
  and reversible; update the corresponding `DbSet` and entity configuration.
- **Must NOT:** hand-edit already-applied migrations, drop columns without an approved plan, or change the
  snake_case naming convention.

### Agent: **Governance/RBAC Agent**
- **Scope:** `PermissionCodes.cs`, `RoleCodes.cs`, `DatabaseSeeder.cs`, behaviors.
- **Must:** keep `PermissionCodes.All` and `RoleCodes.All` authoritative and in sync with the seeder; reconcile
  the `SUPER_ADMIN`/`ADMIN` vs `SYSTEM_ADMIN` discrepancy.
- **Must NOT:** grant blanket permissions or weaken the maker-checker/period-lock behaviors.

### Agent: **Frontend React Agent**
- **Scope:** `frontend/src/*`.
- **Must:** build screens for existing backend endpoints, wire through `api/endpoints/*`, use i18n + RTL, keep
  routes in `App.tsx`, respect the auth guard.
- **Must NOT:** call the API without the shared client, hardcode strings, or introduce a second state-management or
  data-fetching library.

### Agent: **Jobs & Messaging Agent**
- **Scope:** Hangfire jobs, `OutboxDispatcherService`, outbox handlers.
- **Must:** register jobs/handlers in `Program.cs`, keep recurring schedules explicit, make handlers idempotent.
- **Must NOT:** run jobs in the `Testing` environment or perform cross-module writes outside the outbox pattern.

### Agent: **AI/Knowledge Agent**
- **Scope:** `Application/Features/Ai/*`, `Infrastructure` AI services, pgvector KB.
- **Must:** guard all endpoints with `PermissionCodes.Ai.*`; log prompts to `AiPromptLog`.
- **Must NOT:** expose AI features without feature-flag checks (`AiFeatureFlag`).

### Agent: **DevOps/Observability Agent**
- **Scope:** `docker-compose.*.yml`, `nginx/`, `observability/`, `.github/workflows/`, `scripts/`.
- **Must:** keep dev/prod parity, preserve health/metrics endpoints and OpenTelemetry wiring.
- **Must NOT:** hardcode secrets into compose files or CI.

---

## 4. Task Breakdown Conventions

- **Epic → Feature → Task.** An Epic maps to a business domain (e.g. "Receiving & Putaway UI"). A Feature maps to
  one command/query + endpoint (+ its UI). A Task is a single reviewable unit.
- **Vertical slices:** prefer delivering a feature end-to-end (Domain → Application → Api → Contracts → Frontend →
  tests) over horizontal layers.
- **Definition of Done:** builds with warnings-as-errors, has validator + tests, is guarded by the correct
  `PermissionCode`, is auditable if it mutates state, has Arabic+English UI strings, and (for money/stock
  mutations) participates in period-lock / maker-checker where required.
- **Every stateful write** must consider: authorization, idempotency, period sensitivity, approval, audit — decide
  each explicitly and encode via marker interfaces.
