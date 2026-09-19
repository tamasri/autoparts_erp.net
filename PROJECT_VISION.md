# PROJECT_VISION.md — AutoPartsERP

> **Isolation notice:** This document describes **`autoparts_erp.net`** only — a standalone .NET / PostgreSQL /
> React ecosystem (plus ERPNext as a headless accounting engine). It shares nothing with any prior
> Next.js/NestJS/TypeScript project. *Last verified against the code and the live server: 2026-09-19.*

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
| Writes | EF Core 9 + Npgsql; **raw-SQL migrations** (12) | `Persistence/Migrations` |
| Reads | **Dapper 2** on snake_case tables + `DapperTypeHandlers` (`DateOnly`, `DateTimeOffset`) | |
| Database | **PostgreSQL 16** (`ltree`, `pg_trgm`, `uuid-ossp`; `pgvector` for embeddings) | On the VPS it runs on the host |
| AuthN / AuthZ | ASP.NET Identity (Guid keys) + **JWT RS256**; permission-based (`PermissionCodes`) with seeded role→permission map (`RolePermissionMap`) | |
| Cache | Redis (StackExchange.Redis, distributed cache) | |
| Jobs | **Hangfire** (Postgres storage), queues `default` + `governance`; **Outbox** dispatcher hosted service | |
| Realtime | SignalR hub `/hubs/erp` | one frontend consumer |
| Audit | Audit.NET (EF + PostgreSql sinks) + immutable audit tables | |
| Idempotency | IdempotentAPI + `DistributedIdempotencyService` + `IdempotencyBehavior` | send `Idempotency-Key` on POSTs |
| **Accounting engine** | **ERPNext (Frappe) via REST**, `IErpNextClient` → `ErpNextClient` / `NullErpNextClient`; `erpnext_sync_log` | Live on the VPS, port 8080 |
| Observability | OpenTelemetry (traces + Prometheus metrics at `/metrics`), Serilog, health checks | |
| Docs | OpenAPI + Scalar (mapped unconditionally; not proxied by nginx) | Debt D6 |
| Files | ClosedXML (Excel), QuestPDF (invoice PDF), QRCoder (QR) | |
| Frontend | **React 19, Vite 6, TypeScript 5.7, react-router 7, Zustand 5, axios, sonner, @microsoft/signalr** | |
| Frontend styling | Custom **Vex** design system — `frontend/src/styles/theme.css` (`vex-*`, `btn-*`, `badge*`), RTL | **Not MUI** |
| Frontend data | shared `api/client.ts` + typed `api/endpoints/*`; `usePagedList` hook + `<Pagination>` | **Not TanStack Query** |
| Reverse proxy / TLS | nginx in Docker; self-signed cert on the IP until a domain exists | |
| CI | GitHub Actions: build + unit + integration tests; deploy job gated on secrets | GREEN |
| Tests | UnitTests (31), IntegrationTests (25, need Docker), E2ETests (empty project) | |

**Listed in a manifest but unused (dead weight — remove after the AI/e-mail decisions):**
`@mui/material`, `@mui/x-data-grid`, `@tanstack/react-query`, `@tanstack/react-table`, `react-hook-form`, `zod`,
`@emotion/*`, `stylis*`, `@zxing/*` (frontend); `Microsoft.SemanticKernel`, `Microsoft.Extensions.AI`, `Pgvector`,
`FluentEmail.*`, `ZXing.Net` (backend). `i18next` is initialised but no screen calls it.

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
| Auth | Login/refresh/logout/me | `Login.tsx`, `authStore` | ✅ |
| Dashboard / KPIs | `/kpi/admin/*` definitions only | `Dashboard.tsx`, `KpiDashboard.tsx` | ⚠️ numbers computed in-browser from a page sample |
| Users | CRUD + roles endpoints | list only (paged, searchable) | 🟡 no create/edit forms |
| Roles & permissions | create, grant/revoke | list/partial | 🟡 |
| Approvals (maker-checker) | pending/approve/reject **+ replay executes the request** | `Approvals.tsx` (paged) | ✅ |
| Audit log | list/detail | `AuditLog.tsx` (paged, filters) | ✅ |
| Period locks | lock/unlock/list | `PeriodLocks.tsx` | ✅ |
| Reason codes | create/list | — | 🟡 |
| Customers | full CRUD + statement | `Customers.tsx` (paged), `CustomerDetail.tsx` | ✅ |
| Parties (customer/supplier) | CRUD, types, statements | `Parties.tsx` (paged), `CombinedStatement.tsx` | ✅ (contacts/addresses/notes tables have no API) |
| **Items + item card** | browse/search/get/create/update/aliases/interchanges/stop-ship/stock | `Items.tsx`, `ItemCard.tsx` (5 tabs) | ✅ |
| Catalog (SKU, categories, prices) | full | prices inside item card only | 🟡 no category management |
| Inventory stock | stock/batches/trace/receive/adjust/transfer | `Inventory.tsx` (paged) | ✅ (no batch screens) |
| Receiving / Putaway | full | `Receiving.tsx` | ✅ (unpaged) |
| Transfers | requests/orders/ship/receive | `Transfers.tsx` | ✅ (unpaged) |
| Cycle counts | plan/record/approve variance | `CycleCounts.tsx` | ✅ (unpaged) |
| Stock adjustments | create/post | `StockAdjustments.tsx` | ✅ (unpaged) |
| Issue orders (picking) | full | `IssueOrders.tsx` | ✅ (unpaged) |
| Inventory alerts | list/ack/resolve + low-stock job | `InventoryAlerts.tsx` | ✅ |
| Invoices | create/lines/confirm/post/void/PDF | `Invoices.tsx` (paged), `InvoiceWorkspace.tsx`, `InvoiceDetail.tsx` | ✅ |
| **Payments & allocations** | create, allocate to many invoices, reverse, list | — | 🟡 **no screen; not synced to ERPNext** |
| FX rates | list/latest/create | `FxRates.tsx` | ✅ |
| Warranty | list/claim/process/reject + expiry job | — | 🟡 |
| Reports | P&L, inventory value (+Excel), batch trace, account statement | — | 🟡 |
| Barcodes | scan, generate item/batch codes | — | 🟡 (no scanner UI) |
| **ERPNext accounting sync** | trigger, paged log, summary | `AccountingSync.tsx` | ✅ (items, customers, suppliers, sales invoices) |
| **AI assistant / suggestions / KB** | see §6 | — | ⚠️ **stub** |
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
- [ ] **D6 — Scalar/OpenAPI mapped in every environment**: map only in Development or gate by role.
- [ ] **D7 — Unpaged list screens** (receiving, transfers, cycle counts, adjustments, issue orders, FX rates).
- [ ] **D8 — Integration tests need Docker**; add a CI-independent smoke test for the deployed stack.
- [ ] **D9 — WMS → stock reverse sync** and retiring duplicated sku fields (inventory unification steps 4–5).

