# PROJECT_VISION.md — AutoPartsERP

> **Isolation notice:** This document describes **`autoparts_erp.net`** only — a standalone .NET / PostgreSQL /
> React ecosystem (plus ERPNext as a headless accounting engine). It shares nothing with any prior
> Next.js/NestJS/TypeScript project. *Last verified against the code: 2026-09-21 (live server last checked 2026-09-19).*

---

## ⚠️ RULES FOR ANY AI AGENT WORKING ON THIS PROJECT

1. **ISOLATION RULE:** never import a stack, pattern, port or convention from any other project.
2. **GOVERNANCE-BY-PIPELINE RULE:** authorization, idempotency, period-lock, maker-checker and audit are enforced by
   MediatR behaviors + marker interfaces. Never reimplement them in handlers or endpoints.
3. **RESULT PATTERN RULE:** business outcomes use `Result` / `Result<T>` + `Error`.
4. **snake_case RULE:** the database is snake_case; Dapper SQL aliases back to PascalCase.
5. **BUILD HYGIENE RULE:** Api / Application / Infrastructure build with `TreatWarningsAsErrors=true`.
6. **LANGUAGE RULE:** Arabic is the end-user language, RTL must stay intact. i18next is initialised but not yet
   used by any screen (strings are hardcoded Arabic) — see debt D3 below.
7. **CONTEXT-CHECK RULE:** run `git log`, `git status` and read the target module before coding.
8. **STATUS RULE:** whenever a Phase/Epic item completes, update its status in section 5 of this file in the same
   commit.
9. **ACCOUNTING RULE:** anything ledger-related goes through `IErpNextClient` and is shown in OUR UI; users never
   open ERPNext.
10. **TRUTH RULE:** never mark something "done" that is a stub. A screen with no backing behaviour, or a job that
    records success without doing work, is a defect (see the AI section).

---

## 1. Project Codename & Purpose

- **Codename:** AutoPartsERP · **Namespace root:** `AutoPartsERP` · **Solution:** `AutoPartsERP.sln`
- **Purpose:** a governance-first, Arabic-first ERP for an automotive spare-parts trading business: warehouse
  management, inventory & batches, catalog / part-number intelligence, sales invoicing, payments, customers &
  parties (customer/supplier duality), warranty, FX rates, reporting, and an assistant layer (AI).
- **Accounting:** delegated to ERPNext (headless) with the app as the single UI. The ledger currency is **USD**.
- **Design ethos:** governance as a first-class concern — enforced centrally, not per endpoint.

---

