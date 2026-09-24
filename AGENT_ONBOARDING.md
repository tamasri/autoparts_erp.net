# AGENT_ONBOARDING.md — AutoPartsERP

> **READ THIS FIRST.** Any AI agent (or human) starting work on `autoparts_erp.net` must read this file
> completely before writing a single line of code. Companion docs: `PROJECT_VISION.md` (what exists / roadmap),
> `ENGINEERING_PLAYBOOK.md` (rules), `SETUP_HARDENING.md` (run, deploy, harden), `docs/FEATURE_GAP_AND_ROADMAP.md`
> (gap analysis + phased plan). *Last verified against the code: 2026-09-21.*

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
- Frontend: React 19 + Vite 6 + TypeScript + react-router 7 + Zustand + axios + sonner, on **MUI v6** (RTL, one theme in
  `frontend/src/theme`) with a shared kit in `components/ui` (DataTable, ReasonDialog, ImportDialog, RoutedTabs, ...) and hooks (`usePagedList`, `useLoad`,
  `useCan`, `useConfirm`). TanStack Query, react-hook-form and Zod are used only in `features/customers`. Every screen is on MUI; `theme.css` and the old "Vex" classes are
  gone (D16 closed 2026-09-23). Amounts are shown with `components/ui/Money`.
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
   `usePagedList` + `DataTable` paging for lists and server-side endpoints for every total/KPI.
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
Deploy with `./scripts/deploy-vps.sh` only (see SETUP_HARDENING.md). New in the app image: embedded fonts for PDF (no OS fonts needed).

**What is built and working (verified in the running system):**
- Governance pipeline (Validation → Authorization → Idempotency → PeriodLock → MakerChecker) — incl. a working
  approval **replay** (`IApprovalReplayContext`) so an approved request actually executes.
- ~30 Carter modules; 23 raw-SQL migrations (ids `202401010000NN`); 70+ tables.
- Auth (JWT RS256), users/roles/permissions backend, audit log, period locks, approvals.
- Two product models unified: `skus` + `inventory_stock` (operational, drives invoices) linked to
  `items` + `inventory_balances` (WMS) via `items.sku_id`, kept in step by SQL functions run from Hangfire
  (`sync_items_from_skus`, `sync_inventory_balances_from_stock`). Reverse sync (WMS → stock) is NOT done.
- **ERPNext hand-off is live:** `ErpNextClient` syncs Items, Customers, Suppliers, posted Sales Invoices (submitted, so GL
  entries post) and customer receipts as Payment Entries; voids and reversals cancel the ERPNext documents. Every step is
  recorded in `erpnext_sync_log`. Company currency is **USD**. Purchase invoices, returns, COGS and reports are not synced
  yet (see PROJECT_VISION debts D10–D14).
- Reference pickers (no typing of ids): `LocationSelect`, `EntityPicker`, `ItemPickerModal`, `FxRateField` in
  `frontend/src/components/pickers/`; the invoice screen uses all of them. Five WMS screens still use raw-ID inputs (debt D13).
- Frontend screens (all RTL Arabic), by menu section: Sales (Accounts with customer profile/statements, invoices + workspace + detail, payments), Accounting
  (chart of accounts, entries, reconciliation, receivables/payables, financial reports, FX rates), Purchasing, Items (+ item card), Stock (balances, movements,
  warehouses, alerts), Warehouse operations (receiving, transfers, issue orders, cycle counts, adjustments), Administration (approvals, audit, period locks,
  ERPNext sync, **users with create/edit/roles/password/activation**, roles), Login, Dashboard, KPI.
- Server-side paging on: invoices, customers, parties, inventory, users, approvals, audit, ERPNext sync log,
  items. Still unpaged in the UI: receiving, transfers, cycle counts, adjustments, issue orders, FX rates.
- CI (`.github/workflows/deploy.yml`): build + unit + integration tests on every push; the deploy job only
  runs when the VPS secrets are configured (they are not yet, so CI is build/test only) and it is GREEN.

