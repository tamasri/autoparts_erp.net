# SETUP_HARDENING.md — AutoPartsERP

> Setup, deployment and hardening for **`autoparts_erp.net`** only. *Last verified 2026-09-19 against the running VPS; local setup and security items updated 2026-09-21.*
> Never paste secrets into chat, tickets or commits. Anything that has been pasted in a chat is considered exposed.

---

## 1. Prerequisites (pinned)

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **9.0.312** or newer | `global.json` → `rollForward: latestMajor` |
| Node.js | 20 LTS or 22 LTS | Vite 6 / React 19 |
| Docker + Compose | current | dev infra and production stack |
| PostgreSQL | 16 | needs `uuid-ossp`, `pg_trgm`, `ltree`; `pgvector` for AI embeddings |
| GitHub CLI (`gh`) | current | used to watch CI (`gh run list`) |

---

## 2. Local development

### Option A — Windows all-in-one
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-local.ps1     # switches: -SkipDocker -SkipBuild -SkipFrontendInstall
.\scripts\stop-local.ps1
```
(`START-FULLSTACK.bat` / `.vbs` are double-click wrappers.) Logs land in `scripts/logs/` (git-ignored).

### Option B — manual
A fresh checkout has no `appsettings.Development.json` (it is untracked, it holds a JWT private key and the DB password). Create it once:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\init-dev-settings.ps1 -DbPassword <password from docker-compose.dev.yml>
```
It copies `appsettings.Development.example.json` and generates a new RSA key pair on your machine; it refuses to overwrite an existing file.
```bash
docker compose -f docker-compose.dev.yml up -d          # Postgres 16, Redis 7, Seq, pgAdmin
dotnet restore AutoPartsERP.sln --configfile NuGet.Config
dotnet run --project src/AutoPartsERP.Api --launch-profile Development     # http://localhost:47000
cd frontend && npm install && npm run dev                                   # http://localhost:47173
```

| Surface | URL |
|---|---|
| API / Scalar / OpenAPI | `http://localhost:47000` · `/scalar/v1` · `/openapi/v1.json` |
| Health / Metrics | `/health`, `/health/live`, `/health/ready` · `/metrics` |
| Hangfire | `/hangfire` (needs a `SYSTEM_ADMIN` **Bearer** token — a plain browser gets 401) |
| SignalR | `ws://localhost:47000/hubs/erp` |
| Frontend | `http://localhost:47173` |
| Postgres / Redis | `localhost:47432` / `localhost:47379` |
| Seq UI / pgAdmin | `:47341` / `:47050` |

**Bootstrap admin.** Created once by `DatabaseSeeder` from `Seed:AdminEmail`, `Seed:AdminUsername`,
`Seed:AdminPassword` (env: `Seed__AdminPassword`, …). There is no built-in password in any environment: when the admin does not exist yet the API
refuses to start without one. `scripts/init-dev-settings.ps1` writes a random `Seed.AdminPassword` into the git-ignored
`appsettings.Development.json` (it is not printed; read it from that file for the first login, then change it). An existing admin is never touched.

**Migrations** auto-apply on startup outside `Testing`. They are raw SQL (`Persistence/Migrations`, ids
`202401010000NN`); to add one, create the next numbered class — see ENGINEERING_PLAYBOOK §2.2.

**Local verification stack.** `docker compose -f docker-compose.dev.yml up -d postgres redis` (only what the API needs; other
containers on the machine are not touched), then run the API in Development and exercise your endpoints with the seeded admin.

**Tests.** `dotnet test tests/AutoPartsERP.UnitTests` runs anywhere. `IntegrationTests` need Docker (Testcontainers);
CI runs them. Frontend: `cd frontend && npx tsc --noEmit && npm run build`.

---

## 3. Production topology (the VPS as deployed)

```
Internet ─▶ nginx (Docker, 80→443 redirect, self-signed TLS on the IP)
              ├─ /              → static SPA (frontend/dist, built on the host)
              ├─ /api/, /hubs/, /hangfire → api container :8080
              └─ /health (127.0.0.1 only), /metrics (Docker subnets only)
api container ──▶ host PostgreSQL 16   (host.docker.internal:5432)
              ──▶ redis container
              ──▶ ERPNext on the host (host.docker.internal:8080)   [frappe_docker pwd.yml, headless]
```
Host: Ubuntu 24.04, 1 vCPU / 2 GB RAM + swap. **Not enough** for ERPNext + app + growth — upgrade before production.
No domain yet, so TLS is self-signed on the IP.