## 2. Verified Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Runtime / SDK | .NET 9, SDK 9.0.312 (`rollForward: latestMajor`) | `global.json` |
| Compiler hygiene | Nullable + ImplicitUsings everywhere; `TreatWarningsAsErrors` on **Api, Application, Infrastructure** only | Domain/Contracts: debt D2 |
| API host | ASP.NET Core Minimal APIs + **Carter** modules (~28) | `Api/Modules/*` |
| CQRS | **MediatR 12** behaviors: Validation → Authorization → Idempotency → PeriodLock → MakerChecker | `Program.cs` |
| Validation / mapping | FluentValidation 11, Mapster | |
| Writes | EF Core 9 + Npgsql; **raw-SQL migrations** (19) | `Persistence/Migrations` |
| Reads | **Dapper 2** on snake_case tables + `DapperTypeHandlers` (`DateOnly`, `DateTimeOffset`) | |
| Database | **PostgreSQL 16** (`ltree`, `pg_trgm`, `uuid-ossp`; `pgvector` for embeddings) | On the VPS it runs on the host |
| AuthN / AuthZ | ASP.NET Identity (Guid keys) + **JWT RS256**; permission-based (`PermissionCodes`) with seeded role→permission map (`RolePermissionMap`) | |
| Cache | Redis (StackExchange.Redis, distributed cache) | |
| Jobs | **Hangfire** (Postgres storage), queues `default` + `governance`; **Outbox** dispatcher hosted service | |
| Rep performance | `/sales-reps`, `/sales-reps/kpis` | commission on gross profit, target gauge and monthly target vs achieved per rep, KPI comparison of all reps |
| Landed cost | migration 28, `/landed-costs`, `LandedCostDialog` | ERPNext-style Landed Cost Voucher: charges split by value/qty/equally, capitalized on stock on hand, the sold share to COGS; suppliers owed get service bills |
| Returns | migration 27, `…/returns` endpoints, `ReturnDialog` | sales and purchase returns against their invoice (ERPNext `is_return` / `return_against`), capped by DB triggers, costed at the original cost, credited to the original |
| Numbering | `document_series` + triggers (migration 26) | one gapless series per document kind / entry type; recorded Super Admin deletion; previous/next in every document view |
| Realtime | SignalR hub `/hubs/erp` | server-decided groups; approvals (new/decided) and stock alerts pushed as toasts + menu badges (2026-09-24) |
| Audit | Audit.NET (EF + PostgreSql sinks) + immutable audit tables | |
| Idempotency | IdempotentAPI + `DistributedIdempotencyService` + `IdempotencyBehavior` | send `Idempotency-Key` on POSTs |
| **Accounting engine** | **ERPNext (Frappe) via REST**, `IErpNextClient` → `ErpNextClient` / `NullErpNextClient`; `erpnext_sync_log` | Live on the VPS, port 8080 |
| Observability | OpenTelemetry (traces + Prometheus metrics at `/metrics`), Serilog, health checks | |
| Docs | OpenAPI + Scalar (Development only; not proxied by nginx) | D6 closed 2026-09-23 |
| Files | **One engine for print/export** (`IDocumentRenderer`: QuestPDF + ClosedXML + embedded Noto fonts, Arabic RTL) behind `POST /api/v1/exports/{pdf|xlsx|csv}`; CsvHelper for imports; QRCoder (QR) | QuestPDF Community licence |
| Frontend | **React 19, Vite 6, TypeScript 5.7, react-router 7, Zustand 5, axios, sonner, @microsoft/signalr** | |
| Frontend styling | **MUI v6** (`createTheme`, `direction:'rtl'`) + emotion cache + `stylis-plugin-rtl`; the design tokens live in the theme; `theme.css` removed (2026-09-23) | **Adopted 2026-09-19** |
| Frontend data | **@tanstack/react-query v5** (`useQuery`/`useMutation`, 30s `staleTime`); `lib/apiClient.ts` envelope unwrapper | **Adopted 2026-09-19** |
| Frontend forms | **react-hook-form v7** + **Zod v3** (`zodResolver`); MUI `Dialog` replaces every `window.prompt` | **Adopted 2026-09-19** |
| Frontend i18n | **react-i18next** `useTranslation` active; `ar.json`/`en.json` expanded; language switcher in Topbar | **Adopted 2026-09-19** |
| Reverse proxy / TLS | nginx in Docker; self-signed cert on the IP until a domain exists | |
| CI | GitHub Actions: build + unit + integration tests; deploy job gated on secrets | GREEN |
| Tests | UnitTests (84), IntegrationTests (33, need Docker; auth/health only — they do not run SQL), E2ETests (empty project) | |

**Stack adoption status — updated 2026-09-19 (owner-approved full migration; see §2a):**
- **Frontend — NOW ACTIVE:** `@mui/material` v6, `@mui/x-charts`, `@tanstack/react-query` v5, `react-hook-form` v7, `zod` v3, `@emotion/cache`, `@emotion/react`, `stylis-plugin-rtl`, `react-i18next`.
- **Removed 2026-09-23 (unused):** frontend `@tanstack/react-table`, `@zxing/*`, `@mui/x-data-grid`, `@playwright/test`; backend `Microsoft.SemanticKernel`, `Microsoft.Extensions.AI`, `Pgvector`(+EF), `FluentEmail.*`, `ZXing.Net`, `SkiaSharp`. Add back the one a feature actually needs when it is built (the `vector` Postgres extension stays).

### §2a — Owner-Approved Policy Override (2026-09-19)

> **Decision:** The owner explicitly approved on 2026-09-19 the full adoption of the "phantom" frontend stack that was installed but never wired. This supersedes the prior prohibition in `ENGINEERING_PLAYBOOK.md §2.5` and `AGENT_ONBOARDING.md` starter-prompt.

