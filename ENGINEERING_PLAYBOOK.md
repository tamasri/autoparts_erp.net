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
- **CI does not run your SQL [convention — read this].** Integration tests use the `Testing` environment (no migrations,
  no seeding) and only assert auth/health, so a green run says nothing about a new query, migration or job. Before pushing a
  data-layer change: start the dev stack (`docker compose -f docker-compose.dev.yml up -d postgres redis`), run the API in
  Development, log in as the seeded admin and call every endpoint you added or changed — including the write paths, the
  governed (maker-checker) paths and an empty-parameter call. Extending the integration tests to seed and hit real endpoints is
  the durable fix (backlog D8).
- **No flaky tests.** A test may not depend on timing of an internal exporter or on test order. (The metrics
  endpoint test failed intermittently for that reason and now asserts only what is deterministic.)
- **Never commit secrets [convention + .gitignore]:** `appsettings.Development.json` (it was tracked from the first commit until 2026-09-21 — create yours with `scripts/init-dev-settings.ps1`), `*password*.txt`,
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
- **Result pattern:** business outcomes return `Result`/`Result<T>` with `Error(code, message)`. Pipeline behaviors
  short-circuit through `ResultFactory`, which supports exactly those two shapes — never return another type from a command.
- **Identity:** read the caller through `ICurrentUser` (the `sub` claim; `MapInboundClaims=false`). Never trust a `Guid.Empty`
  user id: all-zero `created_by`/`requested_by` values mean the identity plumbing is broken.
- **EF entities with domain-generated Guid ids** need `ValueGeneratedNever()` in their configuration.
- **Maker-checker requests** must populate every NOT NULL column of `approval_requests` (both column generations), and the
  requester cannot review their own request unless `Governance:AllowSelfApproval` is true.
- **Reads (Dapper):** snake_case SQL, **always alias columns** to the DTO property, and make the CLR type match the
  column exactly (`DateOnly` for `date`, `DateTimeOffset` for `timestamptz`; handlers are in
  `DapperTypeHandlers`). A positional-record constructor that cannot be matched fails silently at runtime as a 500.
  Prefer `Dapper` handlers to return `PagedResponse<T>` for lists.
- **Writes:** Domain factories + EF, or parameterised SQL for set-based operations. No string-concatenated SQL.
- **NULL semantics:** Postgres UNIQUE treats NULLs as distinct, so `ON CONFLICT` on a nullable column never fires —
  use update-then-insert or a partial index.
- **Migrations:** raw SQL in `Persistence/Migrations`, next sequential id, additive, never edit an applied one;
  test on an empty database (the seeder and demo data run on boot).
- **Optional query parameters must be nullable** (`int? page`, `bool? includeInactive`) with defaults applied in the handler call;
  a required non-nullable minimal-API parameter that is missing surfaces as a 500.
- **Idempotency & response storage:** columns that persist serialized responses are `text`. A `varchar(100)` here made
  successful writes return 500 after commit.
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
- **Syncers make themselves self-sufficient:** before a document is sent, the customer and every item it references are upserted
  (`SalesInvoiceErpNextSyncer.EnsureMasterDataAsync`), so a brand-new customer no longer fails its first invoice.
- **Lifecycle mapping:** post → Sales Invoice (submitted); allocate a receipt → Payment Entry referencing the invoices; void an
  invoice → cancel the Sales Invoice; reverse a receipt → cancel the Payment Entry (cancel the payment before its invoice).
  `erpnext_sync_log.status` = SYNCED / FAILED / SKIPPED / CANCELLED; a scheduled sweep retries FAILED items.