### 3.1 Files
- `docker-compose.vps.yml` — `api`, `redis`, `nginx` (adds `extra_hosts: host.docker.internal:host-gateway`).
- `.env.vps` — **secrets, untracked**, created from `.env.vps.template` by the deploy script.
- `nginx/nginx.conf` — bind-mounted; `/health` is loopback-only; unrouted paths fall back to the SPA.
- `scripts/deploy-vps.sh` — the **only supported way to deploy**.

### 3.2 Deploying
```bash
cd /erp
git pull origin main
bash scripts/deploy-vps.sh        # keeps working even if the file lost its +x bit
```
The script: validates prerequisites → creates/validates `.env.vps` → ensures JWT keys → ensures the seed admin
password (generates one if it is still a placeholder) → checks Postgres reachability from a container → prepares TLS
files → **builds the frontend into `frontend/dist`** → `docker compose down/up --build` → waits for `/health` → checks
the HTTPS edge.

Migrations run when the API starts (20 = user warehouses, 21 = invoice discounts, 22 = sales reps, 23 = warehouse visibility, 2026-09-23); nothing to run by hand.

**Do not** run `docker compose up` by hand:
- it does not rebuild `frontend/dist` (the UI would stay stale);
- without `--env-file .env.vps` the api boots with **blank** configuration and nginx returns 502.

If `git pull` complains about local changes on the server, look at them (`git diff`), then `git stash` (or
`git checkout -- <file>`), pull, and deploy. If the script loses its executable bit, use `bash scripts/...` and
`git config core.fileMode false`.

### 3.3 `.env.vps` keys (values are secrets — never share)
`POSTGRES_HOST/PORT/DB/USER/PASSWORD`, `REDIS_PASSWORD`, `JWT_PRIVATE_KEY`, `JWT_PUBLIC_KEY`, `ALLOWED_ORIGINS`,
`SEED_ADMIN_EMAIL/USERNAME/PASSWORD`, `GOVERNANCE_ALLOW_SELF_APPROVAL` (default `false`; see AGENT_ONBOARDING §4.21), `ERPNEXT_ENABLED`, `ERPNEXT_BASE_URL` (`http://host.docker.internal:8080`),
`ERPNEXT_API_KEY`, `ERPNEXT_API_SECRET`. Enter API keys **directly on the server**; verify without printing them
(`grep -c PASTE_ .env.vps` should be `0`). Planned additions: `AI_*` (provider, base URL, model, key), `SMTP_*`,
`BACKUP_*`.

### 3.4 ERPNext (accounting engine)
- Runs from frappe_docker (`pwd.yml`) on the host, port 8080; the API reaches it through the host port.
- Company currency **USD**. An API key/secret pair belongs to a dedicated user, stored only in `.env.vps`.
- Operators use **Accounting Sync** in our app (trigger, log, summary) — not ERPNext's UI and not `/hangfire`.
- Verify a sync from the server: log in through the API, `POST /api/v1/erpnext/sync`, then read
  `SELECT erpnext_doctype, status, count(*) FROM erpnext_sync_log GROUP BY 1,2;`.
- **Change the default ERPNext `Administrator` password (`admin`)** — pending (H-4).

### 3.4a ERPNext read access (chart of accounts, documents)
The API user needs create/write on **Sales Person** (reps), read on **Cost Center, Mode of Payment, Sales/Purchase Taxes and Charges Template, Fiscal Year, Currency Exchange** (reference lists) and read on Account, Company, GL Entry, Journal Entry, Payment Entry, Sales/Purchase Invoice, Customer, Supplier and Item. The accounting screens also **write**: create/write on **Account** (chart maintenance; renaming calls `erpnext.accounts.doctype.account.account.update_account_number`), and create/submit/cancel on **Journal Entry**. Reports use grouped `GL Entry` queries (`group_by`, `sum(debit)`), so the user must be allowed to read GL Entry with aggregates. If an accounting screen shows an error, read the message: it is ERPNext's own text.