**Rationale (audit, 7,686 LOC read):** All libraries were already in `package.json`. The app had 641 inline `style={{}}` objects, 6 `window.prompt` calls for critical input, zero caching, and i18next completely unused. Migration installs nothing new — it activates the existing stack.

**Governance constraints:**
1. Vex CSS design tokens (`#5c54ff` palette, 12 px radii, card shadows) are preserved inside `createTheme` — visual identity unchanged.
2. `theme.css` was deleted once its last consumer was migrated (2026-09-23).
3. Arabic remains the primary language; the language switcher is additive.
4. All `window.prompt` calls replaced with Zod-validated MUI `Dialog`s in Phase 3.
5. Every doc referencing the old prohibition is updated in the same commit as the code change.

### Solution graph
```
Domain (no deps) → Contracts → Application (CQRS, behaviors, abstractions) → Infrastructure (EF, Dapper, jobs,
services, ERPNext client) → Api (Carter modules, composition root)        tests/*        frontend/
```

---

## 3. Screen / Feature Inventory (verified)

Legend — ✅ backend + working UI · 🟡 backend only (no UI or thin UI) · ⚠️ exists but stubbed/incorrect · 🔴 missing

| Domain | Backend | Frontend | Status |
|---|---|---|---|
| Auth | Login/refresh/logout/me; refresh token in an HttpOnly cookie, access token in memory, silent renewal on 401 | `Login.tsx`, `authStore`, `lib/session.ts` | ✅ (2026-09-23) |
| Dashboard / KPIs | **`GET /dashboard/summary`** (server-aggregated) + `/kpi/admin/*` definitions | `Dashboard.tsx` (cards, 30-day chart, recent invoices, top customers), `KpiDashboard.tsx` | ✅ |
| Users | create, edit profile, assign roles (governed), password reset, activate/deactivate (governed); guards: no self role/status change, last system administrator protected | `Users.tsx` + `UserDialog` (paged, status filter) | ✅ (warehouse assignment: open, item 9) |
| Roles & permissions | create, grant/revoke | list/partial | 🟡 |
| Approvals (maker-checker) | pending/approve/reject **+ replay executes the request** | `Approvals.tsx` (paged) | ✅ |
| Audit log | list/detail | `AuditLog.tsx` (paged, filters) | ✅ |
| Period locks | lock/unlock/re-lock/list per module; effective immediately (cache invalidated on change) | `PeriodLocks.tsx` | ✅ (fixed 2026-09-21) |
| Reason codes | create/list | — | 🟡 |
| Customers | full CRUD + statement | `Customers.tsx` (paged), `CustomerDetail.tsx` | ✅ |
| Parties (customer/supplier) | CRUD, types, statements | `Parties.tsx` (paged), `CombinedStatement.tsx` | ✅ (contacts/addresses/notes tables have no API) |
| **Items + item card** | browse/search/get/create/update/aliases/interchanges/stop-ship/stock | `Items.tsx`, `ItemCard.tsx` (5 tabs) | ✅ |
| Catalog (SKU, categories, prices) | full | prices inside item card only | 🟡 no category management |
| Inventory stock | stock/batches/trace/receive/adjust/transfer | `Inventory.tsx` (paged) | ✅ (no batch screens) |
| Warehouses & locations | create / edit / deactivate (blocked while stock exists, no cycles), per-location stock overview | `Warehouses.tsx` (`/inventory/warehouses`) | ✅ |
| **Item movements (ledger)** | `GET /inventory/movements` over `inventory_movements`, written by receiving, putaway, transfers, adjustments, invoices (sale/return/void), direct receive/adjust/transfer and issue orders; running balance per item | `Movements.tsx` (`/inventory/movements`) | ✅ |
| **Purchasing** | purchase invoices (draft → post = stock in + cost + ERPNext; void), supplier payments with multi-bill allocation and reversal | `Purchasing.tsx` (`/purchasing`) | ✅ |
| Receiving / Putaway | full | `Receiving.tsx` | ✅ (paged, pickers, view/print) |
| Transfers | requests/orders/ship/receive | `Transfers.tsx` | ✅ (unpaged) |
| Cycle counts | plan/record/approve variance | `CycleCounts.tsx` | ✅ (unpaged) |
| Stock adjustments | create/post | `StockAdjustments.tsx` | ✅ (unpaged) |
| Issue orders (picking) | full | `IssueOrders.tsx` | ✅ (unpaged) |
| Inventory alerts | list/ack/resolve + low-stock job | `InventoryAlerts.tsx` | ✅ |
| Invoices | create/lines/confirm/post/void/PDF | `Invoices.tsx` (paged), **`InvoiceWorkspace.tsx` (item-picker dialog, customer/FX pickers)**, `InvoiceDetail.tsx` | ✅ (full lifecycle verified on Postgres) |
| **Payments & allocations** | create, allocate to many invoices, reverse (returns the money to the invoices) — **synced to ERPNext as Payment Entry; reversal cancels it** | — | ✅ screen `/payments` (auto-allocation oldest-first, reversal) + backend + ERPNext link |
| FX rates | list/latest/create | `FxRates.tsx` | ✅ |
| **Accounting (ledger in ERPNext)** | `/accounting/*`: chart of accounts (create / rename / retype / disable, import .xlsx/.csv with dry run, export), dynamic entry types, manual entries (draft → post → Journal Entry in ERPNext, void → cancel), tags on entries and ledger vouchers, ledger-account reconciliation (ticked lines stored here, amounts read from ERPNext), trial balance / balance sheet / profit & loss / ledger statement / reconciliation statement, receivables and payables with ageing | `ChartOfAccounts.tsx`, `JournalEntries.tsx`, `Reconciliation.tsx`, `PartyBalances.tsx`, `Reports.tsx` | ✅ (verified end to end on Postgres + a stateful ERPNext mock) |
| **Print / export / import** | generic renderer; item import (.xlsx/.csv, dry run, idempotent) `POST /items/import` | `DocumentDialog`, `ExportMenu`, `ItemImportDialog`; view/print on transfers, issue orders, counts, adjustments, receiving, receipts; export on items, invoices, payments, statements, movements, warehouses, chart | ✅ |
| Warranty | list/claim/process/reject + expiry job | — | 🟡 |
| Reports | P&L, inventory value (+Excel), batch trace, account statement | — | 🟡 |
| Barcodes | scan, generate item/batch codes | — | 🟡 (no scanner UI) |
| **ERPNext accounting sync** | trigger, paged log, summary; Items, Customers, Suppliers, Sales Invoices, Payment Entries; cancel on void/reversal | `AccountingSync.tsx` | ✅ |
| **AI assistant / suggestions / KB** | see §6 | — | 🔴 not implemented (chat now returns an explicit "not configured" error) |
| Realtime notifications | `ErpHub` | signalr client in one place | 🟡 |
| Purchase invoices, bank/cash accounts, POS, public invoice link, e-mail/SMS, CRM extras, backups | — | — | 🔴 |

