# ENGINEERING_PLAYBOOK.md — AutoPartsERP

> **Isolation notice:** rules here apply to **`autoparts_erp.net`** exclusively. *Last verified 2026-09-19.*
> Each rule is tagged **[enforced]** (a build/test/CI check or the runtime pipeline enforces it) or
> **[convention]** (relies on discipline — review carefully).

---

## 1. Organizational Structure & RBAC

### 1.1 Role vocabulary
`Domain/Constants/RoleCodes.cs` is the source of truth. `DatabaseSeeder` creates **every** `RoleCodes.All` role with
its permission claims (via `RolePermissionMap`) and assigns the bootstrap admin to `SYSTEM_ADMIN`. The old
`SUPER_ADMIN` / `ADMIN` mismatch is fixed. Governance roles: `SYSTEM_ADMIN`, `SECURITY_ADMIN`,
`COMPLIANCE_OFFICER`, `APPROVER`, `AUDITOR`, `STANDARD_USER`.

### 1.2 Canonical business personas (compose roles from granular permissions)

| Role | Persona | Representative permissions |
|---|---|---|
| System Administrator | Platform owner | everything, incl. ERPNext sync, AI admin, backups |
| Security Administrator | IAM | users, roles |
| Compliance Officer | Governance oversight | audit, period locks, reason codes, approvals read |
| Approver | Second signer | approvals review, `*:approve_*` |
| Auditor | Read-only + audit | audit read, all `*:read` |
| Warehouse Manager / Operator | WMS | inventory, receiving, transfers, cycle counts, adjustments, alerts, items, catalog, barcodes |
| Sales Representative / Supervisor | Front office | invoices, customers, payments read/allocate, items read; supervisor adds post/void/price override |
| Accountant | Ledger & reporting | payments, FX, reports, period locks, invoices read/post |
| Warranty Officer, Catalog Steward, AI Operator | Specialised | `warranty:*`, `catalog:*`/`items:*`, `ai:*` |

**Permission naming [convention]:** colon form `resource:action` (85 codes). Nine legacy dot-form codes
(`users.read`, `approvals.review`, …) remain — debt D1: migrate them to colon form. New permissions go in the nested
class **and** in `PermissionCodes.All`, and are referenced via `RequiredPermission => PermissionCodes.X.Y`.
User-facing screens that a role must not see are hidden **and** the endpoint is protected (never UI-only).

---

## 2. Development Standards

### 2.1 Git & delivery
- **Trunk-based.** The owner has authorised direct pushes to `main`; use short branches only for risky work.
  **[convention]**
- **Conventional Commits** with a scope: `feat(erpnext): …`, `fix(hangfire): …`, `docs: …`. Every commit ends with the
  `Co-Authored-By` line the session requires. **[convention]**
- **CI must stay green [enforced]:** `.github/workflows/deploy.yml` builds and runs unit + integration tests on every
  push. Before pushing: `dotnet build`, `dotnet test tests/AutoPartsERP.UnitTests`, and for frontend work
  `npx tsc --noEmit && npm run build`. After pushing: `gh run list` and fix a red run before anything else.
  Integration tests need Docker (Testcontainers) and only run in CI on machines without it.
- **No flaky tests.** A test may not depend on timing of an internal exporter or on test order. (The metrics
  endpoint test failed intermittently for that reason and now asserts only what is deterministic.)
- **Never commit secrets [convention + .gitignore]:** `appsettings.Development.json`, `*password*.txt`,
  `.env*` (except templates). A leaked file must be purged, and any secret ever pasted in chat is rotated.
- Check `git status` before every commit for local noise (`scripts/logs/`, `*_run_*.log`).

### 2.2 .NET coding guidelines
- **Dependency rule [convention]:** Domain → nothing; Contracts → nothing; Application → Domain/Contracts;
  Infrastructure → Application/Domain; Api composes. (Known breach: Domain references Humanizer — debt D2.)
- **Feature folders** `Application/Features/<Domain>/<Action>/` with command/query, validator, handler (a single
  `*Features.cs` for tightly related handlers is acceptable).
- **Marker interfaces on commands [enforced by the pipeline]:** `IAuthorizedRequest` (+`RequiredPermission`),
  `IIdempotentRequest`, `IPeriodSensitiveRequest`, `IMakerCheckerRequest` (+`RequiresApproval`),
  `IAuditableRequest` (+`AuditModule`). Never reimplement in handlers. An approved maker-checker request is
  replayed through the same pipeline with the approval check bypassed (`IApprovalReplayContext`) — do not add
  side paths.