**What is stubbed or missing (do not assume it works):**
- **AI is not implemented.** `AiService.ChatAsync` now returns an explicit `Ai.ProviderNotConfigured` error (it used to
  echo the user's text); nothing generates suggestions; no screen calls `/ai/*`. See PROJECT_VISION §6.
- Backend with no screen: AI, Warranty, Reports (the old P&L/inventory value), Barcodes, Catalog categories, batches, role-permission editing, reason codes.
  Tables with no API: `party_contacts`, `party_addresses`, `party_notes`, `attribute_schemas`, `item_reorder_settings`, `barcode_scan_logs`.
- Not built: POS, public invoice links, e-mail/SMS, CRM extras, backups UI, purchase returns, tax templates (purchasing and the accounting section exist).

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

1. **Dev ports (deliberately unusual, to coexist with other projects on the same machine):** API
   `http://localhost:47000`; Vite `47173` (its proxy targets `localhost:47000` for `/api`, `/hubs`); dev Docker infra
   (`docker-compose.dev.yml`): Postgres **47432**, Redis **47379**, Seq UI 47341 (ingest 47342), pgAdmin 47050.
2. **Dev DB:** `Host=localhost;Port=47432;Database=autoparts_erp;Username=erp_user` (password in the dev compose and
   the git-ignored `appsettings.Development.json`). Production credentials come from `.env.vps`.
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
17. **Local secrets/noise:** `ADMIN PASSWORD.txt`, `*password*.txt`, `scripts/logs/`, `*_run_*.log` and `appsettings.Development.json` are git-ignored. **Exception found
    2026-09-21:** `appsettings.Development.json` (a dev JWT private key and the dev DB password) had been committed in the very first commit, before the ignore rule
    existed, and the repository is public. It is untracked now and `scripts/init-dev-settings.ps1` creates a fresh one (new key pair) on any checkout, but it
    **stays in git history** and must be treated as public (the VPS uses its own key pair from `.env.vps`). Check `git status` before every commit.
18. **Verify data-layer changes against a real Postgres, locally.** CI does not execute your SQL: integration tests run in
    `Testing` (no migrations, no seeding) and only check auth/health. Start the dev stack
    (`docker compose -f docker-compose.dev.yml up -d postgres redis`, needs Docker Desktop), run the API in
    Development, log in as the seeded admin and call the endpoints you touched. This caught seven shipped-but-broken
    features in one session (see PROJECT_VISION §5.2, Phase 0).
19. **Identity claims:** JwtBearer runs with `MapInboundClaims = false`, so the user id is the `sub` claim
    (`CurrentUserService` also falls back to `NameIdentifier`). With the default mapping `UserId` was `Guid.Empty`
    and every `created_by`, audit and approval row belonged to nobody.
20. **Pipeline failures need a matching return type.** `ResultFactory.Failure<TResponse>` supports `Result` and
    `Result<T>`; anything else throws and becomes a 500 instead of a 400/403/202.
21. **Maker-checker:** `Governance:AllowSelfApproval` (env `GOVERNANCE_ALLOW_SELF_APPROVAL`, default `false`) decides
    whether the requester may review their own request. With a single operator nobody could approve anything, so set
    it `true` only until a second approver user exists. The approvals list hides your own requests unless it is true.
22. **EF + client-generated Guid keys:** configure `ValueGeneratedNever()`; otherwise a new child added through a tracked
    navigation is written as UPDATE and `SaveChanges` throws `DbUpdateConcurrencyException`.

---

## 5. Agent Activity Log

### 2026-09-19 — Antigravity (Frontend Migration — Phases 0–6)

**What the agent did:**
- Read full audit report (7,686 LOC, 60+ files) and confirmed explicit owner approval for full stack migration.
- Updated `PROJECT_VISION.md` §2 (tech stack), §2a (new — policy override), and D3/D4 debt items.
- Updated `ENGINEERING_PLAYBOOK.md` §2.5 (new frontend guidelines), added `## Execution Reports` section.
- **Phase 0:** Created `frontend/src/lib/rtlCache.ts` + `frontend/src/theme/theme.ts`; updated `frontend/src/main.tsx` to wire `QueryClientProvider`, `CacheProvider` (emotion RTL), `ThemeProvider`, `CssBaseline`.
- **Phase 1:** MUI `createTheme` in `theme.ts` maps all Vex CSS tokens 1:1 — primary `#5c54ff`, 12 px radii, card shadow, font stack. `direction:'rtl'` set once.
- **Phase 2:** Created `frontend/src/lib/apiClient.ts` (typed envelope unwrapper — `apiGet/apiPost/apiPut/apiDelete`) + `frontend/src/features/customers/queries.ts` (TanStack Query hooks: `useCustomerList`, `useCustomerById`, `useSaveCustomer`, `useDeactivateCustomer`).
- **Phase 3:** Created `frontend/src/features/customers/schema.ts` (Zod schemas) + `frontend/src/features/customers/CustomerDialog.tsx` (MUI Dialog + RHF + zodResolver) + `frontend/src/features/customers/DeactivateDialog.tsx` (replaces `window.prompt`).
- **Phase 4:** Expanded `frontend/src/i18n/ar.json` and `en.json` with customers, nav, common, lang keys (95 keys each).
- **Phase 5:** Rewrote `frontend/src/pages/customers/Customers.tsx` with `<DataGrid paginationMode="server" />`.
- **Phase 6:** Rewrote `frontend/src/App.tsx` with `React.lazy` + `Suspense` + `ErrorBoundary` on all 28 routes. Created `frontend/src/components/common/ErrorBoundary.tsx`.

**How to run locally:**
```powershell
# Start dev stack (Postgres + Redis)
docker compose -f docker-compose.dev.yml up -d postgres redis

# Start API
dotnet run --project src/AutoPartsERP.Api --launch-profile Development

# Start frontend
cd frontend
npm install      # already installed; skippable if no package changes
npm run dev      # http://localhost:47173
```

**TypeScript check + build:**
```powershell
cd frontend
npx tsc --noEmit   # should pass with 0 errors
npm run build      # vite build to frontend/dist
```

**ENV vars required:** no new frontend env vars. All API calls go to `/api/v1` via Vite proxy. New backend-side env vars are unchanged (see §3.3 of SETUP_HARDENING.md).

**Files changed in this session:**
```
frontend/src/main.tsx                                [MODIFY]
frontend/src/App.tsx                                 [MODIFY]
frontend/src/lib/rtlCache.ts                         [NEW]
frontend/src/lib/apiClient.ts                        [NEW]
frontend/src/theme/theme.ts                          [NEW]
frontend/src/features/customers/schema.ts            [NEW]
frontend/src/features/customers/queries.ts           [NEW]
frontend/src/features/customers/CustomerDialog.tsx   [NEW]
frontend/src/features/customers/DeactivateDialog.tsx [NEW]
frontend/src/pages/customers/Customers.tsx           [REWRITE]
frontend/src/components/common/ErrorBoundary.tsx     [NEW]
frontend/src/i18n/ar.json                            [MODIFY]
frontend/src/i18n/en.json                            [MODIFY]
PROJECT_VISION.md                                    [MODIFY]
ENGINEERING_PLAYBOOK.md                              [MODIFY]
AGENT_ONBOARDING.md                                  [MODIFY]
```

### 2026-09-19 — Claude (ERPNext sync fixes, inventory/payments screens)
- Company lookup asked ERPNext for a non-existent field `cost_of_goods_sold_account` (→ every COGS Journal Entry and payment sync FAILED). Now reads `default_expense_account`.
- Customer rename: when ERPNext refuses the rename (a record with the new name already exists) the catalog job now adopts that record via upsert instead of failing forever.
- Removed `@mui/icons-material` from `vite.config.ts` manualChunks (not a dependency; broke `npm run build`).
- New Payments screen and picker-based inventory screens (old-style components; migrate to the MUI stack with the rest).

### 2026-09-20 — Claude (defects from testing, warehouse control, documents, exports)
- **Audit first (owner rule):** `inventory_movements` already existed (written only by WMS paths), `locations` had a read endpoint only, QuestPDF/ClosedXML were referenced but the invoice "PDF" was plain text. Reused those; added CsvHelper and `@mui/x-tree-view`.
- **PDF bug:** `GetInvoicePdfQuery` returned UTF-8 text labelled `application/pdf`. Now rendered by `IDocumentRenderer` (embedded Noto Naskh Arabic + Noto Sans, RTL). Verified by rasterising a page with `GenerateImages()` (a scratch console project; the browser pane cannot show PDFs).
- **Sync errors from the screenshots:** inventory-account parent is now discovered (`Current Assets - ABBR` is not guaranteed); customer payment references are clamped to the invoice's real outstanding in ERPNext.
- **Statements:** the customer statement endpoint returns an object (`transactions`), the old page read it as a list and showed nothing. A voided invoice and its credit note now net to zero (both were not counted consistently). Combined statement exists only for accounts that are both customer and vendor.
- **Users page crash:** the API returns `roles: [{roleId, code, name}]`; the page rendered the objects (React error #31) and the shared error boundary kept the error for the next route.
- **Issue orders** used to change status only; they now decrement stock and write the ledger.
- New: warehouses screen, movements screen, chart of accounts, ERPNext documents, item import, view/print on all warehouse documents. UI label "Accounts" replaces "الأطراف" (code keeps `party`).
- Test helpers used: mock ERPNext extended for Account list, `get_balance_on`, `get_count`, document lists (scratchpad, not committed).
- **One menu entry "الحسابات"** (`/accounts`, tabs: all accounts / customers) replaces the two entries "العملاء" and "Accounts". The UI word is Arabic; `party`/`parties` stay in code and routes (`/parties/:id/statement`); `/customers` and `/parties` redirect.
- **ERPNext duplicates:** ERPNext does not refuse a second Customer/Supplier with the same name, it names it "X - 1", "X - 2". Every sync run therefore created new customers (the sync screen showed "…- 61"). `UpsertPartyAsync` now looks the party up by name and updates it. **Existing duplicates on the server must be merged/deleted once in ERPNext** (they are not touched automatically).
- **Stock unification (D15):** `StockLevelWriter` moves `inventory_stock` when warehouse documents change AVAILABLE quantities; migration 15 stops the stock→balances copy double-counting batches; migration 16 merges duplicate "no batch" balance rows (Postgres NULL semantics) and adds `NULLS NOT DISTINCT`. Verified receive → putaway → sync → transfer ship/receive: both tables equal at every step.
- **Purchasing:** migration 17 (bills, lines, supplier payments, allocations), permissions `purchases:*` / `supplier_payments:*` (SYSTEM_ADMIN only until roles are assigned), posting a bill = stock in + ledger + weighted-average cost + outbox → `PurchaseErpNextSyncer`; payments allocate at creation; void blocked while payments exist. Not period-locked at bill creation for drafts; posting/void/reverse check the period.
- **Migration 18:** party code sequence lagged behind the seeded codes, so creating any new account failed with a duplicate key.
- **Stock adjustments reach ERPNext** as a Journal Entry (Dr COGS / Cr Inventory at the item's current cost) via `StockAdjustmentErpNextSyncer`.
- **UI:** shell rewritten in MUI (`components/layout/AppLayout.tsx`, menu data in `navigation.ts`); one `Accounts` screen replaces Customers + Parties (list, roles, statement, customer profile, new account); purchasing screens in `features/purchasing` and `pages/purchasing`.
- **Known gaps:** vendor-only accounts have no statement page yet (the combined one needs both roles); the ~20 screens listed in D16 are still old-style; role bundles for purchasing (buyer, accountant) are not defined yet.
- **Round 3 (2026-09-20):** roles ACCOUNTANT/PURCHASER with permission bundles; `GET /parties/{id}/statement/ap`; void of a purchase bill restores the weighted-average cost; credit limit is USD-only in the UI (SYP column kept at 0, the sales invoice warning now compares dollars against the customer's outstanding from the statement); "زبون" replaces "عميل"; period locks are per module.
- **Screens found reading the wrong field names** while migrating: approvals (needs actionCode/entityType/requestedAtUtc, no requester name — mapped through the users list) and audit log (actorName, occurredAtUtc). Inventory had hard-coded MAIN/WH2/VAN1 columns reading fields the API never returns.
- **Round 4 (2026-09-20) — accounting:** ledger features on top of ERPNext, all under `/api/v1/accounting` (`AccountingModule`) with permissions `accounting:read|manage_accounts|post_entries|reconcile`. Application code is in `Features/Accounting` (`FinancialReports` and `ChartTree` are pure and unit-tested), the ERPNext side in `ErpNextClient.Ledger.cs`, the hand-off in `JournalEntryErpNextSyncer` + `AccountingOutboxHandlers`, tables in migration 19 (`entry_types`, `journal_entries`, `journal_entry_lines`, `accounting_tags`, `accounting_tag_links`, `ledger_reconciliations`, `ledger_reconciliation_items`). `IErpNextClient` lost `ListDocumentsAsync` (the raw document browser is gone) and the per-account balance calls (balances now come from one grouped GL query).
- **Frontend:** `pages/accounting/{ChartOfAccounts,JournalEntries,Reconciliation,PartyBalances,Reports}.tsx`, `features/accounting/*` (pickers, entry dialog, tags, report components, `documents.ts` = what each report prints), `components/ui/{ImportDialog,RoutedTabs}`, `hooks/{useLoad,useCan,useConfirm,useChartAccounts}`. Menu sections with tabs replace one-entry-per-screen.
- **Bug fixed on the way:** the accounts screen offered party roles `SALES_REP`/`CARRIER` that do not exist (real codes: CUSTOMER, VENDOR, EMPLOYEE, DELIVERY_COMPANY, GOVERNMENT), so creating such an account failed with "InitialTypeCodes[0]".
- **Verified locally:** about 90 API checks against Postgres and a stateful mock ledger (chart CRUD/import, TB/BS/P&L totals, ledger with running balance and party filter, ageing, entry lifecycle incl. void → cancel, tags, reconciliation incl. undo and the void guard); 56 unit + 33 integration tests pass. **Not verified against a real ERPNext:** Frappe's own validation of Account create/rename and Journal Entry (read the error text in the entry when it fails).
- **Known gaps:** no maker-checker on manual entries (posting is one step); multi-currency entries (accounts must be in the company currency); tags cannot be renamed from the UI (API supports it); the ledger statement cuts at 5000 lines; purchase returns, tax templates and bank-statement import are not built.

### 2026-09-21 — Claude (pending work plan)
- **What existed:** `IMakerCheckerRequest` in `Common/Abstractions/Markers/`, `MakerCheckerBehavior` with `RequiresApproval` guard + replay bypass, `PostInvoiceCommand` as the model (`bool RequiresApproval => true`). No SYSTEM_ADMIN bypass anywhere.
- **What was reused:** `IMakerCheckerRequest`, `MakerCheckerBehavior`, `ICurrentUser.HasRole()`, `RoleCodes.SystemAdministrator`. Existing migration naming pattern (`*.cs`, class name = migration name).
- **What was built:** dual approval for `PostJournalEntryCommand` + `VoidJournalEntryCommand` (`IMakerCheckerRequest`, `RequiresApproval = true`); SYSTEM_ADMIN bypass added to `MakerCheckerBehavior` in ONE place; APPROVER resolution service for stock transfers (item 9).
- **Verified:** dotnet build + tests pass; endpoints exercised against Postgres.
- **Gaps:** real ERPNext validation not tested; item 3 (ledger paging), 9 (user warehouses), 7 (user edit), 5 (sales reps), 6 (invoice discount), 8 (consistency check), 10 (KPIs), 4 (old screens) still pending.

### 2026-09-21 — Claude (review of the interrupted session + Round 5)
- **What the previous session left:** one commit (`5e3d2a3`, in a Claude worktree branch `claude/fervent-heyrovsky-952240` under `.claude/worktrees/`, invisible from the main folder) and uncommitted files for warehouse
  approval. The commit was merged; its faults were fixed in `673f4a3` (period-lock marker looked at today's month; page 2 double-counted a line and page 3+ started from the wrong balance; the screen
  showed 100 rows with no pager). The uncommitted files were NOT adopted (migration without attributes and against a non-existent `users` table, hand-rolled JSON scanning with an operator-precedence bug, calls
  to service methods that do not exist, `||` where both managers must approve, and a silent change making every other approval need the APPROVER role); a backup patch was kept outside the repo.
- **Done (item numbers = the task list):** 1 dual approval for manual entries (`IMakerCheckerRequest`, SYSTEM_ADMIN exempt in `MakerCheckerBehavior`); 3 exact paged ledger statement (aggregate totals, offset totals,
  stable order, tag filter inside the query, export up to 20,000 lines); 7 users screen + backend loopholes closed (see below). Security fixes from an external static review: dev settings untracked, fail-closed
  connection strings, package-lock tracked, CI builds the frontend, Grafana on localhost.
- **Three period-lock defects found and fixed** (`1182b4f`): the cached answer was never invalidated (a lock took up to 10 minutes to apply), re-locking an unlocked month failed with a duplicate key, and the
  lock/unlock commands were themselves period-sensitive (once the cache was right a locked month could not be unlocked).
- **Users:** `PUT /users/{id}` is the profile only (it used to change roles, bypassing the approval on `AssignRoles`); `IsActive` was hard-coded true and could not be undone (now real, `/activate` added);
  admin password reset `/users/{id}/password` is deliberately not governed (an approval stores the request as plain JSON); guards in `UserService`: no self role/status change, never remove the last usable
  system administrator, unknown roles refused.
- **Not done (still open):** 2 currency display layer (see the open question in PROJECT_VISION §5.4), 4 old screens to MUI, 5 sales-rep page, 6 invoice-level discount, 8 ERPNext consistency screen, 9 per-user
  warehouses and warehouse-manager approval of transfers (design: `user_warehouses(user_id → asp_net_users, warehouse_id → locations, is_manager)`; approvers resolved in one service used by `MakerCheckerBehavior` and
  `GovernanceService`; both the source and destination manager must approve; SYSTEM_ADMIN exempt; do not change who may approve other requests), 10 ERPNext-grade KPIs.
- **Verified locally:** ~60 API checks on Postgres + the stateful mock ledger (230 vouchers paged and stitched, dual approval incl. replay, period locks, users); 84 unit + 33 integration tests; `tsc` and build clean.

### 2026-09-23 — Claude (items 9 and 6: warehouse-manager approval, invoice-level discount)
- **Item 9 — per-user warehouses and transfer approval** (`7bf61b0`). What existed: `IMakerCheckerRequest`, `MakerCheckerBehavior` (SYSTEM_ADMIN exempt), `GovernanceService`. Built on them:
  migration 20 (`user_warehouses(user_id, warehouse_id → top-level locations, is_manager, assigned_by/at)`, `approval_requests.scope_warehouse_ids uuid[]`); marker `IWarehouseTransferRequest` on ship-transfer,
  direct stock transfer and transfer request; `IWarehouseAccess` resolves the two top-level warehouses of a transfer (recursive CTE; a shelf move inside one warehouse needs no approval);
  `TransferApprovalPolicy` (pure, 15 tests) decides who may approve and when it completes. Rules: the requester's own managed side counts as consent; every other side needs one of its managers;
  a manager of any warehouse on the transfer may approve (owner decision §5.4-2); the general APPROVER role does not approve transfers; SYSTEM_ADMIN approves anything. A duplicate pending request is 409.
  Assigning warehouses: `GET/PUT /users/{id}/warehouses` (`users.manage-roles`, governed, no self-change). New role `WAREHOUSE` (25 permissions). Bug fixed: an order already in transit could be shipped again.
- **Item 6 — discount on the whole invoice** (sales, returns, purchase bills), on top of each line's discount. Migration 21: `invoices.discount_pct`, `purchase_invoices.subtotal_usd/discount_pct/discount_amount_usd`,
  SQL function `recalc_invoice_totals(uuid)` = the only place a sales invoice's totals are computed (it replaced three copies; one ignored the sign of returns). Rule in `DocumentDiscount` (tested):
  a percentage of the lines **or** a dollar amount, never both, never more than the lines; a percentage follows the lines when they change. API: `discountPct`/`discountAmountUsd` on create (sales and purchase),
  `PUT /invoices/{id}/discount` (draft only). `InvoiceDto.amounts` shows subtotal, discount, delivery. ERPNext: `apply_discount_on = "Net Total"` + `discount_amount` (negative on a return).
  Purchase: the bill discount lowers each unit's cost proportionally (factor total/subtotal) in the weighted average on post and in the reversal on void.
- **Bugs found on the way:** the invoice detail page read `subtotalSyp/discountAmountSyp/deliveryFeeSyp` that the API never returned (now `amounts`); dividing Postgres numerics gives more digits than .NET `decimal`
  holds (500 on posting a discounted bill) — round in SQL before Dapper reads it.
- **Verified locally:** unit 112, integration 33; live against Postgres + mock ERPNext: item 9 31/31, item 6 30/30 (totals in both currencies, percent following lines, delivery change, refusals, draft-only,
  PDF, ERPNext payloads for sale/return/no-discount/bill, weighted cost at the discounted price and its exact reversal); discount applied and removed in the browser on a draft invoice.
- **Known gaps:** the delivery fee is still not sent to ERPNext (its Sales Invoice total is lower than ours by the fee); a RETURN is not linked to the invoice it returns, so it carries its own discount;
  no purchase returns. (Stock lists filtered by warehouse: done, see "Item 9 completed" below.)
- **Item 5 — sales representatives.** What existed (abandoned): `invoices.sales_rep_id` and `customers.assigned_sales_rep` holding user ids with no table, FK or validation; a `SALES_REP` role row
  seeded in migration 1 with no permissions and not in `RoleCodes`; the invoice screen copied the customer's rep. Built on them: migration 22 `sales_reps(user_id → asp_net_users, commission_pct,
  monthly_target_usd, is_active, notes)` with the existing references kept as reps and FKs added; `RoleCodes.SalesRep` with a 14-permission bundle; permissions `sales_reps:read|manage` (ACCOUNTANT reads).
  API `/api/v1/sales-reps` (list with figures for a period, detail with 12-month trend/customers/invoices, candidates, save, hand over customers). A rep without `sales_reps:read` sees only themselves.
  Figures: posted SALE/RETURN only; net = sales − returns; commission on net without delivery/tax (`SalesRepMetrics`, tested); target = monthly × calendar months touched; collected = allocations of
  unreversed receipts in the period; outstanding = today's balance of the rep's sales. Invoices default to the customer's rep and refuse an inactive one (`SalesRepGuard`). ERPNext: the rep is upserted as
  a **Sales Person** under "Sales Team" (commission rate, enabled) and the Sales Invoice gets `sales_team` = that person at 100 %, so ERPNext's own sales-person reports match.
  UI: menu «المندوبون» (`pages/sales/SalesReps.tsx`, `SalesRepDetail.tsx`, `features/salesReps/*`), rep picker on the new invoice and in the customer dialog. Added `@mui/x-charts` (MUI X 7, same line as the tree view).
- **Bug fixed on the way:** saving a customer from the edit dialog cleared their rep (the dialog never sent the field and the API wrote NULL). Now a missing field keeps the rep and the empty guid removes it.
- **Verified:** unit 123; live 32/32 (backfill, role, candidates, validation, hand-over all-or-nothing, rep kept on customer edit, default on invoices, figures with a sale/return/receipt, trend, ERPNext
  Sales Person + sales_team, a rep's own scope and 403s, deactivation); both pages and the invoice picker checked in the browser.
- **Known gaps (item 5):** a rep renamed in the users screen becomes a new Sales Person in ERPNext on the next invoice (old one stays); commission is a report, not a posted expense; no split of one
  invoice between several reps; reps are not filtered by warehouse.
- **Item 8 — ERPNext consistency check and reference data.** What existed: the sync log (every local record ↔ its ERPNext name), the Accounting Sync screen, `GetCompanyAsync`. Built:
  `GET /api/v1/accounting/erpnext/consistency` (`accounting:read`) compares seven sections (customers, suppliers, items, sales invoices, purchase invoices, payment entries, journal entries incl.
  cost-of-goods and adjustment entries): counts and dollar totals on both sides, and every difference — NOT_SENT (with the last error), MISSING_IN_ERPNEXT, ONLY_IN_ERPNEXT, CANCELLED_IN_ERPNEXT_ONLY,
  NOT_CANCELLED_IN_ERPNEXT, AMOUNT_DIFFERS. The decision is `ConsistencyComparer` (pure, 6 tests); ERPNext is read through `IErpNextClient.GetDocumentIndexAsync` for a fixed set of doctypes. Nothing is fixed
  automatically; the existing sync job re-sends failures. `GET /accounting/erpnext/reference/{kind}` returns fixed read-only lists (cost-centers, modes-of-payment, sales-taxes, purchase-taxes, fiscal-years,
  exchange-rates with our own rate for the same day) — deliberately not a document browser (unknown kinds are 400). UI: tabs on «مزامنة المحاسبة» (log / consistency / reference); every difference and every
  sync-log row links to its local record (`features/accounting/erpnextLinks.ts`).
- **Gap closed:** the delivery fee now reaches ERPNext as an "Actual" charge on the company's income account (after the invoice discount), so a Sales Invoice's grand total equals ours; before, ERPNext's
  receivable was lower by the fee on every invoice that had one.
- **Verified:** unit 129; live 27/27 against a mock that stores what it receives (`scratchpad/mock_erp2.js`): matched invoice (62 = 62 incl. fee and discount), deleted / changed amount / cancelled
  only there / voided only here / stranger in ERPNext, FAILED listed with its error, COGS entries matched, reference lists and the same-day rate, 400 for other doctypes, 403 without `accounting:read`,
  accountant allowed; screen checked in the browser.
- **Known gaps (item 8):** the comparison reads every record of each doctype on each run (fine for thousands, not for hundreds of thousands — add date filters then); stock-adjustment entries are compared
  by existence only (they were valued at the cost of the day they were sent); parties deactivated here stay enabled in ERPNext and are reported as such; the "only in ERPNext" list cannot tell a
  document made directly in ERPNext from one whose sync-log row was lost.
- **Item 9 completed — warehouse scoping of lists and actions** (the part of the spec left open earlier). Migration 23: SQL function `user_visible_locations(user)` = assigned warehouses and
  everything under them. Permission `inventory:all_warehouses` (SYSTEM_ADMIN, ACCOUNTANT, PURCHASER, AUDITOR) sees every warehouse; everyone else only their own — no assignment means empty lists.
  Lists filtered in SQL: receiving, transfer requests/orders (either side), issue orders, cycle counts, adjustments, stock, item movements, warehouse overview. Details and every warehouse command
  carry `IWarehouseScopedRequest`; `WarehouseScopeBehavior` (after authorization) asks `WarehouseScope` which locations the request touches and refuses with 403
  `Authorization.WarehouseNotAssigned`. Transfers: create needs one side, ship the source, receive the destination; an approved request being replayed is not re-checked. Unknown documents stay 404.
  Not filtered on purpose: the sales item picker (sellers need stock everywhere) and the plain location list used by pickers (a transfer needs the other warehouse as destination).
- **Verified:** unit 135 (6 new for the behaviour); live 21/21 (four document lists × five kinds of user, transfer lists, stock, movements, overview, 403 on another warehouse's documents and
  new work, transfers allowed from one side, ship refused to the wrong side, unknown = 404, admin unaffected); item 9 approval test re-run 31/31.
- **Item 10 — KPIs on the dashboard with filters.** What existed: `GetDashboardSummaryQuery` (fixed current-month tiles, used by both the dashboard and a duplicate KPI page), the ledger
  reports (`FinancialReports`, P&L query). Built: `GET /api/v1/dashboard/kpis?from&to&warehouseId&customerId&salesRepId&categoryId` (`GetBusinessKpisQuery`, `invoices:read`) — net sales,
  returns, cost, gross profit and margin; purchases; receivables and payables with ageing buckets and DSO; cash in/out; stock value, out-of-stock, below-reorder and slow movers (90 days);
  12-month trend; top customers, top items, sales by rep; overdue invoices. Net profit is ERPNext's P&L for the same period, fetched through the existing P&L query (company-wide only).
  Sales are invoice lines scaled to their invoice's net total (after line and invoice discounts, without delivery and tax), so they always add up to the invoices; categories include
  sub-categories (ltree); a warehouse includes its shelves. Sections a filter does not apply to are hidden or left unfiltered and the screen says which. The arithmetic is `KpiMath` (tested).
  UI: `features/dashboard/BusinessKpis.tsx` on the home screen (each card links to its report); `/kpi` redirects home and its menu entry and page are gone; the summary endpoint now returns only
  today's work (latest invoices, open alerts), so no figure is computed twice. Removed unused `SalesChart` and `KpiCard`.
- **Found on the way:** two seed invoices (INV-2026-00001/00002) store a subtotal that differs from their lines by $0.26 / $0.46 (data from before the single totals function). KPIs follow the
  invoice totals as issued; the data itself was not changed.
- **Verified:** unit 147; live 29/29 against hand-written SQL on a different path (invoice headers instead of lines): net sales, returns, cost, invoice count, margin, receivables and buckets,
  DSO, overdue ordering, purchases, payables, cash, stock value, slow movers, the September month of the trend, rep totals, ledger net profit = P&L report, customer / rep / category / warehouse
  filters, 400 on a reversed period, 403 for warehouse staff, a sales rep without purchases or net profit; the screen and the currency switch checked in the browser.
- **Known gaps (item 10):** receivables are today's balances of invoices dated in range (not the balance as it stood on the end date); payables are not split by supplier on the dashboard;
  no dashboard export yet (every card links to a report that has one).
- **Item 2 — one Money display.** Ready-made tools only: `Intl.NumberFormat` (`lib/format.ts`), the existing latest-rate hook `useFxMid`, zustand `persist` for the choice
  (`stores/displayStore.ts`). `components/ui/Money.tsx`: an amount kept in dollars shown in both currencies, lira first by default (owner decision) and the other small; a switch in the top bar
  flips which comes first for every screen and is remembered. A lira amount recorded on the document (invoice, receipt, statement line) is used instead of today's rate; a lira-only record shows
  lira only. Rolled out to: dashboard, invoice list, customer page and statements, receivables/payables ageing, purchasing (bills, supplier payments, bill total), trial balance, balance sheet,
  profit and loss, ledger statement, journal entries, chart of accounts (USD accounts), warehouses (stock value), sales reps, ERPNext consistency. Exports keep plain dollar numbers.
- **Deliberate exceptions:** entry forms stay in the currency typed (bills, manual entries, payments); bank reconciliation stays in the account's currency because it is matched line by line with a
  bank statement. The old-style screens (invoice workspace and detail, payments, item card, warehouse documents, pickers) get `Money` as part of their MUI migration (item 4).
- **Item 4 — the last old-style screens on MUI.** Rebuilt on the shared kit with the same props / API calls: pickers (`EntityPicker` → async MUI Autocomplete, `LocationSelect`,
  `ReasonCodeSelect`, `FxRateField`, `ItemPickerModal` as an MUI Dialog), `WmsLinesEditor`, and the screens login, invoice workspace (MUI Stepper) and detail, item card
  (`RoutedTabs`), payments, receiving, transfers, issue orders, cycle counts, stock adjustments. Expandable rows became dialogs (pick/verify/issue, count lines, putaway);
  every `window.prompt` became `ReasonDialog`; lists use `DataTable` with server paging; amounts use `Money`. Deleted: `styles/theme.css` (its global rules — font smoothing,
  thin scrollbar — moved to `MuiCssBaseline` in `theme.ts`), `ErrorBanner`, `LoadingSpinner`, `Pagination`, `StatusBadge`, `EmptyState`, `KpiCard`, `SalesChart`, `KpiDashboard`.
- **Bugs found on the way:** transfers in transit had no "receive" button (the page looked for status SHIPPED, the server sets IN_TRANSIT); the login page showed a "remember me"
  checkbox and a "forgot password" link that did nothing (removed; the page says the administrator resets passwords); the invoice detail page read a `payments` list the API never returns
  (now paid / remaining from the invoice itself).
- **Verified in the browser:** every migrated screen loads its data; item picker adds a line with its available quantity; a sales invoice created end to end through the new
  workspace (customer → due date from terms → rep default → item → review → saved and opened); payments, item card tabs, login. `tsc` (also with --noUnusedLocals) and the production build clean.

### 2026-09-23 — fixes from an external audit
An outside review was checked claim by claim; only the confirmed findings were fixed, the rest were answered.
- **Invoice period lock:** post and void checked the lock against *today* (the endpoint passed `DateTime.UtcNow`), so an invoice dated in a locked month could still be posted or voided into ERPNext. Now every step checks the invoice's stored date (`InvoicePeriod`); creation is `IPeriodSensitiveRequest` on the invoice date; drafts in a locked month cannot be edited or confirmed. The date parameter was removed from `PostInvoiceCommand` / `VoidInvoiceCommand`.
- **Sales and reserved stock:** a sale took any on-hand quantity, including reserved; now `on_hand − q ≥ reserved` (one atomic UPDATE). The warehouse balance moves in the same transaction instead of up to 5 minutes later; running the sync job afterwards changes nothing (verified).
- **ERPNext duplicate parties:** a failed "does this customer exist?" lookup was read as "no" and created "X - 1". It now fails; the sync log shows FAILED and `SyncCatalogToErpNextJob` (every 30 min) retries it (`ErpNextPartyUpsertTests`).
- **Security:** no default admin password anywhere (init script generates one into the git-ignored settings); Scalar only in Development; CSP / Permissions-Policy / body size / refresh limit in nginx; per-user limit on exports, PDFs, imports and AI. Unused packages removed.
- **Answered, not changed:** item import cannot double-create (each row is its own idempotent `CreateSku`, existing codes are skipped); the balances upsert is correct (the unique index is `NULLS NOT DISTINCT`); issue orders already lock rows; approvals by warehouse managers are intentional; `ReceiveBatch` rolls back and rethrows on purpose (unexpected errors must reach the global handler); `window.prompt` was already gone.
- **Verified:** 22 live checks on Postgres (lock on create/lines/discount/delivery/confirm/post/void, today's invoices unaffected, reserved stock refused, balance −2/+2 at once and equal to the job's result, PDF 30 → 429); CSP tested against the production build (fonts, charts, print frame; no violations); `nginx -t` passes; unit 151 + integration 33; `tsc` and build clean.

### 2026-09-23 — sign-in tokens out of the browser's storage (H-11)
- **Before:** both tokens were saved in localStorage (readable by any injected script); the app never used the refresh token, so every user was signed out when the 15-minute access token expired; sign-out only cleared the browser; a deactivated user could keep renewing for 7 days.
- **Now:** `erp_rt` HttpOnly cookie (SameSite=Strict, Secure outside Development, path `/api/v1/auth`) carries the refresh token; `AuthSessionResponse` (access token, user, permissions) is the only JSON; the store keeps it in memory. On load `App` calls `refreshSession()` and `PrivateRoute` waits (`checking`). A 401 triggers one shared refresh and a retry. Sign-out calls `/auth/logout`, which revokes the token and clears the cookie. Refresh and logout need `X-Requested-With`. The auth endpoints are no longer idempotency-cached (their answers are credentials plus a cookie). `RefreshTokenRequest`/`LogoutRequest` were removed.
- **Verified:** 17 API checks (cookie flags, no token in the body, CSRF header, rotation, reuse refused, logout revokes, deactivated user refused); in the browser on the production build with the CSP: session restored from the cookie alone, localStorage empty, cookie invisible to script, with a 1-minute token five parallel 401s → one refresh → five retries → still signed in; sign-out revokes (refresh 401), reload lands on login. 5 new integration tests.

### 2026-09-23 — scheduled database backups (H-5)
- `scripts/backup-db.sh [daily|predeploy|manual]` and `scripts/restore-check.sh`; scheduled by `deploy-vps.sh` into `/etc/cron.d/autoparts-erp-backup`; settings `BACKUP_DIR`, `KEEP_*`, `BACKUP_REMOTE` in `.env.vps` (template updated). `.gitattributes` forces LF for `*.sh`.
- **Bug caught while testing:** a `[[ weekday ]] && …` line ended the script under `set -e` on every non-Sunday, silently skipping rotation and the off-server copy; replaced with `if` blocks and checked the exit code.
- **Verified locally** against the dev database: 3 runs with `KEEP_DAILY=2` (rotation, weekly copy, exit 0), restore check restored 83 tables / 38 users / 38 invoices in a throw-away container; `bash -n` and shellcheck clean.

### 2026-09-23 — WhatsApp assistant (Groq for intent only, everything else local)
- **Pieces:** `whatsapp-gateway/` (Node 20 + Baileys 7, transport only, compose service `whatsapp` under profile `whatsapp`, volume `whatsapp-auth`);
  `AssistantModule` (`/internal/assistant/whatsapp/inbound` + `/gateway/status` with `X-Gateway-Secret`; admin `/api/v1/assistant/*`);
  `Features/Assistant`: `WhatsAppAssistant` (flow), `AssistantAnswers` (five read-only questions), `EntityMatcher`, `RuleBasedIntents`, `AssistantLinks`;
  `GroqIntentExtractor`, `RedisAssistantState`, `AssistantIdentity` (acts as the linked user via `IAuthService.BuildPrincipalAsync`);
  migration 24 (`ar_norm()`, trigram indexes on normalized names, `assistant_links`, flag `WHATSAPP_ASSISTANT`); permissions `assistant:use` (+ACCOUNTANT), `assistant:manage`;
  screen **مساعد واتساب** (`pages/settings/WhatsAppAssistant.tsx`, QR via `qrcode`). Config: `Ai:*`, `Assistant:GatewaySecret`, `Assistant:UtcOffsetHours`; VPS: `WHATSAPP_ENABLED`, `AI_API_KEY`.
- **Found on the way:** invoices have a third type `CREDIT_NOTE` (the reversal a void creates) — labelled in answers; the gateway first marked messages read / "typing" before knowing whether to answer, which would reveal the bot to strangers — now nothing is visible unless a reply is sent.
- **Verified:** 37 end-to-end checks against Postgres with a recording stand-in for Groq (secret required; strangers silent; code only from its own number, stored hashed; ة/ه-hamza variants and typos matched; numbered choice incl. Arabic digits; answers equal the statement/stock in the DB; **no ERP data in any model request**; model down → keyword rules; dedupe; 20/min limit; flag off; audit; revoke); the real gateway container reached WhatsApp and its QR showed on the screen; UI link flow in the browser; 32 new unit tests (183 total), integration 35; `tsc`/build clean.

### 2026-09-24 — ERPNext v16 break (root cause), party identity, full audit
- **Symptom (production):** chart of accounts, balances, dashboard profit: "SQL functions are not allowed as strings in SELECT: sum(debit) as debit" with a Frappe traceback on screen.
- **Root causes, all fixed:** (1) the client spoke one Frappe dialect — v16 refuses SQL-text aggregates and v≤15 refuses the dict form → `Aggregate` + per-server dialect learning; (2) raw ERPNext bodies (tracebacks) reached screens and the sync log → `ErpNextErrors`, full body only in the server log; (3) the test mock accepted anything → it now emulates v16/v15, series naming, link validation, 404s; (4) the chart screen failed whole when only balances failed → shows the tree and says why balances are missing.
- **Found while tracing it:** customers/suppliers were identified in ERPNext by display name — two customers with the same name shared one ERPNext record (balances merged) and on series-named sites (CUST-00001) documents would reference a non-existent customer; a FAILED party sync lost the link and created a duplicate record next time; the ledger statement crashed (500) when an ERPNext name was recorded twice (`TagResolver` `ToDictionary`). All fixed (`ErpNextPartyLinks`, `DISTINCT ON`).
- **Audit sweeps:** authorization (every endpoint/request) — clean; SQL built from user input — clean (all fragments are fixed text + parameters); 20 data invariants on the dev DB — the mismatches were old demo/dev data (demo invoices whose headers ≠ lines, stock without opening movements, sales before the SALE ledger existed); stock ↔ movement ledger drift measured across all stock-moving scenarios — 0.
- **Also fixed:** SignalR could not authenticate over WebSocket, any user could join any hub group; demo data was seeded in production; a fresh development database crashed at start (demo customers named a sales rep that migration 22 requires to exist); demo invoices now equal their lines and demo stock has opening movements; robots.txt + X-Robots-Tag + meta robots.
- **Still open (closed the same day, next entry):** the server never sent SignalR events.
- **Verified:** mock as Frappe 16 and 15 (47 accounting checks identical, v16: 0 refused requests; v15: one refusal then text form learned); party identity in both naming modes (13/13 each); full regression (batch, discounts, payments, consistency, warehouses, scopes, locks, cookies, accounting); empty database from zero — all migrations, seeders, 20 invariants clean; unit 193, integration 35; tsc/build; nginx -t.

### 2026-09-24 — live notifications, stock alerts that exist, demo-data reset
- **Notifications (SignalR, server-decided):** `IRealtimeNotifier` (Application) → `HubRealtimeNotifier` (Api). `ErpHub.OnConnectedAsync` puts each connection in `user:{id}` and in `perm:{code}` for the codes in `RealtimeGroups.NotifiedPermissions` it holds — clients cannot choose groups. Events: `NewApprovalRequest` (to `approvals.review` holders + managers of the warehouses in the request's scope, not the requester), `ApprovalDecided` (final approve or reject → requester only), `StockAlert` (to `inventory_alerts:read`). Sent by `ApprovalNotifications` (from `ApprovalService`/`GovernanceService`) and `StockAlertScanner`; a send failure is logged, never fails the business action.
- **Browser:** `hooks/useRealtime.ts` (replaces `useSignalR.ts`, mounted in `AppLayout`): toast with a button to the screen, menu badges (pending approvals, open stock alerts) from `stores/realtimeStore.ts`, Approvals and InventoryAlerts reload on their version counter; counts are re-read from the API on (re)connect; token from `freshToken()` (renews if < 60 s left).
- **Stock alerts were never produced:** `inventory_alerts` had readers but no writer, and the item card edits `items.reorder_level` while stock/dashboard/reports read `skus.reorder_level`. Migration 25 `AddStockAlertSupport`: trigger copying `items.reorder_level` → `skus`, backfill, one open alert per item and type (unique partial index). `StockAlertScanner` (pure `Decide`: ≤ 0 OUT_OF_STOCK, ≤ level LOW_STOCK, level 0 = not watched; resolves alerts that no longer apply) runs after every posted/voided invoice (outbox handlers, only that invoice's SKUs) and every 10 minutes for everything (`LowStockAlertJob`, was daily).
- **Demo-data reset:** `scripts/reset-business-data.sh` (dry run → typed `DELETE-ALL-BUSINESS-DATA` → stops api/whatsapp → `docker compose run api --reset-business-data --confirm=…` → starts again). Code: `Api/Maintenance/ResetBusinessData` (order: ERPNext first; if it is not fully emptied this DB is not touched), `Infrastructure/Maintenance/ErpNextCompanyWipe` (Transaction Deletion Record → v16 `generate_to_delete_list` → submit → wait for Completed; then Item Price, Item, Sales Person (leaf), Customer, Supplier), `BusinessDataReset` (explicit `Wiped`/`Kept` table lists — **a new table must be added to one of them or the reset refuses to run**; TRUNCATE in one statement, numbering sequences restart, non-system entry types dropped, users without SYSTEM_ADMIN removed unless `--keep-users`). Options `--dry-run`, `--skip-erpnext`, `--keep-users`. Safe to repeat.
- **Bug caught by the live test:** Dapper cannot pass a `uuid[]` column to a positional record's constructor ("parameterless default constructor … System.Array") — the new-request notice was silently not sent. The array is now read on its own.
- **Verified:** 22 live checks with five SignalR clients (managers of the two warehouses told, uninvolved manager not, reviewer told, pending count; requester told only on the final decision, rejection with its reason; bad token refused; item-card level reaches the SKU; posted sale below the level → one alert pushed and one open row, a second sale does not repeat it, restore + void → resolved). Reset on a copy of the dev DB (1,996 rows, 74 users; guard passed; admins, roles, reason codes, categories kept; numbering from 1) and against the mock as Frappe 16 and 15, repeated on the empty copy. Unit 205, integration 35, tsc/build.