> **Overall assessment:** the backend is broad and the governance layer is strong. The frontend now covers the
> WMS, sales, governance and the first accounting screens. The main remaining work is (1) the accounting core
> (payments, purchases, banks, reports), (2) the sales experience (POS, public invoice links, notifications),
> (3) CRM, (4) the reports centre, (5) admin/security screens and backups, (6) a **real** AI layer.

---

## 4. Core Architectural Principles

1. **Governance by pipeline, not by endpoint** — marker interfaces on commands, fixed behavior order.
2. **CQRS with a dual data strategy** — EF/Domain for writes and invariants, Dapper for reads.
3. **snake_case at the database boundary** — configurations applied first, naming rewrite second.
4. **Result pattern over exceptions** for business flow.
5. **Outbox for reliable side effects** — e.g. `InvoicePosted` → ERPNext sync; the catalog job also back-fills and
   retries anything that failed.
6. **ERPNext behind one interface** — `IErpNextClient`; disabled = `NullErpNextClient` (sync rows become `SKIPPED`).
   Customer name = party display name; item code = sku code; docs are submitted so the ledger posts.
7. **Server-side everything that scales** — paging, search, totals and KPIs are computed by the API, never from a
   sample page in the browser.
8. **AI proposes, humans approve** — read-only tools, suggestions stored as `PENDING`, execution through
   maker-checker; prompt logs are immutable.
