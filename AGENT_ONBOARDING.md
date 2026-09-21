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
Deploy with `./scripts/deploy-vps.sh` only (see SETUP_HARDENING.md). New in the app image: embedded fonts for PDF (no OS fonts needed).

**What is built and working (verified in the running system):**
- Governance pipeline (Validation → Authorization → Idempotency → PeriodLock → MakerChecker) — incl. a working
  approval **replay** (`IApprovalReplayContext`) so an approved request actually executes.
- ~28 Carter modules; 18 raw-SQL migrations (later ones use ids `202401010000NN`); 60+ tables.
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
- Frontend screens (all RTL Arabic): Login, Dashboard, KPI, Customers (+detail), Parties (+combined statement),
  Invoices (+workspace, detail), FX rates, Items list + **item card** (details/edit, stop-ship, stock, aliases,
  interchanges, prices), Inventory, Receiving, Transfers, Cycle counts, Adjustments, Issue orders, Inventory
  alerts, Approvals, Audit log, Period locks, Users (list), Roles, **Accounting Sync**.
- Server-side paging on: invoices, customers, parties, inventory, users, approvals, audit, ERPNext sync log,
  items. Still unpaged in the UI: receiving, transfers, cycle counts, adjustments, issue orders, FX rates.
- CI (`.github/workflows/deploy.yml`): build + unit + integration tests on every push; the deploy job only
  runs when the VPS secrets are configured (they are not yet, so CI is build/test only) and it is GREEN.

**What is stubbed or missing (do not assume it works):**
- **AI is not implemented.** `AiService.ChatAsync` now returns an explicit `Ai.ProviderNotConfigured` error (it used to
  echo the user's text); nothing generates suggestions; no screen calls `/ai/*`. See PROJECT_VISION §6.
- Backend with no screen: AI, Payments, Warranty, Reports, Barcodes, Catalog categories, batches, user/role
  editors, reason codes. Tables with no API: `party_contacts`, `party_addresses`, `party_notes`,
  `attribute_schemas`, `item_reorder_settings`, `barcode_scan_logs`.
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
17. **Local secrets/noise:** `ADMIN PASSWORD.txt`, `*password*.txt`, `appsettings.Development.json`,
    `scripts/logs/`, `*_run_*.log` are git-ignored. One of them was committed once and had to be purged — check
    `git status` before every commit.
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
