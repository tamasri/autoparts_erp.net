#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

COMPOSE_FILE="docker-compose.vps.yml"
ENV_TEMPLATE=".env.vps.template"
ENV_FILE=".env.vps"
CERT_DIR="nginx/certs"
CERT_FILE="$CERT_DIR/cert.pem"
KEY_FILE="$CERT_DIR/key.pem"

STEP=0

step() {
  STEP=$((STEP + 1))
  echo
  echo "=================================================================="
  echo "STEP $STEP: $1"
  echo "=================================================================="
}

warn() {
  echo "[WARN] $1"
}

fail() {
  echo "[ERROR] $1" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

upsert_env() {
  local key="$1"
  local value="$2"
  if grep -q "^${key}=" "$ENV_FILE"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$ENV_FILE"
  else
    echo "${key}=${value}" >> "$ENV_FILE"
  fi
}

is_placeholder() {
  local value="${1:-}"
  [[ -z "$value" || "$value" == change_me* || "$value" == your-* || "$value" == base64_* ]]
}

b64() {
  printf '%s' "$1" | base64 | tr -d '\n'
}

step "Validate prerequisites"
require_cmd docker
require_cmd openssl
require_cmd curl
docker compose version >/dev/null 2>&1 || fail "docker compose plugin is required."
docker info >/dev/null 2>&1 || fail "Docker daemon is not running."

[[ -f "$COMPOSE_FILE" ]] || fail "$COMPOSE_FILE not found."
[[ -f "$ENV_TEMPLATE" ]] || fail "$ENV_TEMPLATE not found."
[[ -f "Dockerfile" ]] || fail "Dockerfile not found."
[[ -f "nginx/nginx.conf" ]] || fail "nginx/nginx.conf not found."

step "Prepare environment file (.env.vps)"
if [[ ! -f "$ENV_FILE" ]]; then
  cp "$ENV_TEMPLATE" "$ENV_FILE"
  echo "Created $ENV_FILE from template."
fi
chmod 600 "$ENV_FILE"

set -a
source "$ENV_FILE"
set +a

required_vars=(
  POSTGRES_HOST
  POSTGRES_PORT
  POSTGRES_DB
  POSTGRES_USER
  POSTGRES_PASSWORD
  REDIS_PASSWORD
  ALLOWED_ORIGINS
  SEED_ADMIN_EMAIL
  SEED_ADMIN_USERNAME
)

for key in "${required_vars[@]}"; do
  value="${!key:-}"
  if is_placeholder "$value"; then
    fail "Variable $key is missing/placeholder in $ENV_FILE."
  fi
done

if [[ "${POSTGRES_HOST}" == "localhost" || "${POSTGRES_HOST}" == "127.0.0.1" ]]; then
  warn "POSTGRES_HOST=${POSTGRES_HOST}. From containers this points to the container itself."
  warn "If PostgreSQL is on VPS host, use POSTGRES_HOST=host.docker.internal."
fi

step "Ensure JWT keys exist"
if is_placeholder "${JWT_PRIVATE_KEY:-}" || is_placeholder "${JWT_PUBLIC_KEY:-}"; then
  echo "Generating RSA key pair for JWT..."
  private_pem="$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null)"
  public_pem="$(printf '%s' "$private_pem" | openssl rsa -pubout 2>/dev/null)"
  private_b64="$(b64 "$private_pem")"
  public_b64="$(b64 "$public_pem")"
  upsert_env "JWT_PRIVATE_KEY" "$private_b64"
  upsert_env "JWT_PUBLIC_KEY" "$public_b64"
  echo "JWT keys generated and written to $ENV_FILE"
  set -a
  source "$ENV_FILE"
  set +a
else
  echo "JWT keys already provided."
fi

step "Ensure seed admin password exists"
seed_admin_generated=0
if is_placeholder "${SEED_ADMIN_PASSWORD:-}"; then
  generated_password="$(openssl rand -base64 24 | tr -d '=+/' | cut -c1-20)Aa1!"
  upsert_env "SEED_ADMIN_PASSWORD" "$generated_password"
  seed_admin_generated=1
  echo "Generated a random SEED_ADMIN_PASSWORD and wrote it to $ENV_FILE."
  set -a
  source "$ENV_FILE"
  set +a
else
  echo "Seed admin password already provided."
fi

step "WhatsApp assistant settings"
# The gateway authenticates to the API with this shared secret (never leaves the server). Generated once, kept in .env.vps.
if is_placeholder "${ASSISTANT_GATEWAY_SECRET:-}"; then
  upsert_env "ASSISTANT_GATEWAY_SECRET" "$(openssl rand -hex 32)"
  set -a; source "$ENV_FILE"; set +a
  echo "Generated ASSISTANT_GATEWAY_SECRET."
fi
if [[ "${WHATSAPP_ENABLED:-false}" == "true" ]]; then
  export COMPOSE_PROFILES=whatsapp
  echo "WhatsApp gateway: enabled (pair it from Settings → WhatsApp assistant)."
  [[ -n "${AI_API_KEY:-}" ]] || warn "AI_API_KEY is empty: the assistant will use keyword rules only (no language model)."
else
  echo "WhatsApp gateway: disabled (set WHATSAPP_ENABLED=true in $ENV_FILE to run it)."
fi

step "Check external PostgreSQL connectivity"
# --add-host is required on native Linux Docker Engine: unlike Docker Desktop,
# host.docker.internal is not resolvable by default in a plain `docker run`
# container. The api service in docker-compose.vps.yml already gets this via
# its own `extra_hosts`, so without it here this check gives a false negative
# even when POSTGRES_HOST=host.docker.internal is correctly configured.
docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -e PGPASSWORD="$POSTGRES_PASSWORD" \
  postgres:16-alpine \
  sh -lc "pg_isready -h '$POSTGRES_HOST' -p '$POSTGRES_PORT' -U '$POSTGRES_USER' -d '$POSTGRES_DB'" \
  || fail "PostgreSQL is unreachable with provided credentials."
echo "PostgreSQL connectivity check passed."

step "Prepare TLS certificate files for Nginx"
mkdir -p "$CERT_DIR"
if [[ ! -f "$CERT_FILE" || ! -f "$KEY_FILE" ]]; then
  cert_cn="$(echo "$ALLOWED_ORIGINS" | awk -F',' '{print $1}' | sed -E 's#https?://##; s#/.*##')"
  cert_cn="${cert_cn:-localhost}"
  echo "Generating self-signed cert for CN=$cert_cn ..."
  openssl req -x509 -nodes -newkey rsa:2048 \
    -keyout "$KEY_FILE" \
    -out "$CERT_FILE" \
    -days 365 \
    -subj "/CN=$cert_cn" >/dev/null 2>&1
  echo "Self-signed certificate generated under $CERT_DIR"
else
  echo "TLS cert files already exist."
fi

step "Build frontend static assets (frontend/dist)"
docker run --rm \
  -v "$ROOT_DIR/frontend:/app" \
  -w /app \
  node:20-alpine \
  sh -lc "npm ci || npm install; npm run build" \
  || fail "Frontend build failed."
[[ -d "frontend/dist" ]] || fail "frontend/dist not found after build."
echo "Frontend build completed."

step "Back up the database before migrations run"
# The API applies migrations on start-up, so this is the last moment the database is in its previous state.
# SKIP_PREDEPLOY_BACKUP=1 skips it (only for an emergency redeploy when the backup itself is what is broken).
has_schema=$(PGPASSWORD="$POSTGRES_PASSWORD" docker run --rm --add-host=host.docker.internal:host-gateway -e PGPASSWORD postgres:16-alpine \
  psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "select to_regclass('public.invoices') is not null" 2>/dev/null || echo f)
if [[ "${SKIP_PREDEPLOY_BACKUP:-0}" == "1" ]]; then
  warn "Pre-deploy backup skipped (SKIP_PREDEPLOY_BACKUP=1)."
elif [[ "$has_schema" != "t" ]]; then
  echo "Empty database (first install) - nothing to back up yet."
else
  ENV_FILE="$ROOT_DIR/$ENV_FILE" bash "$ROOT_DIR/scripts/backup-db.sh" predeploy \
    || fail "Pre-deploy backup failed; nothing was changed. Fix it, or rerun with SKIP_PREDEPLOY_BACKUP=1 if you accept deploying without one."
fi

step "Build and start Docker stack"
# --profile whatsapp on "down" so a gateway that was switched off is stopped too; "up" starts it only when COMPOSE_PROFILES says so.
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" --profile whatsapp down --remove-orphans || true
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --build \
  || fail "docker compose up failed."

step "Wait for services and verify health"
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps

# Check the api container directly rather than through nginx: nginx.conf
# intentionally restricts `location /health` to 127.0.0.1 as a hardening
# measure, but a curl from the VPS host to the published port does not
# appear as 127.0.0.1 inside the nginx container (Docker rewrites the
# source address for published-port traffic), so it would always 403 here
# even when the api is genuinely healthy.
api_healthy=0
for i in {1..30}; do
  if docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" exec -T api wget -qO- http://localhost:8080/health >/dev/null 2>&1; then
    api_healthy=1
    break
  fi
  sleep 2
done

if [[ "$api_healthy" != "1" ]]; then
  echo "API health endpoint is not ready yet. Showing API logs:"
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" logs --tail=120 api
  fail "Health check failed."
fi
echo "API is healthy."

# Sanity-check the public HTTPS edge (frontend root) separately - this does
# NOT depend on the api's own health, just that nginx + TLS + static files
# are serving.
if curl -kfsS -o /dev/null https://localhost/ 2>/dev/null; then
  echo "Nginx HTTPS edge is responding."
else
  warn "Nginx HTTPS edge (https://localhost/) did not respond - check 'docker compose logs nginx' and nginx/certs/."
fi

step "Schedule database backups"
# Written on every deploy so the schedule always follows this checkout's path. Times are the server's local time.
BACKUP_LOG=/var/log/autoparts-erp-backup.log
cat > /etc/cron.d/autoparts-erp-backup <<CRON
# Managed by scripts/deploy-vps.sh - edit there, not here.
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
# Daily backup at 02:30 (weekly copy on Sunday, monthly on the 1st; rotation in backup-db.sh)
30 2 * * * root bash $ROOT_DIR/scripts/backup-db.sh daily >> $BACKUP_LOG 2>&1
# Weekly proof that the newest backup restores (throw-away container), Sunday 04:15
15 4 * * 0 root bash $ROOT_DIR/scripts/restore-check.sh >> $BACKUP_LOG 2>&1
CRON
chmod 644 /etc/cron.d/autoparts-erp-backup
cat > /etc/logrotate.d/autoparts-erp-backup <<ROT
$BACKUP_LOG {
  weekly
  rotate 12
  compress
  missingok
  notifempty
}
ROT
echo "Backups scheduled: daily 02:30, restore check Sundays 04:15, log $BACKUP_LOG, files ${BACKUP_DIR:-/var/backups/autoparts-erp}."
[[ -n "${BACKUP_REMOTE:-}" ]] || warn "BACKUP_REMOTE is not set in $ENV_FILE: backups stay on this server only (a lost server loses them too)."

step "Deployment completed"
echo "VPS deployment is up."
echo "App URL: https://<your-domain-or-vps-ip>"
echo "Health:  https://localhost/health (from VPS shell)"
echo
if [[ "$seed_admin_generated" == "1" ]]; then
  echo "!! Save this admin login now - it will not be shown again !!"
  echo "   Email:    ${SEED_ADMIN_EMAIL}"
  echo "   Password: ${SEED_ADMIN_PASSWORD}"
  echo
fi
echo "Useful commands:"
echo "  docker compose --env-file $ENV_FILE -f $COMPOSE_FILE ps"
echo "  docker compose --env-file $ENV_FILE -f $COMPOSE_FILE logs -f api"
echo "  docker compose --env-file $ENV_FILE -f $COMPOSE_FILE logs -f nginx"
