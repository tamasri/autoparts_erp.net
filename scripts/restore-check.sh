#!/usr/bin/env bash
# Proves the newest backup can actually be restored. Runs weekly from cron (installed by deploy-vps.sh).
#
#   bash scripts/restore-check.sh [path/to/file.dump]
#
# The dump is restored into a throw-away PostgreSQL container (pgvector image: the schema needs the vector extension),
# checked, and the container is removed. The live database is never touched.
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ENV_FILE:-$ROOT_DIR/.env.vps}"
[[ -f "$ENV_FILE" ]] && { set -a; source "$ENV_FILE"; set +a; }
BACKUP_DIR="${BACKUP_DIR:-/var/backups/autoparts-erp}"
IMAGE="${RESTORE_CHECK_IMAGE:-pgvector/pgvector:pg16}"

log() { echo "[restore-check $(date '+%Y-%m-%d %H:%M:%S')] $*"; }
fail() { log "FAILED: $*"; exit 1; }

dump="${1:-$(ls -1t "$BACKUP_DIR"/daily/*.dump "$BACKUP_DIR"/predeploy/*.dump 2>/dev/null | head -1 || true)}"
[[ -n "$dump" && -f "$dump" ]] || fail "no backup found in $BACKUP_DIR"
if [[ -f "$dump.sha256" ]]; then
  (cd "$(dirname "$dump")" && sha256sum -c --quiet "$(basename "$dump").sha256") || fail "checksum mismatch for $dump"
fi

name="autoparts-restore-check-$$"
trap 'docker rm -f "$name" >/dev/null 2>&1 || true' EXIT
docker pull -q "$IMAGE" >/dev/null || fail "cannot pull $IMAGE"
# A random password for the throw-away server only; it exists for the few minutes of the check.
docker run -d --name "$name" -e POSTGRES_PASSWORD="$(head -c 18 /dev/urandom | base64)" -e POSTGRES_DB=restore_check \
  -v "$(cd "$(dirname "$dump")" && pwd):/backup:ro" "$IMAGE" >/dev/null
for _ in $(seq 1 120); do docker exec "$name" pg_isready -U postgres -d restore_check >/dev/null 2>&1 && break; sleep 1; done
docker exec "$name" pg_isready -U postgres -d restore_check >/dev/null 2>&1 || fail "throw-away server did not start"

log "restoring $(basename "$dump")"
docker exec "$name" pg_restore -U postgres -d restore_check --no-owner --no-privileges --exit-on-error "/backup/$(basename "$dump")" \
  || fail "pg_restore reported errors"

q() { docker exec "$name" psql -U postgres -d restore_check -Atc "$1"; }
tables=$(q "select count(*) from information_schema.tables where table_schema='public'")
users=$(q "select count(*) from asp_net_users")
invoices=$(q "select count(*) from invoices")
items=$(q "select count(*) from items")
latest=$(q "select coalesce(max(created_at)::text,'-') from invoices")
[[ "$tables" -gt 50 && "$users" -gt 0 ]] || fail "restored database looks empty (tables=$tables users=$users)"
log "ok: $tables tables, $users users, $invoices invoices, $items items, newest invoice $latest"