- **Result pattern:** business outcomes return `Result`/`Result<T>` with `Error(code, message)`.
- **Reads (Dapper):** snake_case SQL, **always alias columns** to the DTO property, and make the CLR type match the
  column exactly (`DateOnly` for `date`, `DateTimeOffset` for `timestamptz`; handlers are in
  `DapperTypeHandlers`). A positional-record constructor that cannot be matched fails silently at runtime as a 500.
  Prefer `Dapper` handlers to return `PagedResponse<T>` for lists.
- **Writes:** Domain factories + EF, or parameterised SQL for set-based operations. No string-concatenated SQL.
- **NULL semantics:** Postgres UNIQUE treats NULLs as distinct, so `ON CONFLICT` on a nullable column never fires —
  use update-then-insert or a partial index.
- **Migrations:** raw SQL in `Persistence/Migrations`, next sequential id, additive, never edit an applied one;
  test on an empty database (the seeder and demo data run on boot).
- **Endpoints** are thin Carter modules: bind input, `sender.Send(...)`, `result.ToApiResult()`. Lists take
  `page`/`pageSize` (clamp to 100) and return `PagedResponse<T>` (`items`, `pageNumber`, `pageSize`, `totalCount`).
- **Async everywhere**, thread `CancellationToken`.
- **Jobs:** every recurring job is registered in `Program.cs`, tagged `[Queue("governance")]`, idempotent, and must
  do real work or be disabled — never record "completed" without doing it **[convention]**. The Hangfire server must
  list every queue used (a missing queue silently stops all jobs).
- **Aggregations are server-side [convention]:** totals, KPIs and counts are computed by an endpoint, never by
  summing a sample page in the browser.

### 2.3 ERPNext integration rules
- The single door is `IErpNextClient`. Add methods there; implement in `ErpNextClient`; keep `NullErpNextClient`
  returning a no-op so the app runs with ERPNext off (`Erpnext:Enabled=false`).
- **Names, not ids:** ERPNext identifies a customer by its name and an item by its `item_code`. We use the party
  display name and the sku code. Resolve them in SQL before calling (`SalesInvoiceErpNextSyncer` is the model).
- **Submit ledger documents** (`docstatus = 1`) or nothing posts. Keep `update_stock = 0` — inventory belongs to us.
- **Currency:** the company currency is **USD**; send `currency` explicitly (`Erpnext:Currency`).
- **Every attempt is logged** in `erpnext_sync_log` (`SYNCED`/`FAILED`/`SKIPPED`, error text, attempts). Failures are
  retried by `SyncCatalogToErpNextJob`. Show sync state in the app (Accounting Sync screen).
- Frappe rejects group-type links: use leaf records (`Customer Group = Commercial`, `Territory = Rest Of The World`,
  `Supplier Group = Local`). Fix defaults from the real error text in the log, not from guesses.
- API credentials live only in the server environment (`ERPNEXT_API_KEY/SECRET`).

### 2.4 AI rules
- All AI features go through one provider abstraction over an **OpenAI-compatible** endpoint (Groq / DeepSeek);
  base URL, model and key are configuration, the key never leaves the server.
- **Read-only tools only**, each checking the caller's permissions. **AI never writes core data**
  (`AllowWritesToCoreData` stays blocked). A proposed action is a `PENDING` row in `ai_suggestions` and executes only
  via the maker-checker flow.
- Every call is written to the immutable prompt log; feature flags gate every feature; minimise and redact what is
  sent (no secrets, no credentials, no more customer data than the question needs).
- Never present a stub as a working feature.

### 2.5 Frontend guidelines
- React 19 function components + hooks. **Data:** shared axios client (`api/client.ts`) + typed modules in
  `api/endpoints/*`; unwrap envelopes with `unwrapList` / `unwrapNode` / `unwrapPaged` (`api/apiData.ts`).
  **State:** Zustand for auth; local state otherwise. **Do not introduce** MUI, TanStack Query, react-hook-form or
  Zod (declared in `package.json` but unused; slated for removal).
- **Lists [convention]:** `usePagedList` + `<Pagination>` — one page in memory, server search debounced, stale
  responses discarded. Never replace the whole page with a spinner while searching (it steals input focus); dim the
  table instead.
