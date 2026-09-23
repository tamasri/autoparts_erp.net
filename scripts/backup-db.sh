#!/usr/bin/env bash
# Database backup for the VPS. Runs daily from cron (installed by deploy-vps.sh) and before every deploy.
#
#   bash scripts/backup-db.sh            # daily backup (+ weekly on Sunday, monthly on the 1st)
#   bash scripts/backup-db.sh predeploy  # labelled copy taken just before a deploy/migration
#
# - pg_dump runs from the postgres:16-alpine image (same major version as the server; nothing to install on the host).
# - Custom format (-Fc, compressed), written to a temp name, checked with pg_restore --list, then renamed: a partial or
#   unreadable dump never replaces a good one.
# - Rotation: KEEP_DAILY days, KEEP_WEEKLY Sunday copies, KEEP_MONTHLY first-of-month copies, KEEP_PREDEPLOY predeploy copies.
# - Off-server copy (strongly recommended): set BACKUP_REMOTE=user@host:/path in .env.vps (rsync over SSH with a key).
# - Credentials come from .env.vps; the password is passed through the environment, never on a command line.
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ENV_FILE:-$ROOT_DIR/.env.vps}"
LABEL="${1:-daily}"

[[ -f "$ENV_FILE" ]] || { echo "[backup] $ENV_FILE not found" >&2; exit 1; }
set -a; source "$ENV_FILE"; set +a

BACKUP_DIR="${BACKUP_DIR:-/var/backups/autoparts-erp}"
KEEP_DAILY="${KEEP_DAILY:-14}"
KEEP_WEEKLY="${KEEP_WEEKLY:-8}"
KEEP_MONTHLY="${KEEP_MONTHLY:-12}"
KEEP_PREDEPLOY="${KEEP_PREDEPLOY:-10}"
PG_IMAGE="${BACKUP_PG_IMAGE:-postgres:16-alpine}"

log() { echo "[backup $(date '+%Y-%m-%d %H:%M:%S')] $*"; }
fail() { log "FAILED: $*"; exit 1; }

case "$LABEL" in daily|predeploy|manual) ;; *) fail "unknown label '$LABEL' (daily|predeploy|manual)";; esac

# One backup at a time (cron and a deploy could overlap).
exec 9>"${TMPDIR:-/tmp}/autoparts-erp-backup.lock"
command -v flock >/dev/null 2>&1 && { flock -n 9 || fail "another backup is running"; }

umask 077
mkdir -p "$BACKUP_DIR"/{daily,weekly,monthly,predeploy}
chmod 700 "$BACKUP_DIR"

stamp="$(date '+%Y-%m-%d_%H%M%S')"
target_dir="$BACKUP_DIR/$LABEL"
[[ "$LABEL" == "manual" ]] && target_dir="$BACKUP_DIR/daily"
name="${POSTGRES_DB}_${stamp}_${LABEL}.dump"
tmp="$target_dir/.${name}.partial"

export PGPASSWORD="$POSTGRES_PASSWORD"
pg() { docker run --rm --add-host=host.docker.internal:host-gateway -e PGPASSWORD -v "$BACKUP_DIR:/backup" "$PG_IMAGE" "$@"; }
in_container() { echo "/backup/${1#"$BACKUP_DIR"/}"; }

log "dumping $POSTGRES_DB from $POSTGRES_HOST:$POSTGRES_PORT → $target_dir/$name"
start=$(date +%s)
pg pg_dump -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
   -Fc -Z 6 --no-owner --no-privileges -f "$(in_container "$tmp")" \
  || { rm -f "$tmp"; fail "pg_dump"; }

# A dump that pg_restore cannot list is not a backup.
objects=$(pg pg_restore --list "$(in_container "$tmp")" | grep -vc '^;') || { rm -f "$tmp"; fail "pg_restore --list could not read the dump"; }
[[ "$objects" -gt 50 ]] || { rm -f "$tmp"; fail "dump lists only $objects objects"; }

mv "$tmp" "$target_dir/$name"
sha256sum "$target_dir/$name" > "$target_dir/$name.sha256"
size=$(du -h "$target_dir/$name" | cut -f1)
log "ok: $name ($size, $objects objects, $(( $(date +%s) - start ))s)"

# Weekly and monthly copies are hard links of the daily file (no extra space until the daily one rotates out).
keep_copy() { ln -f "$target_dir/$name" "$BACKUP_DIR/$1/$name"; cp "$target_dir/$name.sha256" "$BACKUP_DIR/$1/"; log "kept as $1"; }
if [[ "$LABEL" == "daily" ]]; then
  if [[ "${FORCE_WEEKLY:-}" == "1" || "$(date +%u)" == "7" ]]; then keep_copy weekly; fi
  if [[ "${FORCE_MONTHLY:-}" == "1" || "$(date +%d)" == "01" ]]; then keep_copy monthly; fi
fi

rotate() { # keep the newest N dumps in a folder
  local dir="$1" keep="$2"
  local old
  while read -r old; do
    [[ -n "$old" ]] || continue
    rm -f "$old" "$old.sha256"; log "rotated out $old"
  done < <(ls -1t "$dir"/*.dump 2>/dev/null | tail -n +"$((keep + 1))" || true)
}
rotate "$BACKUP_DIR/daily" "$KEEP_DAILY"
rotate "$BACKUP_DIR/weekly" "$KEEP_WEEKLY"
rotate "$BACKUP_DIR/monthly" "$KEEP_MONTHLY"
rotate "$BACKUP_DIR/predeploy" "$KEEP_PREDEPLOY"

if [[ -n "${BACKUP_REMOTE:-}" ]]; then
  if rsync -a --delete -e "ssh -o BatchMode=yes -o StrictHostKeyChecking=accept-new" "$BACKUP_DIR/" "$BACKUP_REMOTE/"; then
    log "copied to $BACKUP_REMOTE"
  else
    fail "off-server copy to $BACKUP_REMOTE (the local backup is fine)"
  fi
else
  log "WARNING: no BACKUP_REMOTE set — backups exist only on this server"
fi
log "done"