### 3.4b WhatsApp assistant
Read-only questions (customer balance, item stock, invoice status, sales, overdue invoices) from phone numbers linked to ERP users,
answered with that user's permissions. Nothing is created or changed from WhatsApp.
1. **Use a dedicated WhatsApp number** (a SIM for the business, WhatsApp Business app on a phone you keep). The gateway uses
   Baileys, an **unofficial** WhatsApp Web client: it is free and runs on this server, but WhatsApp may restrict or ban a number
   that it considers automated. Do not use the owner's personal number. (The official alternative is the WhatsApp Cloud API —
   needs a Meta business account and a public HTTPS webhook; the gateway is the only piece to replace.)
2. **Groq key (optional but recommended):** create one at console.groq.com and put it in `.env.vps` on the server as
   `AI_API_KEY=` — never in chat, e-mail or git. Only the message text is sent to Groq; no ERP data. Without a key the assistant
   still works with keyword rules (fewer phrasings understood).
3. In `.env.vps`: `WHATSAPP_ENABLED=true`, then deploy. `ASSISTANT_GATEWAY_SECRET` is generated automatically.
4. **Pair:** Settings → **مساعد واتساب** shows a QR. On the assistant's phone: WhatsApp → Linked devices → Link a device → scan.
   The session is kept in the `whatsapp-auth` volume (survives deploys). If the phone unlinks it, a new QR appears.
5. **Link each person:** on the same screen, **ربط رقم** → choose the user, enter their number in international form
   (963…) → a 6-digit code appears once. From that phone, send the code to the assistant's number within 15 minutes.
   The user needs the `assistant:use` permission (SYSTEM_ADMIN and ACCOUNTANT have it).
6. **Stop it:** revoke a number on the screen; switch the whole assistant off with the `WHATSAPP_ASSISTANT` AI feature flag;
   or `WHATSAPP_ENABLED=false` and deploy to stop the gateway.

Security model: the gateway only carries messages and knows no rules; the API answers only linked numbers (strangers get no reply,
no read receipt, no typing indicator); a number is linked only after it sends a one-time code (hashed, 15 min, 5 tries per link and
per sender); every answer runs as the linked user (permissions, warehouse scope, audit `ASSISTANT.QUERY`) and a deactivated
user loses access at once; `/internal/assistant/*` is not routed by nginx and needs the shared secret; 20 messages/minute per number.

### 3.5 Firewall / network
- `ufw` should allow only 22, 80, 443 to the world. Container → host Postgres needs `5432` from `172.16.0.0/12`.
- ERPNext's port 8080 was opened to reach the setup wizard from a browser. Check `ufw status` and close it to the
  internet (the api reaches it through the host, not the public interface) — see H-4.

---

## 4. Troubleshooting (real incidents)