9. **Bilingual & RTL-first** — entities carry `name_en` / `name_ar`; Arabic is the end-user language.
10. **Idempotent, auditable writes** — idempotency keys + Audit.NET.
11. **Observability & honesty** — metrics/health are part of the composition root; a job that does nothing must not
    report success.

---

## 5. PROJECT COMPLETION PLAN & ROADMAP

> **Single source of truth for progress.** Update statuses here in the same commit that completes an item.
> The detailed gap matrix, effort estimates and rationale live in `docs/FEATURE_GAP_AND_ROADMAP.md`.

### 5.1 Technical debt register

- [x] **D0 — RBAC seeding**: roles and permission claims are seeded from `RoleCodes.All` via `RolePermissionMap`.
- [x] **D0 — Duplicate marker interfaces**, **response envelope**, **required extensions**, **working-tree noise**: fixed.
- [x] **Prod security basics**: CORS from `AllowedOrigins` (fails closed outside Development); Hangfire dashboard
      requires an authenticated `SYSTEM_ADMIN`; no committed default admin password; JWT/DB secrets from env.
- [x] **Approvals actually execute** (replay) and Hangfire runs the `governance` queue.
- [ ] **D1 — Two permission spellings** (`users.read` dot form vs `invoices:post` colon form): 9 vs 85 codes; pick
      colon as canonical and migrate the 9.
- [ ] **D2 — 13 `null!` in Domain**; `TreatWarningsAsErrors` missing on Domain/Contracts; `Humanizer` referenced from
      Domain (breaks "Domain has no dependencies").
- [ ] **D3 — i18n**: externalise the hardcoded Arabic into `ar.json`/`en.json` and use `useTranslation`.
- [ ] **D4 — Dead dependencies** (list in §2) and stale `README.md`.
- [ ] **D5 — Secrets exposed in past chats** (DB password, JWT keys, ERPNext `Administrator` = `admin`): rotate.
- [x] **D6 — DONE (2026-09-23):** Scalar/OpenAPI are mapped only in Development.
- [ ] **D7 — Unpaged list screens** (receiving, transfers, cycle counts, adjustments, issue orders, FX rates).
- [ ] **D8 — CI does not exercise SQL.** Make the integration tests migrate + seed a Testcontainers database and hit the
      real endpoints (login → dashboard/items/approvals flows) so a green run means something. Until then, local verification
      on real Postgres is mandatory (see ENGINEERING_PLAYBOOK §2.1).
- [x] **D10 — Cost of goods (DONE: COGS Journal Entry per invoice; cost taken from batch/SKU).** Was: not booked in ERPNext. Sales Invoices are sent with `update_stock = 0` (we own inventory), so ERPNext
      records income and receivables but no COGS — its P&L overstates profit. Decide: post a COGS/inventory Journal Entry per
      invoice from our cost prices, or move stock valuation into ERPNext. Also `invoice_lines.cost_price_*` is 0 at creation.
- [x] **D11 — DONE (InvoiceStockMover): voiding returns stock.** Was: voiding did not return stock (it creates a credit note and flips the status only).
- [x] **D12 — DONE: returns add stock and sync as is_return.** Was: RETURN invoices decrement stock when posted and are not synced to ERPNext (needs `is_return` + `return_against`).
- [x] **D13 — DONE: all five inventory screens and Payments use pickers (LocationSelect, WmsLinesEditor, EntityPicker, ReasonCodeSelect).** Was: raw-ID inputs remained on Receiving, Transfers, Issue orders, Stock adjustments and Cycle counts (warehouse/item/vendor
      ids typed by hand). Use `LocationSelect`, `ItemPickerModal mode="warehouse"` and `EntityPicker`. Their list endpoints for
      issue orders/transfers also read `dynamic` rows and need the same typed-record fix.
