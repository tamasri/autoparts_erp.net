#!/usr/bin/env bash
# Puts the app on a domain with a real (Let's Encrypt) certificate that renews itself.
#
#   bash scripts/setup-domain.sh almajdauto.com [email-for-expiry-notices]
#
# The app is served at https://<domain>/erp/ (nginx.conf, vite base); the domain root redirects there.
#
# Before running: the domain's A records (@ and www) point to this server, and scripts/deploy-vps.sh has run once
# with the nginx.conf that serves /.well-known/acme-challenge/ (it is in the repository).
# Safe to run again: it keeps a valid certificate, re-copies it, and re-writes the renewal hook.
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

DOMAIN="${1:-}"
EMAIL="${2:-}"
ENV_FILE=".env.vps"
COMPOSE=(docker compose --env-file "$ENV_FILE" -f docker-compose.vps.yml)
WEBROOT="$ROOT_DIR/nginx/acme"
CERT_DIR="$ROOT_DIR/nginx/certs"
HOOK=/etc/letsencrypt/renewal-hooks/deploy/autoparts-erp.sh

fail() { echo "[ERROR] $1" >&2; exit 1; }
step() { echo; echo "== $1"; }

[[ -n "$DOMAIN" ]] || fail "Usage: bash scripts/setup-domain.sh <domain> [email]"
[[ "$DOMAIN" =~ ^[a-z0-9.-]+\.[a-z]{2,}$ ]] || fail "'$DOMAIN' is not a domain name (lower case, no https://)."
[[ $EUID -eq 0 ]] || fail "Run as root."
[[ -f "$ENV_FILE" ]] || fail "$ENV_FILE not found: run scripts/deploy-vps.sh first."
grep -q "acme-challenge" nginx/nginx.conf || fail "nginx/nginx.conf has no acme-challenge location: git pull first."

step "DNS: $DOMAIN and www.$DOMAIN must point to this server"
# SKIP_DNS_CHECK=1 when the public address is not on a local interface (NAT); the challenge probe below still checks.
local_ips=" $(hostname -I) "
for name in "$DOMAIN" "www.$DOMAIN"; do
  resolved=$(getent ahostsv4 "$name" | awk '{print $1}' | sort -u | tr '\n' ' ')
  [[ -n "$resolved" ]] || fail "$name does not resolve yet. Add the A record and wait a few minutes."
  if [[ "${SKIP_DNS_CHECK:-0}" != "1" ]]; then
    for ip in $resolved; do
      [[ "$local_ips" == *" $ip "* ]] || fail "$name resolves to $ip, which is not this server ($local_ips). Delete the old A records (the registrar's parking addresses) and keep only this server's address; then wait for the TTL."
    done
  fi
  echo "$name -> $resolved OK"
done

step "nginx with the challenge folder"
mkdir -p "$WEBROOT" "$CERT_DIR"
"${COMPOSE[@]}" up -d nginx
probe="probe-$(openssl rand -hex 6)"
mkdir -p "$WEBROOT/.well-known/acme-challenge"
echo ok > "$WEBROOT/.well-known/acme-challenge/$probe"
sleep 2
got=$(curl -fsS "http://$DOMAIN/.well-known/acme-challenge/$probe" 2>/dev/null || true)
rm -f "$WEBROOT/.well-known/acme-challenge/$probe"
[[ "$got" == "ok" ]] || fail "http://$DOMAIN/.well-known/acme-challenge/ is not served by this nginx (port 80 closed in ufw or at the provider?)."
echo "Challenge folder reachable over http://$DOMAIN"

step "certbot"
if ! command -v certbot >/dev/null 2>&1; then
  apt-get update -qq && apt-get install -y -qq certbot
fi

# The hook runs after every successful issue/renewal: copies the certificate where nginx reads it and reloads nginx.
mkdir -p "$(dirname "$HOOK")"
cat > "$HOOK" <<HOOK
#!/usr/bin/env bash
# Managed by scripts/setup-domain.sh - copies the renewed certificate for the app's nginx and reloads it.
set -e
live=/etc/letsencrypt/live/$DOMAIN
[[ -z "\${RENEWED_LINEAGE:-}" || "\$RENEWED_LINEAGE" == "\$live" ]] || exit 0
install -m 644 "\$live/fullchain.pem" "$CERT_DIR/cert.pem"
install -m 600 "\$live/privkey.pem" "$CERT_DIR/key.pem"
cd "$ROOT_DIR" && docker compose --env-file "$ENV_FILE" -f docker-compose.vps.yml exec -T nginx nginx -s reload
HOOK
chmod 755 "$HOOK"

account=(--register-unsafely-without-email)
[[ -n "$EMAIL" ]] && account=(--email "$EMAIL" --no-eff-email)
certbot certonly --webroot -w "$WEBROOT" -d "$DOMAIN" -d "www.$DOMAIN" \
  --cert-name "$DOMAIN" --keep-until-expiring --agree-tos --non-interactive "${account[@]}"

# certbot runs the deploy hook only when it issued something; run it anyway so a kept certificate is in place too.
RENEWED_LINEAGE="/etc/letsencrypt/live/$DOMAIN" bash "$HOOK"
openssl x509 -in "$CERT_DIR/cert.pem" -noout -subject -issuer -enddate

step "Allowed origins (CORS) and links printed on documents"
current=$(grep -E '^ALLOWED_ORIGINS=' "$ENV_FILE" | cut -d= -f2- | tr -d '"' || true)
origins="https://$DOMAIN,https://www.$DOMAIN"
IFS=',' read -ra existing <<<"$current"
for o in "${existing[@]}"; do
  o="${o// /}"
  [[ -z "$o" || "$o" == *your-domain* || ",$origins," == *",$o,"* ]] || origins="$origins,$o"
done
if grep -q '^ALLOWED_ORIGINS=' "$ENV_FILE"; then
  sed -i "s|^ALLOWED_ORIGINS=.*|ALLOWED_ORIGINS=$origins|" "$ENV_FILE"
else
  echo "ALLOWED_ORIGINS=$origins" >> "$ENV_FILE"
fi
echo "ALLOWED_ORIGINS=$origins"
public_url="https://$DOMAIN/erp"
if grep -q '^APP_PUBLIC_URL=' "$ENV_FILE"; then
  sed -i "s|^APP_PUBLIC_URL=.*|APP_PUBLIC_URL=$public_url|" "$ENV_FILE"
else
  echo "APP_PUBLIC_URL=$public_url" >> "$ENV_FILE"
fi
echo "APP_PUBLIC_URL=$public_url (links and QR codes on printed documents)"
"${COMPOSE[@]}" up -d api

step "Renewal"
systemctl enable --now certbot.timer >/dev/null 2>&1 || true
systemctl list-timers certbot.timer --no-pager 2>/dev/null | sed -n 1,2p || true
certbot renew --dry-run --cert-name "$DOMAIN" >/dev/null 2>&1 && echo "Renewal dry run: OK" || echo "[WARN] Renewal dry run failed: run 'certbot renew --dry-run' to see why."

step "Check"
sleep 3
code=$(curl -sS -o /dev/null -w '%{http_code}' "https://$DOMAIN/erp/" || true)
echo "https://$DOMAIN/erp/ -> HTTP $code (a trusted certificate, or curl would have refused)"
echo
echo "Done. Open https://$DOMAIN/erp"
