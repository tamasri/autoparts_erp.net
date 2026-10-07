#!/usr/bin/env bash
# Restores an archive from scripts/migrate-export.sh into the Coolify stack (docker-compose.coolify.yml; docs/COOLIFY.md).
# Run on the NEW server, after the first successful Coolify deploy (all containers up, ERPNext site "frontend" created):
#
#   curl -fsSL https://raw.githubusercontent.com/tamasri/autoparts_erp.net/main/scripts/migrate-import.sh -o migrate-import.sh
#   bash migrate-import.sh /root/autoparts-migration-<time>.tar.gz
#
# It REPLACES the app database and the ERPNext site of the new stack with the archive's contents (asks first).
# ERPNext's Administrator password is then set to Coolify's SERVICE_PASSWORD_ERPNEXTADMIN (no more "admin").
set -Eeuo pipefail

ARCHIVE="${1:-}"
SITE=frontend
fail() { echo "[ERROR] $1" >&2; exit 1; }
step() { echo; echo "== $1"; }

[[ $EUID -eq 0 ]] || fail "Run as root."
[[ -f "$ARCHIVE" ]] || fail "Usage: bash migrate-import.sh /root/autoparts-migration-<time>.tar.gz"
if [[ -f "$ARCHIVE.sha256" ]]; then
  [[ "$(sha256sum "$ARCHIVE" | awk '{print $1}')" == "$(cat "$ARCHIVE.sha256")" ]] || fail "Checksum mismatch: the copy is damaged; copy it again."
  echo "Checksum OK"
fi

# One container per role, found by the labels in docker-compose.coolify.yml.
one() {
  local ids; ids=$(docker ps -a --filter "label=autoparts.service=$1" --format '{{.Names}}')
  [[ -n "$ids" ]] || fail "No container labelled autoparts.service=$1. Deploy the stack in Coolify first."
  [[ $(wc -l <<<"$ids") -eq 1 ]] || fail "More than one autoparts.service=$1 container: $(tr '\n' ' ' <<<"$ids")"
  echo "$ids"
}
all() { docker ps -a --filter "label=autoparts.service=$1" --format '{{.Names}}'; }
env_of() { docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$1" | sed -n "s/^$2=//p" | head -1; }

pg=$(one postgres); api=$(one api); backend=$(one erpnext-backend); create_site=$(one erpnext-create-site)
web=$(one web)
docker exec "$backend" test -f "sites/$SITE/site_config.json" || fail "ERPNext site '$SITE' does not exist yet: wait for erpnext-create-site to finish (Coolify logs), then run again."

work=$(mktemp -d /root/autoparts-import.XXXXXX)
trap 'rm -rf "$work"' EXIT
umask 077
tar -C "$work" -xzf "$ARCHIVE"
src=$(find "$work" -mindepth 1 -maxdepth 1 -type d | head -1)
for f in app.dump erpnext-database.sql.gz erpnext-site_config.json; do [[ -f "$src/$f" ]] || fail "$f is missing from the archive."; done

echo
echo "This replaces the app database and the ERPNext site on THIS server with the archive's data."
read -r -p "Type IMPORT to continue: " answer
[[ "$answer" == "IMPORT" ]] || fail "Cancelled."

step "Stopping the API and ERPNext workers"
docker stop "$web" "$api" >/dev/null
for c in $(all erpnext-worker); do docker stop "$c" >/dev/null; done

step "App database"
db=$(env_of "$pg" POSTGRES_DB); user=$(env_of "$pg" POSTGRES_USER)
docker exec "$pg" psql -v ON_ERROR_STOP=1 -U "$user" -d postgres -c "DROP DATABASE IF EXISTS \"$db\" WITH (FORCE);" -c "CREATE DATABASE \"$db\" OWNER \"$user\";"
docker exec -i "$pg" pg_restore -U "$user" -d "$db" --no-owner --no-privileges --exit-on-error -1 < "$src/app.dump"
docker exec "$pg" psql -At -U "$user" -d "$db" -c "SELECT 'items: '||(SELECT count(*) FROM skus)||', invoices: '||(SELECT count(*) FROM invoices)||', customers: '||(SELECT count(*) FROM customers);" || true

step "ERPNext"
docker exec -u 0 "$backend" bash -c 'rm -rf /tmp/restore && mkdir -p /tmp/restore'
for f in erpnext-database.sql.gz erpnext-files.tar erpnext-private-files.tar erpnext-site_config.json; do
  if [[ -f "$src/$f" ]]; then docker cp "$src/$f" "$backend:/tmp/restore/$f"; fi
done
docker exec -u 0 "$backend" chown -R frappe:frappe /tmp/restore
files=""
if [[ -f "$src/erpnext-files.tar" ]]; then files="$files --with-public-files /tmp/restore/erpnext-files.tar"; fi
if [[ -f "$src/erpnext-private-files.tar" ]]; then files="$files --with-private-files /tmp/restore/erpnext-private-files.tar"; fi
# DB_ROOT_PASSWORD is in the backend container's own environment (docker-compose.coolify.yml); it never passes through here.
docker exec "$backend" bash -c "bench --site $SITE restore /tmp/restore/erpnext-database.sql.gz $files --db-root-username root --db-root-password \"\$DB_ROOT_PASSWORD\" --force"
# The old encryption key: without it ERPNext cannot read the API secret the app signs in with.
docker exec "$backend" bash -c "key=\$(jq -r '.encryption_key // empty' /tmp/restore/erpnext-site_config.json); [ -n \"\$key\" ] && bench --site $SITE set-config encryption_key \"\$key\" && echo 'encryption_key restored'"
docker exec "$backend" bench --site "$SITE" migrate
admin_password=$(env_of "$create_site" ADMIN_PASSWORD)
if [[ -n "$admin_password" ]]; then
  docker exec -e NEW_ADMIN_PASSWORD="$admin_password" "$backend" bash -c "bench --site $SITE set-admin-password \"\$NEW_ADMIN_PASSWORD\" >/dev/null" \
    && echo "Administrator password set to Coolify's SERVICE_PASSWORD_ERPNEXTADMIN"
fi
docker exec "$backend" bench --site "$SITE" clear-cache
docker exec -u 0 "$backend" rm -rf /tmp/restore

step "Starting everything"
docker restart "$backend" >/dev/null
for c in $(all erpnext-worker) $(all erpnext-websocket) $(all erpnext-frontend); do docker restart "$c" >/dev/null 2>&1 || docker start "$c" >/dev/null; done
docker start "$api" >/dev/null
for i in $(seq 1 60); do
  state=$(docker inspect -f '{{.State.Health.Status}}' "$api" 2>/dev/null || echo starting)
  [[ "$state" == "healthy" ]] && break
  sleep 3
done
[[ "$state" == "healthy" ]] || { docker logs --tail 80 "$api"; fail "The API did not become healthy."; }
docker start "$web" >/dev/null
echo "API healthy (pending migrations were applied on start)."
if docker exec "$api" wget -qO- http://erpnext-frontend:8080/api/method/ping | grep -q pong; then
  echo "ERPNext reachable from the API."
else
  echo "[WARN] The API cannot reach ERPNext yet: docker logs $(all erpnext-frontend)"
fi

echo
echo "Done. Next: point the domain's A records to this server; Coolify then issues the certificate."
echo "Check: https://almajdauto.com/erp — sign in, open an invoice, run Accounting Sync."
echo "Delete the archive once verified: rm -f $ARCHIVE $ARCHIVE.sha256"