- [x] **D14 — DONE: rename propagates via rename_doc.** Was: renaming a customer is not propagated to ERPNext (it identifies customers by name).
- [x] **D15 — DONE (2026-09-20): stock and warehouse balances are kept in step.** Was: **Two stock models.** WMS documents (receiving, putaway, transfer orders, adjustments) change `inventory_balances`; invoices, direct receive/adjust/transfer and (now) issue orders change `inventory_stock`. A periodic job copies stock → balances only, so goods received through a WMS document are **not** sellable until D9 is done. The item-movement ledger is complete for both sides.
- [x] **D16 — DONE (2026-09-23): every screen is on MUI.** The last old-style screens (login, invoice workspace and detail, item card, payments, receiving, transfers, issue orders, cycle counts, stock adjustments) and the pickers/line editors were rebuilt on the shared kit; `theme.css` and the old common components are deleted; the few global rules live in the theme (`MuiCssBaseline`).
- [ ] **D17 — Dev key in git history.** `appsettings.Development.json` (a dev JWT private key and the dev DB password) was committed in the first commit of a public repository; it is untracked but remains in history. Decide: purge history (force-push) or just treat as public. The VPS uses its own keys.
- [ ] **D9 — WMS → stock reverse sync** and retiring duplicated sku fields (inventory unification steps 4–5).

### 5.2 Phases (in the agreed order)

#### PHASE 0 — Correctness fixes  · `Status: Completed (verified locally on real Postgres; pending deploy)`
- [x] Server-side dashboard aggregation (`GET /api/v1/dashboard/summary`); `Dashboard.tsx` and `KpiDashboard.tsx` rewired; no
      in-browser totals remain.
- [x] `AccountingCheckJob` implemented for real (overdue invoices, customers over credit limit, unallocated receipts, ERPNext
      sync failures / unsynced invoices); it records the actual findings, or FAILED with the error.
- [x] AI chat returns an explicit `Ai.ProviderNotConfigured` error instead of echoing the user's text.
- [x] **Found by local verification and fixed** (all were shipped, CI-green and broken): the idempotency layer returned 500
      after a successful write (`response_code varchar(100)`, migration 11); the `ItemInterchange` insert was invalid SQL; every
      maker-checker submission failed (NOT NULL columns not populated); the approvals list threw on NULL `entity_id`
      (migration 12); approving threw `DbUpdateConcurrencyException` (`ValueGeneratedNever`); `Result`-returning commands turned
      validation/permission/approval outcomes into 500 (`ResultFactory`); the JWT `sub` claim was remapped so **every request ran
      as `Guid.Empty`** (`MapInboundClaims=false`); the requester could approve their own request
      (`Governance:AllowSelfApproval`, default off).

#### PHASE 1 — Accounting core  · `Status: Not Started`
- [x] Sync payments to ERPNext as Payment Entry (settles the Sales Invoices it was allocated to) and cancel on reversal — backend done and verified against a mock ERPNext.
- [x] Payments **screen** (create, partial/multiple, allocate, reverse).
- [x] Chart of accounts and account mapping shown from ERPNext; ERPNext document browser (read-only).
- [x] **ERPNext consistency check (2026-09-23):** local vs ERPNext per document type with every difference listed and linked; read-only cost centres, modes of payment, tax templates, fiscal years and exchange rates; delivery fee now sent to ERPNext.
- [ ] Bank/cash accounts (ERPNext accounts) and per-account statements.
- [x] Purchase invoices; discount on the whole sales/purchase invoice (percentage or amount, sent to ERPNext as Additional Discount) — 2026-09-23.
- [ ] Purchase returns; quick-add supplier; bulk pay/receive; delivery fee to ERPNext.
- [ ] Financial reports read from ERPNext: trial balance, P&L, balance sheet, AR/AP aging, general ledger.
- [ ] Taxes (ERPNext tax templates) and currency handling on top of `fx_rates` (company currency USD).
- [ ] Remove `monthly_pl_summary` / `RefreshMonthlyPlJob` once the ERPNext-backed reports replace them.

