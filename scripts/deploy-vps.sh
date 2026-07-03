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

step "Check external PostgreSQL connectivity"
docker run --rm \
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

step "Build and start Docker stack"
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" down --remove-orphans || true
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --build \
  || fail "docker compose up failed."

step "Wait for services and verify health"
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps

for i in {1..30}; do
  if curl -kfsS https://localhost/health >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

if ! curl -kfsS https://localhost/health >/dev/null 2>&1; then
  echo "API health endpoint is not ready yet. Showing API logs:"
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" logs --tail=120 api
  fail "Health check failed."
fi

step "Deployment completed"
echo "VPS deployment is up."
echo "App URL: https://<your-domain-or-vps-ip>"
echo "Health:  https://localhost/health (from VPS shell)"
echo
echo "Useful commands:"
echo "  docker compose --env-file $ENV_FILE -f $COMPOSE_FILE ps"
echo "  docker compose --env-file $ENV_FILE -f $COMPOSE_FILE logs -f api"
echo "  docker compose --env-file $ENV_FILE -f $COMPOSE_FILE logs -f nginx"