| Symptom | Cause | Fix |
|---|---|---|
| Deploy says "PostgreSQL unreachable" | container could not resolve the host | `host.docker.internal:host-gateway` + `ufw allow from 172.16.0.0/12 to any port 5432` |
| Health check fails though api is up | nginx `/health` is loopback-only | the script checks via `docker compose exec api wget` |
| **502 Bad Gateway** | api restarted with blank env (compose run without `--env-file`), or still starting | run `bash scripts/deploy-vps.sh` |
| UI changes not visible | `frontend/dist` not rebuilt | deploy via the script |
| `/hangfire` shows the SPA or 401 | route was missing / dashboard needs a Bearer token | use the Accounting Sync screen |
| Jobs never run, `hangfire.job` rows stay `Enqueued` | server not listening to the `governance` queue | fixed in `Program.cs`; keep it |
| 500s on list endpoints | Dapper positional-record type mismatch, or EF/schema drift | alias columns, match CLR types, check the log |
| API crashes on boot: seed password | password policy (needs upper/lower/digit/symbol, ≥ 8) or missing in Production | set `SEED_ADMIN_PASSWORD` |
| Locked out over SSH | SSH hardening applied without a working key | use the provider's VNC console to revert `PermitRootLogin`/`PasswordAuthentication`; multi-line paste into noVNC corrupts text |
| ERPNext rejects a customer | group-type link (`All Customer Groups`) | use leaf groups (`Commercial`, `Rest Of The World`, `Local`) |
| CI red on a test that passed before | timing-dependent test | make it deterministic (see the metrics test) |
| A write returns 500 but the row was created | the idempotency layer failed after commit (`response_code` was `varchar(100)`) | fixed by migration 11; keep response columns `text` |
| Approvals list empty / posting an invoice or stop-ship never completes | maker-checker: requester cannot review own request | log in as a second approver, or set `GOVERNANCE_ALLOW_SELF_APPROVAL=true` temporarily |
| `/auth/me` says user not found; audit rows show an all-zero user id | JWT `sub` remapped, `UserId` = `Guid.Empty` | `MapInboundClaims = false` (fixed) |
| PDF shows boxes or is missing Arabic | fonts not embedded | fonts are embedded resources under `Infrastructure/Exports/Fonts`; rebuild the image, never rely on OS fonts |
| Accounting screens say "ERPNext integration is disabled" (503) | `Erpnext:Enabled` is false or the API cannot reach ERPNext | enable it in `.env.vps`, then redeploy |
| A posted entry stays "بانتظار الإرسال" or "فشل الإرسال" | outbox not processed yet, or ERPNext refused the Journal Entry | open the entry: the error is ERPNext's text (closed period, missing party on a receivable line, currency); fix and press "مزامنة الآن" |
| "The ticked lines do not match the statement" | statement balance ≠ previously cleared + ticked lines | tick the lines that are on the statement; the difference is shown live |
| API stops at start-up with "Database:ConnectionString is not configured" (or Redis) | outside Development the app no longer falls back to a local default | set the missing value in `.env.vps` (production) or run `scripts/init-dev-settings.ps1` (development) |
| A locked period still accepts entries for a few minutes / a locked month cannot be unlocked | (fixed 2026-09-21) the lock answer was cached for 10 minutes and never invalidated; the lock commands were gated by the lock they manage | deploy the fix; nothing to do in the data |
| 500 "Numeric value does not fit in a System.Decimal" | a SQL division returned more digits than .NET decimal holds | round the expression in SQL (fixed for purchase bill posting, 2026-09-23) |
| A transfer stays "بانتظار الموافقة" | it needs the manager of each other warehouse involved (item 9) | give a user manager status on that warehouse (Users → warehouses), or approve as SYSTEM_ADMIN |
| Invoice refused with "SalesRep.NotActive" | the customer's rep was deactivated | reactivate the rep, or give the customer to another rep (المندوبون → إسناد زبائن), or pick another rep on the invoice |
| ERPNext rejects an invoice: Sales Person not found / "Sales Team" missing | the root sales-person group has another name on that server | read the error in the sync log; create "Sales Team" as a group Sales Person in ERPNext or adjust `SyncSalesPersonAsync` |
| Consistency screen: many "أُرسل ثم اختفى" rows | documents were deleted in ERPNext, or the app was pointed at another ERPNext/company | check `Erpnext:BaseUrl` and the company; re-create what is really missing by clearing its sync-log row and running the sync |
| Invoice refused by ERPNext: no default income account for the delivery fee | the company has no Default Income Account | set it in ERPNext (Company → Default Income Account), then "مزامنة الآن" |
| A warehouse user sees empty lists / gets "This warehouse is not assigned to you" | no warehouse assigned, or the document belongs to another warehouse | assign the warehouse (Users → edit → warehouses), or give the role `inventory:all_warehouses` if the person must see all |
| Item import rejects the file | not .xlsx/.csv, > 5 MB, or no `Code` column | download the template from the import dialog |

---

## 5. Production hardening checklist