#### PHASE W — Warehouse, documents and exports (2026-09-20)  · `Status: Mostly done`
- [x] Warehouse control (locations CRUD + overview) and the item-movement ledger, fed by every stock-changing path.
- [x] Issue orders really take stock out when issued (before: status change only).
- [x] View / print / PDF / Excel / CSV for transfers, issue orders, cycle counts, adjustments, receiving, receipts, statements, movements; exports on the main lists.
- [x] Real invoice PDF (it used to be a plain-text file served as PDF).
- [x] Item import from Excel/CSV with template and dry run.
- [x] One "الحسابات" menu entry (all accounts + customers tabs; `party` stays in code), combined statement only for customer+vendor accounts, customer statement rebuilt.
- [x] ERPNext customer/supplier sync is now update-in-place (it used to create "X - N" duplicates on every run).
- [x] Unify the two stock models (D15): putaway, transfers and adjustments move sellable stock in the same transaction; duplicate "no batch" balance rows merged.
- [x] Purchase invoices + supplier payments + ERPNext sync (Purchase Invoice, Payment Entry Pay, cancels); posting receives the goods and sets a weighted-average cost.
- [x] Stock adjustments are booked in ERPNext (Dr COGS / Cr Inventory at cost).
- [x] **Accounting section (2026-09-20):** chart of accounts with groups and ledgers (+ import/export), dynamic entry types (receipt, payment, contra, journal, opening, debit/credit note and any the business adds), manual entries with tags, ledger reconciliation and its statement, trial balance, balance sheet, profit & loss, ledger statement, receivables/payables ageing, PDF/Excel/CSV on every report. Permissions `accounting:*` (ACCOUNTANT holds all four, AUDITOR reads).
- [x] **Fixes and reviews (2026-09-21):** ledger statement is paged exactly (aggregates, offset totals, stable order); manual entries need a second approval (SYSTEM_ADMIN exempt); period locks take effect at once and can be re-locked/unlocked; users screen and role-change guards; dev settings file untracked (repository is public), fail-closed connection strings, lockfile + CI frontend job.
- [x] **Menu restructured:** related screens are one menu entry with section tabs (sales documents, stock, warehouse operations, users and roles); the raw ERPNext document browser and the duplicate ERPNext-documents screen were removed.
- [x] Purchasing roles (ACCOUNTANT, PURCHASER), supplier statement, cost restored when a bill is voided, period locks per module (SALES / PURCHASES / PAYMENTS).
- [x] Credit limit kept in USD only (lira shown from the latest saved rate); "العميل" is "الزبون" in every Arabic label.
- [x] **Per-user warehouses (2026-09-23):** admin assigns warehouses and manager status; transfers between warehouses wait for the managers of the warehouses involved (SYSTEM_ADMIN exempt). Warehouse lists, documents and actions are kept to the user's warehouses (`inventory:all_warehouses` sees all).
- [x] All screens and the shell on MUI (D16, 2026-09-23).
- [ ] export buttons on customers, inventory, approvals, audit.
- [ ] Barcode scanner UI (add a scanner library when it is built) and purchase-side statements.

#### PHASE 2 — Sales experience  · `Status: In Progress`
- [x] **Sales representatives (2026-09-23):** rep list and page (sales, returns, collections, receivables, commission, monthly target, 12-month trend), customer hand-over, rep on invoices and customers, ERPNext Sales Person + sales team.
- [ ] POS screen: barcode/name/code search, fast lines, hold (= `DRAFT`) and resume.
- [ ] Public invoice link with token: view, PDF, (later) pay. `IPaymentGateway` abstraction + webhook +
      public payment page shell for **Sham Cash** (integration is added when the owner provides the API).
- [ ] Notification channels: e-mail (SMTP), pluggable SMS channel; payment reminders; message log. (WhatsApp inbound assistant done 2026-09-23; the same gateway can later send reminders.)
- [ ] Invoice history: financial operations, e-mails sent, read/paid state.

#### PHASE 3 — CRM  · `Status: Not Started`
- [ ] Rich customer profile (contacts/addresses/notes already have tables; add API + UI; photo, social links).
- [ ] Activity timeline (invoices, payments, audit + manual calls), generic **tags**, **custom fields**,
      direct e-mail with correspondence log, accounting summary per customer.

#### PHASE 4 — Reports centre  · `Status: In Progress`
- [x] **Business KPIs on the dashboard (2026-09-23):** period / warehouse / customer / rep / category filters and a currency switch; sales, profit (gross and ledger net), purchases, ageing and DSO, cash, stock value and slow movers, top customers / items / reps, overdue invoices, 12-month trend.
- [ ] Sales / purchases / returns, profit by item and by invoice, slow movers, item movement, payments and expenses,
      salesperson points (rep figures exist on the reps page); date + tag filters; Excel export everywhere (ClosedXML is in place).