### 5.2 Phases (in the agreed order)

#### PHASE 0 — Correctness fixes  · `Status: Not Started`
- [ ] Server-side KPI/dashboard aggregation endpoints (receivables, stock counts, sales by period); rewire
      `Dashboard.tsx` and `KpiDashboard.tsx`; remove in-browser totals.
- [ ] `AccountingCheckJob`: implement for real (SQL rules) or disable — no more fake "completed".
- [ ] Label the current AI chat as unavailable until Phase 6 (do not present an echo as intelligence).

#### PHASE 1 — Accounting core  · `Status: Not Started`
- [ ] Payments screen (create, partial/multiple, allocate, reverse) + sync as ERPNext Payment Entry.
- [ ] Bank/cash accounts (ERPNext accounts) and per-account statements.
- [ ] Purchase invoices, purchase returns, discounts (ERPNext Purchase Invoice); quick-add supplier; bulk pay/receive.
- [ ] Financial reports read from ERPNext: trial balance, P&L, balance sheet, AR/AP aging, general ledger.
- [ ] Taxes (ERPNext tax templates) and currency handling on top of `fx_rates` (company currency USD).
- [ ] Remove `monthly_pl_summary` / `RefreshMonthlyPlJob` once the ERPNext-backed reports replace them.

#### PHASE 2 — Sales experience  · `Status: Not Started`
- [ ] POS screen: barcode/name/code search, fast lines, hold (= `DRAFT`) and resume.
- [ ] Public invoice link with token: view, PDF, (later) pay. `IPaymentGateway` abstraction + webhook +
      public payment page shell for **Sham Cash** (integration is added when the owner provides the API).
- [ ] Notification channels: e-mail (SMTP), pluggable SMS/WhatsApp channel; payment reminders; message log.
- [ ] Invoice history: financial operations, e-mails sent, read/paid state.

#### PHASE 3 — CRM  · `Status: Not Started`
- [ ] Rich customer profile (contacts/addresses/notes already have tables; add API + UI; photo, social links).
- [ ] Activity timeline (invoices, payments, audit + manual calls), generic **tags**, **custom fields**,
      direct e-mail with correspondence log, accounting summary per customer.

#### PHASE 4 — Reports centre  · `Status: Not Started`
- [ ] Sales / purchases / returns, profit by item and by invoice, slow movers, item movement, payments and expenses,
      salesperson points; date + tag filters; Excel export everywhere (ClosedXML is in place).

#### PHASE 5 — Security & administration  · `Status: Not Started`
- [ ] Users and roles editors (forms), user ↔ warehouse scoping, one-click database backup (scheduled
      `pg_dump`, rotation, admin-only download), catalog categories, batches, warranty and reason-code screens.

#### PHASE 6 — Real AI  · `Status: Not Started`
- [ ] Provider abstraction over an OpenAI-compatible API (Groq / DeepSeek), config-driven, key only on the server.
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

## 6. AI layer — truthful status

- **Today:** `AiService.ChatAsync` returns the user's own text prefixed with "تم استلام طلبك"; `ai_suggestions` is
  never written; `AccountingCheckJob` inserts a hardcoded completed row; feature flags reference local `llama`
  models that are not installed; embeddings are never generated. **No screen calls `/api/v1/ai/*`.**
- **Kept (good foundations):** feature flags with roles, `AllowWritesToCoreData` policy block, immutable prompt log,
  suggestion/feedback/session tables, scheduled-task table.
- **Decision:** hosted OpenAI-compatible provider — Groq (free tier, rate-limited) first, DeepSeek as the cheap
  alternative; model name and base URL are configuration. Free tiers change — re-check limits and data terms before
  relying on them. Data sent to the provider is minimised and never includes secrets or credentials.
- **Plan:** Phase 6 above.