- **Verify without a real ERPNext:** run a tiny mock of `/api/resource/*`, `Company` and `frappe.client.cancel` on a local port, start
  the API with `Erpnext__Enabled=true Erpnext__BaseUrl=http://localhost:<port>`, and drive the flow; check the mock's request log
  and `erpnext_sync_log`. (Frappe's own validation still only runs on the real server — read its error text in the sync log.)

### 2.3a Before you build anything (owner rule, 2026-09-20)
1. **Check it is not already built and abandoned.** Search the modules, features, tables, DTOs and unused dependencies first (`grep` the module list, `\d table` in Postgres, `package.json`). This project has several half-finished pieces; extend them instead of adding a second one.
2. **Use a ready-made library** for anything generic (PDF/Excel/CSV, tree views, tables, file parsing) — QuestPDF, ClosedXML, CsvHelper, MUI X are in the tree. Write only the glue.
3. State in the commit or the session log what existed and what was reused.

### 2.3b Stock and movements
- **Every code path that changes stock must write the item ledger** (`inventory_movements`): use `InventoryMovementWriter` (SKU-keyed paths) or insert with the WMS item id (item-keyed paths). A stock change with no ledger row is a bug.
- `performed_by` has a foreign key to users: never pass `Guid.Empty`.
- Two stock tables exist: `inventory_stock` (by SKU, what invoices sell) and `inventory_balances` (by item and status, what warehouse documents work on). **Any code that changes the AVAILABLE quantity of an item at a location must also call `StockLevelWriter.ApplyAvailableAsync` in the same transaction** (putaway, transfers, adjustments, purchase bills do). A change that touches only one table makes goods unsellable or sellable by mistake.
- `inventory_balances` is unique with `NULLS NOT DISTINCT` (migration 16), so `ON CONFLICT (item_id, location_id, batch_id, status)` works for "no batch" rows. Never insert such rows without it.

### 2.3c Printing and exporting
- Screens never build PDF/Excel themselves: describe the document as an `ExportDocument` and use `DocumentViewButton` / `DocumentDialog` / `ExportMenu` (`lib/exportClient.ts`). Document builders live in `lib/wmsDocuments.ts`.
- Numeric columns must be listed in `numericColumns`; text beginning with = + - @ is neutralised by the renderer (spreadsheet formula injection).
- Fonts are embedded resources (`Infrastructure/Exports/Fonts`); never rely on system fonts in the container.

### 2.3d ERPNext master data
- ERPNext does **not** refuse a second Customer/Supplier with the same name; it creates "X - 1". Always look a party up by name first (`UpsertPartyAsync`) and update it. Items are unique by `item_code`, so create-then-update-on-duplicate is fine for them.
- Anything that changes the value of stock must reach the ledger: sales (COGS), purchases (Purchase Invoice), adjustments (Journal Entry). Add the matching outbox event when you add a new stock-changing flow.

### 2.3e Accounting (ledger stays in ERPNext)
- **One ledger.** Nothing is posted or summed twice: reports read ERPNext's GL Entry totals (`GetGlBalancesAsync`, a grouped query) and shape them in `FinancialReports` (pure, unit-tested). Never add a local copy of balances.
- **Manual entries** live in `journal_entries` / `journal_entry_lines` (draft → POSTED → VOID). Posting writes an outbox event; `JournalEntryErpNextSyncer` books a submitted Journal Entry and records it in `erpnext_sync_log` (entity `JournalEntry`); void cancels it. A posted entry is never edited. The entry type's **kind** decides the ERPNext voucher type (`EntryKinds.ErpNextVoucherType`); users may add types (own name and number prefix) but not new kinds.
- **Accounts are referenced by their ERPNext name** ("Cash - AB"). A receivable/payable account needs a customer/supplier on the line; every other account refuses one (`EntryLineRules`). Renaming an account changes its full name in ERPNext; posted entries follow, drafts must be re-picked.
- **Reconciliation** stores only which GL lines were cleared (`ledger_reconciliation_items`, unique per line). Amounts are always re-read from ERPNext when completing; the statement balance must equal (previously cleared + ticked). Only the latest reconciliation of an account can be undone, and an entry with a reconciled line cannot be voided.
- **Tags** attach to a manual entry (`JOURNAL_ENTRY`, id) or any ERPNext voucher (`ERPNEXT`, `"<voucher type>|<number>"`); `TagResolver` merges both views once an entry has been booked.
- **Period locks:** module `ACCOUNTING` (create/edit through the pipeline, post and void through `IPeriodLockService`).
- **Sections in the menu:** put related screens in one `NavItem` with `tabs` (`components/layout/navigation.ts`); do not add a menu entry per screen. Tabs inside a screen use `RoutedTabs` (`?tab=`).
- **A paged ledger read is exact [convention]:** totals and counts come from ERPNext aggregate queries (`GetGlSummaryAsync`), the balance a page starts from from the totals of the lines skipped (`GetGlOffsetSummaryAsync`), the order is always `posting_date, creation, name`, and the arithmetic lives in `LedgerPaging` (tested). Never re-read "a page of history" to derive a balance.
- **Period locks [enforced by tests]:** the check reads a short-lived cache; every lock/unlock must call `IPeriodLockService.InvalidateCacheAsync`. The lock/unlock commands themselves are NOT `IPeriodSensitiveRequest`. A command that is period-sensitive by request date implements `IPeriodSensitiveRequest` with that date; one that learns the date from the database checks `IPeriodLockService` in its handler — never `OperationDate = now`.
- **Governed commands never carry secrets [convention]:** an approval stores the request as JSON in `approval_requests`. Passwords and keys go through ungoverned, audited commands (e.g. admin password reset).
- **Approval exemptions live in one place [convention]:** SYSTEM_ADMIN's exemption is in `MakerCheckerBehavior`; approver resolution (who may approve what) must be one service used by `GovernanceService`, not per handler.
- **Transfers between warehouses [enforced by tests]:** a command that moves stock between warehouses implements `IWarehouseTransferRequest` and is resolved in `WarehouseAccess.ResolveTransferAsync`; the decision is `TransferApprovalPolicy` (pure). Do not check warehouse managers anywhere else. A warehouse is a top-level location; moves inside one warehouse are not transfers.

### 2.3g Sales representatives
- A rep is a **user** with a row in `sales_reps`; `invoices.sales_rep_id` and `customers.assigned_sales_rep` reference it (FK). Check with `SalesRepGuard.IsActiveAsync` before writing either.
- **Rep figures come from one query** (`SalesRepReader.ListAsync`) and the arithmetic from `SalesRepMetrics`; do not compute rep totals elsewhere. Visibility is `SalesRepScope`: `sales_reps:read` sees all, a rep sees themselves.
- Customer updates: a missing `assignedSalesRep` keeps the rep, `Guid.Empty` removes it (same convention as `Customer.AssignSalesRep`). Never write NULL because a form left a field out.
- ERPNext: the rep is a Sales Person named by the user's full name; invoices carry `sales_team` at 100 %.

### 2.3f Invoice totals and discounts
- **One formula.** A sales invoice's subtotal/discount/total are written only by `recalc_invoice_totals(id)` (via `InvoiceTotals`); any code that changes lines, the fee or the discount calls it. Never update `total_*` by hand.
- **Invoice discount = percentage or amount** (`DocumentDiscount.Resolve`), stored positive; a RETURN's subtotal and total are negative. Line discounts stay on the lines. Only a DRAFT's discount can change.
- **ERPNext:** send it as `apply_discount_on = "Net Total"` + `discount_amount` (`WithInvoiceDiscount`), negative on a return. For purchases the discount also lowers the item cost (factor total/subtotal) on post and on void — keep both queries identical.
- **Numeric division in SQL:** `numeric / numeric` can return more than 28 significant digits and Dapper then throws "Numeric value does not fit in a System.Decimal". `round(..., 6)` any computed ratio you read into C#.
- Verify accounting changes with the stateful mock ledger approach: a fake ERPNext that really posts Journal Entries to an in-memory GL and answers grouped `GL Entry` queries, then check TB/BS/P&L totals by hand.

### 2.4 AI rules
- All AI features go through one provider abstraction over an **OpenAI-compatible** endpoint (Groq / DeepSeek);
  base URL, model and key are configuration, the key never leaves the server.
- **Read-only tools only**, each checking the caller's permissions. **AI never writes core data**
  (`AllowWritesToCoreData` stays blocked). A proposed action is a `PENDING` row in `ai_suggestions` and executes only
  via the maker-checker flow.
- Every call is written to the immutable prompt log; feature flags gate every feature; minimise and redact what is
  sent (no secrets, no credentials, no more customer data than the question needs).
- Never present a stub as a working feature.

### 2.5 Frontend guidelines (updated 2026-09-19 — owner-approved full migration)

> **Policy change:** `ENGINEERING_PLAYBOOK.md §2.5` (old) prohibited MUI, TanStack Query, react-hook-form and Zod. The owner overrode this on 2026-09-19 (see `PROJECT_VISION.md §2a`). The rules below replace the old §2.5 in full.

> **Never use `require()` in `frontend/src`.** It works in the Vite dev server but not in the production bundle, where it blanks the whole app (`require is not defined`) — this happened on 2026-09-19 with `rtlCache.ts`. Use ES `import`s (add a `.d.ts` for untyped packages). After any change to `main.tsx`, the theme or the emotion cache, run `npm run build && npx vite preview` and open the page: a green `tsc`/`build` does not prove the bundle runs.

- **Component model:** React 19 function components + hooks. **No class components.**
- **Data fetching:** server state is loaded with `useLoad` (one request that reloads when its filters change) or `usePagedList` (server paging + debounced search) — both keep only the newest answer. TanStack Query v5 is used in `features/customers` only; use it for a new feature only when caching across screens is needed. Hooks must not call the API in `useEffect` more than once per screen concern.
- **API layer:** single `lib/apiClient.ts` instance (wraps `api/client.ts`) that unwraps the `ApiResponse` envelope (`res.data?.data ?? res.data`) once and throws a typed `ApiError`. Typed endpoint modules in `api/endpoints/*` stay; they return the raw axios response — `apiClient.ts` normalises it for TanStack Query.
- **Forms:** **react-hook-form v7** + **Zod v3** (`zodResolver`). One `schema.ts` per entity. MUI `Controller` or a thin `RHFTextField` wrapper bridges RHF to MUI inputs. `window.prompt` is banned — use MUI `Dialog` with a Zod-validated form.
- **Styling:** **MUI v6 `createTheme`** with `direction: 'rtl'` as the single source of truth. Vex CSS tokens (`#5c54ff`, `12px` radii, card shadows, font stack) are mapped 1-to-1 into the theme. Per-component `style={{ direction: 'rtl' }}` inline overrides are deleted as each screen is migrated. `theme.css` sections are removed only when their last consumer is gone. **No new colour literals** — use theme palette or CSS variables from `theme.css` during migration.
- **RTL:** `@emotion/cache` + `stylis-plugin-rtl` wired once in `main.tsx`. All MUI components flip automatically. `document.dir` is set by `i18n.changeLanguage` callback.
- **Lists:** `DataTable` with its `paging` prop, fed by `usePagedList` (1-based page there, 0-based in `DataTable`); totals come from the server. `DataGrid` is not used.
- **State:** Zustand for auth only. Everything else: server state through the hooks above, or local `useState` for UI toggles. `useCan(permission)` hides buttons the server would refuse (the server still decides).
- **i18n:** `useTranslation()` from `react-i18next` on every screen. New translation keys added to both `ar.json` and `en.json` with a `// TODO: translate` comment in `en.json` if the English translation is unverified. Arabic is the primary language — the app is always shipped in Arabic; the EN switcher is additive.
- **Notifications:** `toast` / `extractApiError` from `lib/toast`. POSTs guarded by `WithIdempotency()` must send an `Idempotency-Key` header (unchanged).
- **Wording:** in Arabic UI text a customer is **الزبون / الزبائن** (never عميل). Money that has a company currency is stored and edited in USD; show the lira value from `useFxMid()` (`inLira`) instead of storing a second figure.
- **UI kit for new screens:** `DataTable`, `ReasonDialog` (replaces window.prompt), `useConfirm` (replaces window.confirm), `ImportDialog` (every Excel/CSV import), `RoutedTabs`, `useLoad` (one load with filters), `useCan` (hide buttons the server would refuse), `StatusChip`, `KpiTile`, `components/ui/PageHeader`, `DocumentDialog`, `DocumentViewButton`, `ExportMenu`, `components/accounts/StatementTable`; tree views via `@mui/x-tree-view`. A route-level `ErrorBoundary` is keyed by the URL so one crash does not stick to the next screen.
- **Pickers:** `LocationSelect`, `EntityPicker`, `ItemPickerModal`, `FxRateField` remain as-is; they will be progressively wrapped in MUI `Autocomplete` in Phase 5+.
- **Code splitting:** `React.lazy` + `Suspense` on every route. Route-level `ErrorBoundary` from `components/common/ErrorBoundary.tsx`.
- **Agent rule:** `frontend/src/features/<entity>/` is the canonical location for: `schema.ts` (Zod), `queries.ts` (TanStack), `<Entity>Dialog.tsx` (RHF form), keeping them co-located and importable by both the list page and detail page.

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

---

## Execution Reports

> Daily/phase reports for the Frontend UI/UX modernisation project (started 2026-09-19).
> Format: one `### YYYY-MM-DD — Phase X` entry per phase; table rows added as tasks complete.
> Machine-readable JSON status block appended at the end of each phase entry.

---

### 2026-09-19 — Draft Execution Plan

| Task | Status | Files Modified | Commit Message | Notes |
|---|---|---|---|---|
| Read audit report (7,686 LOC) | Done | — | — | Audit provided by Superagent inline |
| Confirm owner policy override | Done | `PROJECT_VISION.md`, `ENGINEERING_PLAYBOOK.md`, `AGENT_ONBOARDING.md` | `docs: record owner-approved frontend stack adoption (2026-09-19)` | Q1/Q3 explicitly approved |
| Create task.md | Done | `.gemini/brain/…/task.md` | — | Agent-internal |
| Phase 0: rtlCache.ts | In Progress | `frontend/src/lib/rtlCache.ts` | `feat(frontend): add emotion RTL cache (phase0)` | — |
| Phase 0: theme.ts | In Progress | `frontend/src/theme/theme.ts` | `feat(frontend): MUI createTheme with Vex tokens (phase0)` | — |
| Phase 0: main.tsx | In Progress | `frontend/src/main.tsx` | `feat(frontend): wire QueryClient + ThemeProvider + RTL (phase0)` | — |

**Dry-run file list (expected changes — Phase 0):**
```
frontend/src/lib/rtlCache.ts       [NEW]
frontend/src/theme/theme.ts        [NEW]
frontend/src/main.tsx              [MODIFY]
```

**Acceptance criteria tracking (Phase 0):**
- [ ] `npm run build` passes with zero TS errors
- [ ] App loads; all existing screens render unchanged
- [ ] No `direction: 'rtl'` inline styles added (to be removed per screen in Phase 1)

```json
{
  "phase": 0,
  "status": "complete",
  "date": "2026-09-19",
  "changed_files": [
    "frontend/src/lib/rtlCache.ts",
    "frontend/src/theme/theme.ts",
    "frontend/src/main.tsx",
    "frontend/src/lib/apiClient.ts",
    "frontend/src/features/customers/schema.ts",
    "frontend/src/features/customers/queries.ts",
    "frontend/src/features/customers/CustomerDialog.tsx",
    "frontend/src/features/customers/DeactivateDialog.tsx",
    "frontend/src/pages/customers/Customers.tsx",
    "frontend/src/components/common/ErrorBoundary.tsx",
    "frontend/src/App.tsx",
    "frontend/src/i18n/ar.json",
    "frontend/src/i18n/en.json",
    "PROJECT_VISION.md",
    "ENGINEERING_PLAYBOOK.md",
    "AGENT_ONBOARDING.md"
  ],
  "tests": {
    "tsc_noEmit": "PASS (exit 0)",
    "vite_build": "running"
  },
  "phases_completed": ["0-providers", "1-theme", "2-tanstack", "3-rhf-zod", "4-i18n-keys", "5-datagrid", "6-lazy-errorboundary"],
  "phases_remaining": ["4-i18n-useTranslation-per-screen", "5-other-screens", "6-skeleton-polish"],
  "blockers": []
}
```

