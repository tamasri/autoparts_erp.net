#!/bin/sh
# autoparts-backup [schedule|now]
#   schedule (default): run every day at BACKUP_TIME (HH:MM, server time zone TZ), forever.
#   now: one backup, then exit (docker exec <backup container> autoparts-backup now).
#
# App database: pg_dump -Fc, checked with pg_restore --list before it replaces anything.
# ERPNext: mariadb-dump of the site database (credentials from the site's own site_config.json) + its public/private files.
# Rotation: KEEP_DAILY days; the Sunday copy is kept KEEP_WEEKLY weeks, the 1st-of-month copy KEEP_MONTHLY months.
set -eu

BACKUP_DIR=/backups
SITES=/erpnext-sites
SITE="${ERPNEXT_SITE:-frontend}"
KEEP_DAILY="${KEEP_DAILY:-14}"
KEEP_WEEKLY="${KEEP_WEEKLY:-8}"
KEEP_MONTHLY="${KEEP_MONTHLY:-12}"

log() { echo "[backup $(date '+%Y-%m-%d %H:%M:%S')] $*"; }

rotate() { # dir keep_days
  find "$1" -maxdepth 1 -type f -mtime "+$2" -delete 2>/dev/null || true
}

promote() { # file: copy the day's file into weekly/monthly when due
  [ "$(date +%u)" = 7 ] && cp "$1" "$BACKUP_DIR/weekly/" || true
  [ "$(date +%d)" = 01 ] && cp "$1" "$BACKUP_DIR/monthly/" || true
}

backup_app() {
  stamp="$1"
  out="$BACKUP_DIR/daily/app_${stamp}.dump"
  tmp="$out.partial"
  PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h "${POSTGRES_HOST:-postgres}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -Fc -Z 6 --no-owner --no-privileges -f "$tmp"
  objects=$(pg_restore --list "$tmp" | grep -vc '^;' || true)
  if [ "${objects:-0}" -lt 50 ]; then rm -f "$tmp"; log "FAILED: app dump lists only ${objects:-0} objects"; return 1; fi
  mv "$tmp" "$out"
  promote "$out"
  log "app database: $(du -h "$out" | cut -f1), $objects objects → $out"
}

backup_erpnext() {
  stamp="$1"
  config="$SITES/$SITE/site_config.json"
  if [ ! -f "$config" ]; then log "ERPNext: $config not found, skipped"; return 0; fi
  db=$(jq -r '.db_name' "$config"); user=$(jq -r '.db_user // .db_name' "$config"); pass=$(jq -r '.db_password' "$config")
  out="$BACKUP_DIR/daily/erpnext_${stamp}.sql.gz"
  MYSQL_PWD="$pass" mariadb-dump -h "${ERPNEXT_DB_HOST:-erpnext-db}" -u "$user" --single-transaction --quick \
    --routines --skip-lock-tables "$db" | gzip -6 > "$out.partial"
  if ! gzip -t "$out.partial" || [ "$(stat -c %s "$out.partial")" -lt 100000 ]; then rm -f "$out.partial"; log "FAILED: ERPNext dump is empty or broken"; return 1; fi
  mv "$out.partial" "$out"
  files="$BACKUP_DIR/daily/erpnext_${stamp}_files.tar.gz"
  tar -C "$SITES/$SITE" -czf "$files.partial" public/files private/files 2>/dev/null || tar -C "$SITES/$SITE" -czf "$files.partial" --files-from /dev/null
  mv "$files.partial" "$files"
  promote "$out"; promote "$files"
  log "ERPNext: database $(du -h "$out" | cut -f1), files $(du -h "$files" | cut -f1)"
}

run_once() {
  umask 077
  mkdir -p "$BACKUP_DIR/daily" "$BACKUP_DIR/weekly" "$BACKUP_DIR/monthly"
  stamp=$(date '+%Y-%m-%d_%H%M%S')
  status=0
  backup_app "$stamp" || status=1
  backup_erpnext "$stamp" || status=1
  rotate "$BACKUP_DIR/daily" "$KEEP_DAILY"
  rotate "$BACKUP_DIR/weekly" "$((KEEP_WEEKLY * 7))"
  rotate "$BACKUP_DIR/monthly" "$((KEEP_MONTHLY * 31))"
  [ "$status" = 0 ] && log "done" || log "finished WITH ERRORS"
  return "$status"
}

case "${1:-schedule}" in
  now) run_once ;;
  schedule)
    at="${BACKUP_TIME:-02:30}"
    log "scheduled daily at $at (${TZ:-UTC}); files in $BACKUP_DIR (host: ${BACKUP_HOST_DIR:-/var/backups/autoparts-erp})"
    while true; do
      now=$(date +%s)
      next=$(date -d "$(date +%Y-%m-%d) $at" +%s 2>/dev/null || date -D '%Y-%m-%d %H:%M' -d "$(date +%Y-%m-%d) $at" +%s)
      [ "$next" -le "$now" ] && next=$((next + 86400))
      sleep $((next - now))
      run_once || true
    done
    ;;
  *) echo "usage: autoparts-backup [schedule|now]" >&2; exit 2 ;;
esac