- **Styling:** the Vex design system (`styles/theme.css`, `vex-*`, `btn-*`, `badge*`, CSS variables). No new design
  system, no ad-hoc colour literals when a variable exists.
- **Language:** Arabic RTL. i18next exists but is unused; strings are currently hardcoded Arabic (debt D3). New
  screens follow the hardcoded-Arabic convention until the externalisation pass; do not mix languages on a screen.
- **Notifications:** `toast` / `extractApiError` from `lib/toast`. POSTs guarded by `WithIdempotency()` must send an
  `Idempotency-Key` header (see `api/endpoints/items.ts`).
- Routes live in `App.tsx`; the sidebar in `components/layout/AppLayout.tsx`.

### 2.6 Testing rules
- **UnitTests** (validators, behaviors, domain); **IntegrationTests** (API + Postgres via the `Testing` environment);
  **E2ETests** (empty). Playwright is a devDependency with no tests yet.
- Every new command/query: a validator test and a handler/integration test covering the happy path and one governance
  failure (unauthorized / period-locked / needs approval) where relevant.
- After any Dapper read change, exercise the endpoint against real data — type mismatches only show at runtime.

### 2.7 Security rules
- Least privilege: guard endpoints with `RequiredPermission`; admin-only surfaces use the `SystemAdministrator` role.
- Never log or return secrets; never echo tokens (redact when debugging).
- Public surfaces (future invoice links, payment webhooks) use unguessable tokens, rate limits and no internal ids.
- Changing SSH/firewall settings requires a verified working alternative first (a bad change already locked the
  owner out once).

---

## 3. AI Agent Definitions

Agents stay in their lane and obey the governance pipeline.

| Agent | Scope | Must | Must NOT |
|---|---|---|---|
| **Backend .NET Feature** | `Application/Features/*`, `Contracts/*`, `Api/Modules/*` | feature folders, markers, `Result<T>`, validators, `PermissionCodes`, paged lists | reorder behaviors, bypass authorization, put logic in Carter endpoints |
| **DB Migration** | `Persistence/Migrations`, entity configs | additive raw-SQL migrations, snake_case, test on an empty DB | edit applied migrations, drop columns without a plan |
| **Governance / RBAC** | `PermissionCodes`, `RoleCodes`, `RolePermissionMap`, behaviors | keep `All` lists and the seeder in sync | grant blanket permissions, weaken maker-checker/period-lock |
| **Frontend React** | `frontend/src/*` | screens for existing endpoints, `usePagedList`, Vex design system, RTL Arabic | call the API outside the shared client, add a second state/data library, aggregate in the browser |
| **Jobs & Messaging** | Hangfire jobs, outbox, notification channels | register in `Program.cs`, queue `governance`, idempotent, real work | run in `Testing`, write across modules outside the outbox |
| **Accounting / ERPNext Integration** | `IErpNextClient`, `ErpNextClient`, syncers, `erpnext_sync_log`, accounting screens | names not ids, submit documents, log every attempt, USD, keep the UI inside our app | expose ERPNext's UI, store credentials in code, bypass the interface |
| **AI / Knowledge** | `Features/Ai/*`, AI services | provider abstraction, read-only tools, suggestions via approvals, prompt log, feature flags | write core data, ship stubs as features, send secrets to the provider |
| **DevOps / Observability** | compose files, nginx, workflows, `scripts/` | deploy only via `scripts/deploy-vps.sh`, keep health/metrics, keep CI green | hardcode secrets, change SSH/firewall without a fallback |

---

## 4. Task Breakdown Conventions

- **Epic → Feature → Task.** Prefer **vertical slices** (Domain → Application → Api → Contracts → Frontend → tests).
- **Definition of Done:**
  1. builds warning-free (Api/Application/Infrastructure) and frontend `tsc` + build pass;
  2. unit/integration tests added and CI green;
  3. guarded by the right `PermissionCode`, audited if it mutates state, idempotent/period-sensitive/approval
     decided explicitly;
  4. lists paged server-side; totals computed server-side;
  5. Arabic RTL UI, exercised against real data;
  6. `PROJECT_VISION.md` status and (if behaviour changed) the relevant doc updated in the same commit;
  7. deployed to the VPS via the deploy script and verified when the change is user-visible.
- **Every stateful write** decides: authorization, idempotency, period sensitivity, approval, audit.
