# SETUP_HARDENING.md — AutoPartsERP

> Setup, deployment and hardening for **`autoparts_erp.net`** only. *Last verified 2026-09-19 against the running VPS.*
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
`Seed:AdminPassword` (env: `Seed__AdminPassword`, …). In **Development** a fallback (in `DatabaseSeeder`) exists so a fresh checkout works; in
**Production** the API refuses to start without a real password. There is no shared default password.

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
The API user needs read on Account, Company, GL Entry, Journal Entry, Payment Entry, Sales/Purchase Invoice, Customer, Supplier and Item, and permission to call `erpnext.accounts.utils.get_balance_on`. If the chart of accounts screen shows an error, read the message: it is ERPNext's own permission text. Roles allowed to open these screens in our app: SYSTEM_ADMIN, AUDITOR, COMPLIANCE_OFFICER and the legacy ACCOUNTANT.

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
| Item import rejects the file | not .xlsx/.csv, > 5 MB, or no `Code` column | download the template from the import dialog |

---

## 5. Production hardening checklist

**Done ✔**
- [x] CORS from `AllowedOrigins`; fails closed outside Development.
- [x] Hangfire dashboard requires an authenticated `SYSTEM_ADMIN`.
- [x] No committed default admin password; production requires `Seed__AdminPassword`.
- [x] JWT keys, DB and Redis passwords, ERPNext keys come from `.env.vps` (untracked; local secret files are git-ignored).
- [x] TLS at nginx (self-signed for now), HSTS and security headers, login rate limit.
- [x] Scripted deploy with health verification; CI green; approval replay and Hangfire queues fixed.
- [x] Scalar/OpenAPI are **not proxied** by nginx (unreachable from outside).

**Pending**
- [ ] **H-1 Rotate exposed secrets:** Postgres password, JWT key pair (invalidates sessions), Redis password. Then
      delete/relocate any local `ADMIN PASSWORD.txt`.
- [ ] **H-2 Domain + real TLS** (Let's Encrypt or Cloudflare). `docs/cloudflare-setup.md` is a template that still
      mentions another provider — adapt before use. Then set `ALLOWED_ORIGINS` to the real origins.
- [ ] **H-3 SSH:** install a key, verify a second session works, *then* set `PasswordAuthentication no`; add `fail2ban`.
- [ ] **H-4 ERPNext:** change the `Administrator` password; close port 8080 to the internet (`ufw status` to check).
- [ ] **H-5 Backups:** scheduled `pg_dump` of `autoparts_erp` with rotation and an off-server copy; restore test; the
      one-click backup screen is Phase 5.
- [ ] **H-6** Map Scalar/OpenAPI only in Development (or gate by role).
- [ ] **H-7** Server upgrade (RAM/CPU) before production load; monitor swap.
- [ ] **H-8** Set GitHub secrets `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` so CI can deploy (currently skipped).
- [ ] **H-9** Decide migration policy for production (auto-migrate on boot vs a controlled step) and take a backup first.
- [ ] **H-10** AI/notification keys (`AI_*`, `SMTP_*`) live only in `.env.vps`; document their rotation.

---

## 6. Reproducibility notes
- Commit `frontend/package-lock.json`; use `npm ci` in CI. Consider `Directory.Packages.props` and lock files for
  NuGet. Prefer image digests in production compose files.
- Remove unused dependencies (see PROJECT_VISION §2) to shrink builds and attack surface.
- `NuGet.Config` must stay simple — a hardcoded global packages folder once broke CI (a missing font asset path).