**Done ✔**
- [x] 2026-09-21: `Program.cs` stops the start-up outside Development/Testing when `Database:ConnectionString` or `Redis:ConnectionString` is missing (no more `postgres/postgres` fallback); `appsettings.Development.json` untracked; `frontend/package-lock.json` tracked; CI has a `build-frontend` job (`npm ci`, `tsc`, build); Grafana in `docker-compose.prod.yml` bound to 127.0.0.1.
- [x] CORS from `AllowedOrigins`; fails closed outside Development.
- [x] Hangfire dashboard requires an authenticated `SYSTEM_ADMIN`.
- [x] No committed default admin password; production requires `Seed__AdminPassword`.
- [x] JWT keys, DB and Redis passwords, ERPNext keys come from `.env.vps` (untracked; local secret files are git-ignored).
- [x] TLS at nginx (self-signed for now), HSTS and security headers, login rate limit.
- [x] Scripted deploy with health verification; CI green; approval replay and Hangfire queues fixed.
- [x] Scalar/OpenAPI are **not proxied** by nginx, and since 2026-09-23 are mapped only in Development (H-6).
- [x] 2026-09-23: no fallback admin password in any environment (the dev settings script generates one); nginx sends a CSP (scripts `'self'` only; styles allow inline for MUI; Google Fonts; `blob:` frames for print; ws/wss for SignalR; none on `/hangfire`), `Permissions-Policy`, `client_max_body_size 6m`, and limits `/auth/refresh` per IP (20/min) as well as login (H-12); the API limits exports, PDFs, imports and AI to 30 per minute per user (`RateLimiting.Heavy`, 429) (H-13); unused packages removed (SemanticKernel, Extensions.AI, Pgvector, FluentEmail, ZXing, SkiaSharp; frontend react-table, zxing, x-data-grid, playwright).
- [x] 2026-09-23 (H-5): database backups — `scripts/backup-db.sh` (pg_dump from the postgres:16 image, custom format, checked with `pg_restore --list` before it replaces anything, SHA-256 file, rotation 14 daily / 8 weekly / 12 monthly / 10 pre-deploy, optional rsync to `BACKUP_REMOTE`), `scripts/restore-check.sh` (restores the newest dump into a throw-away pgvector container and checks tables/users/invoices; the live DB is never touched). `deploy-vps.sh` takes a `predeploy` backup before migrations run (stops the deploy if it fails; `SKIP_PREDEPLOY_BACKUP=1` overrides) and installs `/etc/cron.d/autoparts-erp-backup` (daily 02:30, restore check Sundays 04:15, log `/var/log/autoparts-erp-backup.log` with logrotate). Files in `/var/backups/autoparts-erp` (mode 700). **Restore for real:** `docker run --rm -i --add-host=host.docker.internal:host-gateway -e PGPASSWORD -v /var/backups/autoparts-erp:/backup postgres:16-alpine pg_restore -h <host> -U <user> -d <empty db> --no-owner /backup/daily/<file>.dump` (stop the api first; restore into an empty database).
- [x] 2026-09-24: `robots.txt` disallows everything (all crawlers, AI crawlers named), nginx adds `X-Robots-Tag: noindex, nofollow, noarchive, nosnippet, noimageindex, notranslate, noai, noimageai` on every response, and the page carries a `robots` meta tag. (robots.txt is only a request; sign-in is the real protection.)
- [x] 2026-09-24: document numbers belong to the database (gapless per series, immutable, never reused); deleting a document is reserved to SYSTEM_ADMIN (`documents:delete` cannot be granted to another role), only for drafts or voided documents, with a snapshot in `deleted_documents` — the database refuses any other delete. Check: سجل التدقيق ← الترقيم والمحذوفات.
- [x] 2026-09-24: SignalR authenticates over WebSocket (token read from the query string on `/hubs` only); the client-callable `JoinGroup(name)` that let any user join any group was removed.
- [x] 2026-09-24: demo data (sample customers, items, stock, invoices) is seeded only in Development or with `Seed:DemoData=true`. **Earlier versions seeded it in every environment** — see "Production data to review" below.
- [x] 2026-09-23 (H-11): the refresh token is only an `erp_rt` cookie — `HttpOnly`, `SameSite=Strict`, `Secure` outside Development, `Path=/api/v1/auth` — never in a response body; the access token lives in page memory only (nothing in localStorage; old saved sessions are deleted on load). Refresh and logout require `X-Requested-With` (CSRF). Refresh is single-use (rotated), refused for locked/deactivated users (previously a deactivated user could renew for 7 days), and logout revokes it server-side (previously sign-out never reached the server).
- [x] 2026-09-23: every `/api` response is `Cache-Control: no-store` (H-12); the unused CI deploy job no longer hides failures — the impossible `dotnet ef … || true` (no SDK in the runtime image; the API migrates on start-up) was replaced by a health wait that fails the job (H-15).

**Production data (owner decision 2026-09-24: all current data is trial data, wipe it)**
- Until 2026-09-24 the API inserted demo data on first start in every environment, and the system was tried out on the server since. The owner decided everything there is trial data. Tool: after deploying, run on the server `cd /erp && bash scripts/reset-business-data.sh` — it shows what would go (dry run), asks for the typed phrase `DELETE-ALL-BUSINESS-DATA`, stops the API, empties the ERPNext company (ERPNext's own Transaction Deletion Record, then items, prices, customers, suppliers, sales persons; chart of accounts, cost centres, warehouses and the company stay) and this database (SYSTEM_ADMIN accounts, roles, reason codes, categories, entry types stay; numbering restarts), then starts the API. No backup is taken by the tool; `bash scripts/backup-db.sh manual` first if in doubt. ERPNext's background workers must be running (the deletion runs there); the API key's user needs the System Manager role — being able to read a Transaction Deletion Record is not enough (other roles can), only System Manager may create one. The dry run checks the role itself (the user's Has Role rows, readable only by System Manager) and names the user, so the confirmation is never asked for a run that cannot finish; add it as Administrator in ERPNext → User → Roles. If ERPNext fails, this database is left untouched and the run can be repeated. Options: `--skip-erpnext`, `--keep-users`.

