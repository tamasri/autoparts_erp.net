#!/usr/bin/env bash
# Packs everything the new (Coolify) server needs from this self-managed server into one archive (docs/COOLIFY.md).
#
#   bash scripts/migrate-export.sh            # rehearsal: the system keeps running
#   bash scripts/migrate-export.sh --final    # cutover: stops the API and nginx first (no more writes), and leaves them stopped
#
# The archive (/root/autoparts-migration-<time>.tar.gz, mode 600) holds SECRETS: the app database, the ERPNext backup and
# site_config.json (its encryption_key decrypts the ERPNext API secret), and coolify.env (JWT keys, ERPNext API key...).
# Copy it to the new server over SSH only, and delete it from both servers once the move is verified.
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"
ENV_FILE=".env.vps"
COMPOSE=(docker compose --env-file "$ENV_FILE" -f docker-compose.vps.yml)
FINAL=0
[[ "${1:-}" == "--final" ]] && FINAL=1

fail() { echo "[ERROR] $1" >&2; exit 1; }
step() { echo; echo "== $1"; }

[[ $EUID -eq 0 ]] || fail "Run as root."
[[ -f "$ENV_FILE" ]] || fail "$ENV_FILE not found (run this in the old server's checkout, e.g. /erp)."
set -a; source "$ENV_FILE"; set +a

stamp="$(date '+%Y%m%d-%H%M%S')"
work="/root/autoparts-migration-$stamp"
umask 077
mkdir -p "$work"

if [[ "$FINAL" == "1" ]]; then
  step "Cutover: stopping the API and nginx (users see the site as down from now on)"
  "${COMPOSE[@]}" stop nginx api
  echo "Stopped. If you abort the move: ${COMPOSE[*]} start api nginx"
fi

step "App database ($POSTGRES_DB)"
PGPASSWORD="$POSTGRES_PASSWORD" docker run --rm --add-host=host.docker.internal:host-gateway -e PGPASSWORD -v "$work:/out" postgres:16-alpine \
  pg_dump -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -Z 6 --no-owner --no-privileges -f /out/app.dump
objects=$(docker run --rm -v "$work:/out" postgres:16-alpine pg_restore --list /out/app.dump | grep -vc '^;')
[[ "$objects" -gt 50 ]] || fail "The app dump lists only $objects objects."
echo "app.dump: $(du -h "$work/app.dump" | cut -f1), $objects objects"

step "ERPNext"
backend=$(docker ps --filter "label=com.docker.compose.service=backend" --format '{{.Names}} {{.Image}}' | awk '$2 ~ /frappe\/erpnext/ {print $1; exit}')
[[ -n "$backend" ]] || fail "No running ERPNext backend container (frappe/erpnext, service 'backend') found."
site="${ERPNEXT_SITE:-$(docker exec "$backend" bash -c 'cat sites/currentsite.txt 2>/dev/null || ls sites/*/site_config.json 2>/dev/null | head -1 | cut -d/ -f2')}"
[[ -n "$site" ]] || fail "Could not find the ERPNext site name; set ERPNEXT_SITE=<name>."
echo "container $backend, site $site"
docker exec "$backend" bench version > "$work/erpnext-versions.txt" 2>/dev/null || true
cat "$work/erpnext-versions.txt"

marker="/tmp/autoparts-export-$stamp"
docker exec "$backend" touch "$marker"
docker exec "$backend" bench --site "$site" backup --with-files
dir="sites/$site/private/backups"
# "*-files.tar" alone would also match "*-private-files.tar".
pick() { docker exec "$backend" bash -c "find $dir -maxdepth 1 -name '$1' ! -name '*-private-files.tar' -newer $marker | sort | tail -1"; }
pick_private() { docker exec "$backend" bash -c "find $dir -maxdepth 1 -name '*-private-files.tar' -newer $marker | sort | tail -1"; }
db_file=$(pick '*-database.sql.gz'); pub_file=$(pick '*-files.tar'); priv_file=$(pick_private)
[[ -n "$db_file" ]] || fail "ERPNext wrote no database backup."
docker cp "$backend:/home/frappe/frappe-bench/$db_file" "$work/erpnext-database.sql.gz"
if [[ -n "$pub_file" ]]; then docker cp "$backend:/home/frappe/frappe-bench/$pub_file" "$work/erpnext-files.tar"; fi
if [[ -n "$priv_file" ]]; then docker cp "$backend:/home/frappe/frappe-bench/$priv_file" "$work/erpnext-private-files.tar"; fi
docker cp "$backend:/home/frappe/frappe-bench/sites/$site/site_config.json" "$work/erpnext-site_config.json"
docker exec "$backend" rm -f "$marker"
gzip -t "$work/erpnext-database.sql.gz" || fail "The ERPNext database backup is not a valid gzip file."
ls -la "$work" | grep erpnext

step "Settings for Coolify (coolify.env)"
keys=(JWT_PRIVATE_KEY JWT_PUBLIC_KEY ERPNEXT_API_KEY ERPNEXT_API_SECRET ASSISTANT_GATEWAY_SECRET AI_API_KEY AI_BASE_URL AI_MODEL
      GOVERNANCE_ALLOW_SELF_APPROVAL SEED_ADMIN_EMAIL SEED_ADMIN_USERNAME)
{
  echo "# Paste into Coolify → the resource → Environment Variables → Developer view. SECRETS: do not share."
  for k in "${keys[@]}"; do
    v="${!k:-}"
    [[ -n "$v" ]] && printf '%s=%s\n' "$k" "$v"
  done
  echo "ERPNEXT_ENABLED=${ERPNEXT_ENABLED:-true}"
  echo "ALLOWED_ORIGINS=https://almajdauto.com,https://www.almajdauto.com"
  echo "APP_PUBLIC_URL=https://almajdauto.com/erp"
  echo "POSTGRES_DB=$POSTGRES_DB"
  echo "POSTGRES_USER=$POSTGRES_USER"
  ver=$(grep -oiE 'erpnext[^0-9]*[0-9]+\.[0-9]+\.[0-9]+' "$work/erpnext-versions.txt" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1 || true)
  [[ -n "$ver" ]] && echo "# ERPNext here is v$ver; the new stack defaults to v16.50.0 (the restore migrates up). To pin: ERPNEXT_VERSION=v$ver"
} > "$work/coolify.env"
echo "Variables written (values not shown): $(grep -oE '^[A-Z_]+=' "$work/coolify.env" | tr -d '=' | tr '\n' ' ')"

step "Archive"
archive="/root/autoparts-migration-$stamp.tar.gz"
tar -C /root -czf "$archive" "$(basename "$work")"
chmod 600 "$archive"
sha256sum "$archive" | awk '{print $1}' > "$archive.sha256"
rm -rf "$work"
echo "$archive ($(du -h "$archive" | cut -f1))"
echo
echo "Next, from this server (replace NEW_SERVER_IP):"
echo "  scp $archive $archive.sha256 root@NEW_SERVER_IP:/root/"
if [[ "$FINAL" == "1" ]]; then echo "The API and nginx stay stopped here. Switch DNS to the new server after the import is verified."; fi
