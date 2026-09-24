#!/usr/bin/env bash
# Wipes ALL business data — in this system's database AND in the ERPNext company — so real work starts from empty books.
# Kept: SYSTEM_ADMIN accounts (other users too with --keep-users), roles and permissions, reason codes, KPI definitions,
# categories, built-in entry types, ERPNext's chart of accounts / cost centres / warehouses / company.
# Deleted: items, customers, suppliers, sales reps, warehouses, stock, invoices, payments, journal entries, approvals, audit log,
# and in ERPNext every transaction of the company plus items, item prices, customers, suppliers and sales persons.
#
# No backup is taken (the data being removed is trial data). Take one first if in doubt: bash scripts/backup-db.sh
#
# Usage (on the server, after deploying the version that contains this script):
#   cd /erp && bash scripts/reset-business-data.sh               # shows what would go, asks for a typed confirmation
#   cd /erp && bash scripts/reset-business-data.sh --skip-erpnext   # only this database
#   cd /erp && bash scripts/reset-business-data.sh --keep-users     # keep every user account
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

COMPOSE=(docker compose --env-file .env.vps -f docker-compose.vps.yml)
PHRASE="DELETE-ALL-BUSINESS-DATA"
EXTRA=()
for arg in "$@"; do
  case "$arg" in
    --skip-erpnext|--keep-users) EXTRA+=("$arg") ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

[[ -f .env.vps ]] || { echo "[ERROR] .env.vps not found — run from the deployment folder." >&2; exit 1; }

echo "== What would be deleted (dry run, nothing is changed) =="
"${COMPOSE[@]}" run --rm --no-deps api --reset-business-data --dry-run "${EXTRA[@]}"

echo
echo "This deletes the data listed above for good. There is no undo."
read -r -p "Type ${PHRASE} to continue: " answer
[[ "$answer" == "$PHRASE" ]] || { echo "Cancelled — nothing was changed."; exit 1; }

started_at_stop=$("${COMPOSE[@]}" --profile whatsapp ps --services --status running | tr '\n' ' ')
echo "== Stopping the API (and WhatsApp gateway) so nothing writes during the reset =="
"${COMPOSE[@]}" --profile whatsapp stop api whatsapp || true

set +e
"${COMPOSE[@]}" run --rm --no-deps api --reset-business-data "--confirm=${PHRASE}" "${EXTRA[@]}"
status=$?
set -e

echo "== Starting the services again =="
"${COMPOSE[@]}" up -d
if [[ "$started_at_stop" == *whatsapp* ]]; then
  "${COMPOSE[@]}" --profile whatsapp up -d whatsapp
fi

if [[ $status -ne 0 ]]; then
  echo "[ERROR] The reset stopped (exit $status) — see the [reset] lines above. It is safe to run again." >&2
  exit $status
fi
echo "Reset finished. Sign in with the SYSTEM_ADMIN account and start by creating the warehouses."