**Health check:** `cd /erp && bash scripts/diagnose.sh` — read-only, secrets masked, report saved to `/var/log/autoparts-erp-diagnose-<time>.txt` with a FAIL / WARN / INFO summary at the end.

**Trial-phase exceptions (owner decision 2026-09-24)**
The current server (130.94.45.230) is a trial deployment for development. The owner will move to a new server and a domain for the real launch, and chose to leave these as they are until then — `diagnose.sh` keeps reporting them as WARN on purpose:
- ERPNext's own web interface is published on port 8080 to the internet over plain HTTP (frappe_docker `frontend`, `0.0.0.0:8080`, ufw `8080/tcp ALLOW Anywhere`). **At launch:** publish it on the Docker bridge only (e.g. `172.17.0.1:8080`, still reachable from the api as `host.docker.internal:8080`) or behind nginx with TLS, and open the ERPNext desk through an SSH tunnel.
- SSH accepts root with a password (`PermitRootLogin yes`, `PasswordAuthentication yes`; ~2,300 failed attempts a day, fail2ban's `sshd` jail is on). **At launch:** a non-root sudo user with a key, then `PermitRootLogin no` and `PasswordAuthentication no` — only after signing in with the key has been verified.

**Pending**
- [ ] **H-0 Dev key in git history:** `appsettings.Development.json` (a dev JWT private key and the dev DB password) sat in the public repository from the first commit until 2026-09-21 and is still in history. It never signed production tokens (the VPS generates its own pair), but treat it as public: do not reuse that DB password anywhere; decide whether to purge history (force-push, owner decision).
- [ ] **H-1 Rotate exposed secrets:** Postgres password, JWT key pair (invalidates sessions), Redis password. Then
      delete/relocate any local `ADMIN PASSWORD.txt`.
- [ ] **H-2 Domain + real TLS** (Let's Encrypt or Cloudflare). `docs/cloudflare-setup.md` is a template that still
      mentions another provider — adapt before use. Then set `ALLOWED_ORIGINS` to the real origins.
- [ ] **H-3 SSH:** install a key, verify a second session works, *then* set `PasswordAuthentication no`; add `fail2ban`.
- [ ] **H-4 ERPNext:** change the `Administrator` password; close port 8080 to the internet (`ufw status` to check).
- [ ] **H-5 (rest) Off-server copy:** set `BACKUP_REMOTE=user@host:/path` in `.env.vps` (a second machine with the server's SSH key in its `authorized_keys`); until then backups live only on the VPS. The one-click backup screen is Phase 5.
- [ ] **H-7** Server upgrade (RAM/CPU) before production load; monitor swap.
- [ ] **H-8** Set GitHub secrets `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` so CI can deploy (currently skipped).
- [ ] **H-9** Decide migration policy for production (auto-migrate on boot vs a controlled step). A backup is now taken before every deploy (2026-09-23).
- [ ] **H-14 Pin versions (rest):** GitHub Actions to commit SHAs, Docker images to versions/digests (`latest` for Prometheus/Grafana/Loki/Tempo in the prod compose), dependency and image scanning in CI. Dependabot is on since 2026-09-23 (`.github/dependabot.yml`: weekly, grouped minor/patch PRs for NuGet, npm, Actions, Docker).
- [ ] **H-10** AI/notification keys (`AI_*`, `SMTP_*`) live only in `.env.vps`; document their rotation.

---

## 6. Reproducibility notes
- Commit `frontend/package-lock.json`; use `npm ci` in CI. Consider `Directory.Packages.props` and lock files for
  NuGet. Prefer image digests in production compose files.
- Remove unused dependencies (see PROJECT_VISION §2) to shrink builds and attack surface.
- `NuGet.Config` must stay simple — a hardcoded global packages folder once broke CI (a missing font asset path).
