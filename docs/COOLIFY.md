# Deploying on Coolify (and moving from the self-managed VPS)

The whole system runs as **one Coolify "Docker Compose" resource** built from this repository:
`docker-compose.coolify.yml`.

```
Internet ─▶ Coolify proxy (Traefik, HTTPS, Let's Encrypt) ─▶ web :80  (SPA at /erp/ + reverse proxy, deploy/coolify/nginx.conf)
                                                              ├─ /api, /hubs, /hangfire ─▶ api :8080
api ─▶ postgres (16, volume postgres-data)    api ─▶ redis (7)    api ─▶ erpnext-frontend :8080 (private)
ERPNext: erpnext-db (MariaDB 11.8), erpnext-backend, -frontend, -websocket, -queue-short, -queue-long, -scheduler,
         -redis-cache, -redis-queue; erpnext-configurator and erpnext-create-site run once.
backup: nightly app pg_dump + ERPNext database/files → host folder /var/backups/autoparts-erp
```

Only `web` gets a domain. No service publishes a port, so the server's other Coolify sites are not affected and ERPNext
is no longer reachable from the internet (the old trial exception on port 8080 is gone).

## 1. Requirements
- The new server: Coolify v4 running, **at least 4 GB RAM free** for this stack (ERPNext alone takes ~2 GB) and ~15 GB disk.
- Root SSH to the old and the new server.
- `deploy/coolify/web.Dockerfile` builds the frontend inside Docker; nothing is installed on the host.

## 2. Create the resource in Coolify
1. Projects → (a project) → **+ New** → **Public Repository** (or GitHub App) →
   `https://github.com/tamasri/autoparts_erp.net`, branch `main`.
2. Build Pack: **Docker Compose**. Base Directory `/`. Docker Compose Location `/docker-compose.coolify.yml`.
3. Services → **web** → Domains: `https://almajdauto.com,https://www.almajdauto.com`. Leave every other service without a domain.
4. Environment Variables → Developer view: paste `coolify.env` from the export archive (§3). Coolify generates
   `SERVICE_PASSWORD_POSTGRES`, `SERVICE_PASSWORD_REDIS`, `SERVICE_PASSWORD_ERPNEXTDB`, `SERVICE_PASSWORD_ERPNEXTADMIN`.
   Optional: `ERPNEXT_VERSION` (default `v16.50.0`), `BACKUP_TIME` (default `02:30`), `TZ` (default `Asia/Damascus`),
   `KEEP_DAILY/WEEKLY/MONTHLY`, `BACKUP_HOST_DIR`.
5. **Deploy.** The first deploy builds two images and pulls ERPNext; `erpnext-create-site` needs a few minutes to create
   the site `frontend`. Coolify shows `erpnext-configurator` / `erpnext-create-site` as exited — that is their job.

Without migrated data (a fresh install) also set `SEED_ADMIN_EMAIL`, `SEED_ADMIN_USERNAME`, `SEED_ADMIN_PASSWORD`, then
create the ERPNext company and API key (SETUP_HARDENING §3.4).

## 3. Move from the old server
1. **Lower the DNS TTL** of `@` and `www` to 300 a day before.
2. Rehearsal (system keeps running) on the old server: `cd /erp && git pull origin main && bash scripts/migrate-export.sh`.
3. Copy: `scp /root/autoparts-migration-*.tar.gz* root@NEW_IP:/root/`.
4. In Coolify: create the resource (§2) with the archive's `coolify.env`; deploy; wait for the site to exist.
5. On the new server:
   `curl -fsSL https://raw.githubusercontent.com/tamasri/autoparts_erp.net/main/scripts/migrate-import.sh -o migrate-import.sh && bash migrate-import.sh /root/autoparts-migration-<time>.tar.gz`
   It stops api/web/ERPNext workers, replaces the app database (`pg_restore`), restores ERPNext (`bench restore` with
   files), puts back the old `encryption_key` (the ERPNext API secret depends on it), runs `bench migrate`, sets the
   ERPNext `Administrator` password to `SERVICE_PASSWORD_ERPNEXTADMIN`, starts everything and checks API → ERPNext.
6. Test before DNS: on your PC add `NEW_IP almajdauto.com` to the hosts file, or just check the logs; then the real cutover:
   old server `bash scripts/migrate-export.sh --final` (stops the old API and nginx), copy, import again (step 5).
7. Point the A records of `@` and `www` to the new server. Coolify issues the certificate once DNS resolves.
8. Verify: sign in, open an invoice and its PDF, Accounting Sync → "مزامنة الآن", `docker exec <backup> autoparts-backup now`.
9. Delete the archives on both servers. Keep the old server switched off (not deleted) for a week, then cancel it.

## 4. Operations
- **Deploy a new version:** push to `main`, then Coolify → Redeploy (or enable its auto-deploy webhook). Pending EF
  migrations run when the api starts.
- **Logs:** Coolify → the resource → Logs (per service).
- **Backups:** `/var/backups/autoparts-erp/{daily,weekly,monthly}` on the host — `app_<time>.dump` (pg_restore format),
  `erpnext_<time>.sql.gz` and `erpnext_<time>_files.tar.gz`. A backup now: `docker exec $(docker ps -qf label=autoparts.service=backup) autoparts-backup now`.
  Copy them off the server (another machine or object storage) — a lost server loses local backups too.
- **ERPNext desk:** private by default. To open it, give `erpnext-frontend` a domain such as
  `https://books.almajdauto.com:8080` (an A record for it first) and sign in as Administrator with
  `SERVICE_PASSWORD_ERPNEXTADMIN`; remove the domain afterwards.
- **psql:** `docker exec -it $(docker ps -qf label=autoparts.service=postgres) psql -U autoparts autoparts_erp`.
- **WhatsApp assistant:** not part of this stack yet (the gateway's linked-device session would need moving too).

## 5. Restore from a nightly backup
- App: stop `api`, then `docker exec -i <postgres> pg_restore -U autoparts -d autoparts_erp --clean --if-exists --no-owner < app_<time>.dump`, start `api`.
- ERPNext: copy the files into `erpnext-backend` and run
  `bench --site frontend restore <erpnext_<time>.sql.gz> --db-root-username root --db-root-password "$DB_ROOT_PASSWORD" --force`,
  then extract `erpnext_<time>_files.tar.gz` into `sites/frontend/`, `bench --site frontend migrate`.