#### PHASE 5 — Security & administration  · `Status: Not Started`
- [ ] Role-permission editor (the users editor is done, 2026-09-21), user ↔ warehouse scoping (done 2026-09-23), one-click database backup (scheduled
      `pg_dump`, rotation, admin-only download), catalog categories, batches, warranty and reason-code screens.

#### PHASE 6 — Real AI  · `Status: Not Started`
- [x] Provider over an OpenAI-compatible API (Groq default; `Ai:BaseUrl/Model/ApiKey`, key only on the server) — `GroqIntentExtractor`, used by the WhatsApp assistant (2026-09-23).
- [ ] Read-only tools (item search, stock, customer balance, recent invoices, period sales), permission-checked.
- [ ] Real scheduled tasks: accounting check (SQL rules + LLM narration), low-stock summary with reorder proposals.
- [ ] Suggestions inbox wired to maker-checker; sidebar advisor; AI admin (flags, tasks, prompt logs, runs, KB).
- [ ] Optional embeddings + pgvector for company knowledge.

#### PHASE 7 — Extras  · `Status: Not Started`
- [ ] Sales commissions; smart stock audit (variance → proposed adjustment → approval); configurable invoice
      template, then a visual designer.

#### PHASE H — Hardening & release  · `Status: In Progress`
- [x] CI green; VPS deployment scripted; nginx + TLS (self-signed); firewall rules; health checks.
- [ ] Domain + real TLS; SSH key-based access then disable password login; fail2ban; secret rotation (D5);
      server upgrade (ERPNext + app + AI need more than 2 GB); backups (Phase 5); set GitHub deploy secrets.

### 5.3 Historical phases (completed earlier)
- **Phase 1 Governance, Phase 2/3/3.5 Operational core, Phase A stabilise, Phase B WMS + sales frontend** — done.
- **Stabilisation & deployment pass (Sept 2026)**: CI repair, VPS deployment, approval replay, schema/mapping
  fixes, inventory unification (step 1–3), ERPNext install + client, sales-invoice sync, paged lists, item card,
  accounting-sync screen, nginx `/hangfire`, Hangfire queue fix.

---

### 5.4 Decisions from the owner (2026-09-22)
1. **Company currency — display layer only.** No data or ERPNext-currency change. Every amount keeps its USD value in `*_usd` columns and in ERPNext; the UI shows the lira equivalent (from `useFxMid`) in small type next to the USD figure everywhere an amount appears. **Done 2026-09-23:** `components/ui/Money.tsx` (lira first by default, top-bar switch remembered per browser, recorded lira used when the document has it); old-style screens adopt it as they move to MUI.
2. **Warehouse-manager approval scope.** A warehouse manager may approve a transfer **into or out of any warehouse they manage** — not only the two warehouses on that specific transfer. Manager status on a warehouse is granted only by an administrator (via the user's warehouse assignment), never self-service. SYSTEM_ADMIN needs no approval. A manager's authority is scoped to transfers; it does not extend to other governed request types. **Done 2026-09-23** (migration 20, `IWarehouseAccess` + `TransferApprovalPolicy`, wired in `MakerCheckerBehavior`/`GovernanceService`).

## 6. AI layer — truthful status

- **Today:** `AiService.ChatAsync` returns the user's own text prefixed with "تم استلام طلبك"; `ai_suggestions` is
  never written; `AccountingCheckJob` inserts a hardcoded completed row; feature flags reference local `llama`
  models that are not installed; embeddings are never generated. **No screen calls `/api/v1/ai/*`.**
- **Kept (good foundations):** feature flags with roles, `AllowWritesToCoreData` policy block, immutable prompt log,
  suggestion/feedback/session tables, scheduled-task table.
- **Decision:** hosted OpenAI-compatible provider — Groq (free tier, rate-limited) first, DeepSeek as the cheap
  alternative; model name and base URL are configuration. Free tiers change — re-check limits and data terms before
  relying on them. Data sent to the provider is minimised and never includes secrets or credentials.
- **WhatsApp assistant (2026-09-23, live):** read-only questions from linked numbers (balance, stock, invoice, sales, overdue); the model gets the message text only and picks a tool; matching and answers are local; see SETUP_HARDENING §3.4b.
- **Plan:** Phase 6 above.
