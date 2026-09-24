#!/usr/bin/env bash
# Full read-only health check of the server: host, Docker, the API, Redis, PostgreSQL (server health and the books' integrity),
# ERPNext (through its API, plus bench if it runs in Docker here), backups and security. It changes nothing.
#
# Usage (on the server):
#   cd /erp && bash scripts/diagnose.sh                       # everything
#   cd /erp && bash scripts/diagnose.sh db erpnext            # only some sections: system docker app redis db erpnext backup security
#   SINCE=72h bash scripts/diagnose.sh                        # log window (default 24h)
#
# The report is printed and saved to /var/log/autoparts-erp-diagnose-<time>.txt. Every value of a secret in .env.vps
# (passwords, keys, tokens) is replaced by *** before anything is printed or saved, so the report can be shared.
# Each finding is also listed in the SUMMARY at the end: FAIL = broken, WARN = needs a look, INFO = worth knowing.
#
# Test hook: DIAG_PSQL="docker exec -i <container> psql -U <user> -d <db>" runs the db section against another database.
set -Euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR" || exit 1

SINCE="${SINCE:-24h}"
SECTIONS=("$@")
[[ ${#SECTIONS[@]} -gt 0 ]] || SECTIONS=(system docker app redis db erpnext backup security)
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT_DIR=/var/log; [[ -w "$OUT_DIR" ]] || OUT_DIR="${TMPDIR:-/tmp}"
OUT="$OUT_DIR/autoparts-erp-diagnose-$STAMP.txt"
COMPOSE=(docker compose --env-file .env.vps -f docker-compose.vps.yml)
PSQL_BOX="erp-diagnose-psql-$$"
SECRETS_FILE="$(mktemp)"; chmod 600 "$SECRETS_FILE"
FINDINGS_FILE="$(mktemp)"

cleanup() {
  docker rm -f "$PSQL_BOX" >/dev/null 2>&1 || true
  rm -f "$SECRETS_FILE" "$FINDINGS_FILE"
}
trap cleanup EXIT

if [[ -f .env.vps ]]; then
  set -a; source .env.vps; set +a
  # Every non-trivial value of a secret-looking variable is masked in the output.
  grep -E '^[A-Z0-9_]*(PASSWORD|SECRET|KEY|TOKEN)[A-Z0-9_]*=' .env.vps | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//' \
    | awk 'length($0) >= 6' > "$SECRETS_FILE" || true
fi

redact() {
  awk -v file="$SECRETS_FILE" '
    BEGIN { n = 0; while ((getline s < file) > 0) if (length(s) >= 6) sec[n++] = s }
    { line = $0
      for (i = 0; i < n; i++) { while ((p = index(line, sec[i])) > 0) line = substr(line, 1, p - 1) "***" substr(line, p + length(sec[i])) }
      print line; fflush() }'
}

want() { local s; for s in "${SECTIONS[@]}"; do [[ "$s" == "$1" ]] && return 0; done; return 1; }
section() { printf '\n\n########################################################################\n# %s\n########################################################################\n' "$1"; }
sub() { printf '\n==== %s ====\n' "$1"; }
flag() { printf '[%s] %s\n' "$1" "$2" >> "$FINDINGS_FILE"; printf '  >> %s: %s\n' "$1" "$2"; }
run() { printf '\n$ %s\n' "$*"; "$@" 2>&1 || printf '  (exit %s)\n' "$?"; }
have() { command -v "$1" >/dev/null 2>&1; }

# ---------------------------------------------------------------------------------------------------------------------- system
system_section() {
  section "1. HOST"
  run date -u
  run uname -a
  [[ -r /etc/os-release ]] && run grep -E '^(PRETTY_NAME|VERSION_ID)=' /etc/os-release
  run uptime
  run nproc
  run free -h
  run df -hT -x tmpfs -x devtmpfs -x overlay
  run df -i -x tmpfs -x devtmpfs -x overlay
  local used
  while read -r used mount; do
    used="${used%\%}"
    [[ "$used" =~ ^[0-9]+$ ]] || continue
    if (( used >= 90 )); then flag FAIL "disk $mount is ${used}% full"; elif (( used >= 80 )); then flag WARN "disk $mount is ${used}% full"; fi
  done < <(df -P -x tmpfs -x devtmpfs -x overlay 2>/dev/null | awk 'NR>1 {print $5, $6}')
  local avail_mb; avail_mb=$(free -m | awk '/^Mem:/ {print $7}')
  [[ -n "$avail_mb" && "$avail_mb" -lt 300 ]] && flag WARN "only ${avail_mb} MB memory available"
  run swapon --show
  sub "Load and top processes"
  run sh -c 'ps -eo pid,user,%cpu,%mem,rss,etime,comm --sort=-%mem | head -15'
  sub "Clock"
  have timedatectl && run timedatectl status
  sub "Out-of-memory kills and kernel errors (last 7 days)"
  if have journalctl; then
    run sh -c 'journalctl -k --since "7 days ago" --no-pager 2>/dev/null | grep -Ei "out of memory|oom-kill|killed process|i/o error|ext4-fs error|segfault" | tail -20'
    local ooms; ooms=$(journalctl -k --since "7 days ago" --no-pager 2>/dev/null | grep -ciE 'oom-kill|killed process' || true)
    [[ "${ooms:-0}" -gt 0 ]] && flag WARN "$ooms out-of-memory kill(s) in the last 7 days"
  else
    run sh -c 'dmesg -T 2>/dev/null | grep -Ei "out of memory|oom|i/o error" | tail -20'
  fi
  sub "Failed systemd units"
  if have systemctl; then
    run systemctl --failed --no-pager
    local failed; failed=$(systemctl --failed --no-legend 2>/dev/null | wc -l)
    [[ "$failed" -gt 0 ]] && flag WARN "$failed failed systemd unit(s)"
  fi
  sub "Listening ports"
  have ss && run ss -tulpn
  sub "Reboot / updates"
  [[ -f /var/run/reboot-required ]] && flag WARN "a reboot is required (kernel or libraries updated)"
  if have apt; then
    local upg; upg=$(apt list --upgradable 2>/dev/null | grep -c upgradable || true)
    local sec; sec=$(apt list --upgradable 2>/dev/null | grep -ci security || true)
    echo "upgradable packages: $upg (security: $sec)"
    [[ "${sec:-0}" -gt 0 ]] && flag WARN "$sec security update(s) pending"
  fi
}

# ---------------------------------------------------------------------------------------------------------------------- docker
docker_section() {
  section "2. DOCKER"
  run docker version --format 'server {{.Server.Version}}, client {{.Client.Version}}'
  run docker compose version
  run docker system df
  sub "All containers on this host"
  run docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.RunningFor}}'
  sub "This application's services"
  run "${COMPOSE[@]}" --profile whatsapp ps -a
  local name state health restarts
  while read -r name; do
    [[ -z "$name" ]] && continue
    state=$(docker inspect -f '{{.State.Status}}' "$name" 2>/dev/null)
    health=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$name" 2>/dev/null)
    restarts=$(docker inspect -f '{{.RestartCount}}' "$name" 2>/dev/null)
    echo "$name: state=$state health=${health:-n/a} restarts=$restarts oom=$(docker inspect -f '{{.State.OOMKilled}}' "$name" 2>/dev/null)"
    [[ "$state" != "running" ]] && flag FAIL "container $name is $state"
    [[ "$health" == "unhealthy" ]] && flag FAIL "container $name is unhealthy"
    [[ "${restarts:-0}" -gt 3 ]] && flag WARN "container $name restarted $restarts times"
  done < <("${COMPOSE[@]}" --profile whatsapp ps -a --format '{{.Name}}' 2>/dev/null)
  sub "Resource use"
  run docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.NetIO}}\t{{.BlockIO}}'
  sub "Health check history of the api"
  local api; api=$("${COMPOSE[@]}" ps -q api 2>/dev/null)
  [[ -n "$api" ]] && run sh -c "docker inspect -f '{{range .State.Health.Log}}{{.Start}} exit={{.ExitCode}} {{.Output}}{{println}}{{end}}' $api | tail -5"
}

# ---------------------------------------------------------------------------------------------------------------------- app
app_section() {
  section "3. APPLICATION (code, configuration, API, logs)"
  sub "Deployed code"
  run git log -5 --format='%h %ci %s'
  run git status --short
  if git fetch -q origin main 2>/dev/null; then
    local behind; behind=$(git rev-list --count HEAD..origin/main 2>/dev/null || echo 0)
    echo "commits on origin/main not deployed here: $behind"
    [[ "$behind" -gt 0 ]] && flag WARN "the server is $behind commit(s) behind origin/main (git pull + deploy)"
  fi
  if [[ -d frontend/dist ]]; then
    echo "frontend/dist built: $(date -u -r frontend/dist/index.html '+%F %T' 2>/dev/null) UTC; last commit: $(git log -1 --format=%ci)"
    [[ frontend/dist/index.html -ot .git/HEAD ]] && flag WARN "frontend/dist is older than the last git change (run deploy-vps.sh to rebuild it)"
  else
    flag FAIL "frontend/dist is missing"
  fi
  local api_img; api_img=$(docker inspect -f '{{.Created}}' "$("${COMPOSE[@]}" ps -q api 2>/dev/null)" 2>/dev/null || true)
  echo "api container created: ${api_img:-?}"

  sub ".env.vps (names only; values are never shown)"
  if [[ -f .env.vps ]]; then
    echo "permissions: $(stat -c '%a %U' .env.vps)"
    [[ "$(stat -c '%a' .env.vps)" =~ ^[67]00$ ]] || flag WARN ".env.vps is readable by other users (chmod 600 .env.vps)"
    local key val state
    for key in POSTGRES_HOST POSTGRES_PORT POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD REDIS_PASSWORD JWT_PRIVATE_KEY JWT_PUBLIC_KEY \
               ALLOWED_ORIGINS SEED_ADMIN_EMAIL SEED_ADMIN_USERNAME SEED_ADMIN_PASSWORD ERPNEXT_ENABLED ERPNEXT_BASE_URL ERPNEXT_API_KEY \
               ERPNEXT_API_SECRET WHATSAPP_ENABLED ASSISTANT_GATEWAY_SECRET AI_API_KEY AI_BASE_URL AI_MODEL GOVERNANCE_ALLOW_SELF_APPROVAL \
               BACKUP_DIR BACKUP_REMOTE; do
      val="${!key:-}"
      if [[ -z "$val" ]]; then state="EMPTY"
      elif [[ "$val" == change_me* || "$val" == your-* || "$val" == base64_* ]]; then state="PLACEHOLDER"
      else state="set (${#val} chars)"; fi
      case "$key" in POSTGRES_HOST|POSTGRES_PORT|POSTGRES_DB|ERPNEXT_ENABLED|ERPNEXT_BASE_URL|WHATSAPP_ENABLED|AI_BASE_URL|AI_MODEL|GOVERNANCE_ALLOW_SELF_APPROVAL|ALLOWED_ORIGINS)
        [[ -n "$val" ]] && state="$val";; esac
      printf '  %-32s %s\n' "$key" "$state"
      [[ "$state" == PLACEHOLDER ]] && flag FAIL "$key in .env.vps is still a placeholder"
    done
    [[ "${GOVERNANCE_ALLOW_SELF_APPROVAL:-false}" == "true" ]] && flag INFO "GOVERNANCE_ALLOW_SELF_APPROVAL=true (one person can approve their own requests)"
    [[ "${ERPNEXT_ENABLED:-false}" != "true" ]] && flag WARN "ERPNEXT_ENABLED is not true — nothing is posted to ERPNext"
  else
    flag FAIL ".env.vps not found in $ROOT_DIR"
  fi

  sub "TLS certificate"
  if [[ -f nginx/certs/cert.pem ]]; then
    run openssl x509 -in nginx/certs/cert.pem -noout -subject -issuer -enddate
    if ! openssl x509 -in nginx/certs/cert.pem -noout -checkend $((14*86400)) >/dev/null 2>&1; then flag FAIL "TLS certificate expires within 14 days (or has expired)"; fi
    openssl x509 -in nginx/certs/cert.pem -noout -issuer 2>/dev/null | grep -q "$(openssl x509 -in nginx/certs/cert.pem -noout -subject 2>/dev/null | sed 's/subject=//')" \
      && flag INFO "TLS certificate is self-signed (browsers warn; see SETUP_HARDENING H-2)"
  else
    flag FAIL "nginx/certs/cert.pem is missing"
  fi

  sub "HTTP checks through nginx"
  local path code
  for path in /health /health/ready / /robots.txt /api/v1/auth/me; do
    code=$(curl -sk -o /dev/null -m 15 -w '%{http_code} %{time_total}s' "https://localhost$path" 2>&1)
    echo "  GET https://localhost$path -> $code"
  done
  code=$(curl -sk -o /dev/null -m 15 -w '%{http_code}' https://localhost/health/ready || true)
  [[ "$code" == "200" ]] || flag FAIL "https://localhost/health/ready answered $code"
  code=$(curl -sk -o /dev/null -m 15 -w '%{http_code}' https://localhost/api/v1/auth/me || true)
  [[ "$code" == "401" ]] || flag WARN "an unauthenticated /api/v1/auth/me answered $code (expected 401)"
  code=$(curl -s -o /dev/null -m 15 -w '%{http_code}' http://localhost/ || true)
  echo "  GET http://localhost/ -> $code (a redirect to https is expected)"
  sub "Security headers"
  run sh -c "curl -skI -m 15 https://localhost/ | grep -iE '^(strict-transport|content-security|x-frame|x-content-type|referrer-policy|permissions-policy|x-robots|server):'"
  sub "Health endpoint body"
  run sh -c "curl -sk -m 15 https://localhost/health/ready; echo"

  sub "API log: errors in the last $SINCE"
  local log; log=$("${COMPOSE[@]}" logs --no-color --since "$SINCE" api 2>/dev/null)
  local errs fatal warns
  errs=$(grep -c ' ERR\]' <<<"$log" || true); fatal=$(grep -c ' FTL\]' <<<"$log" || true); warns=$(grep -c ' WRN\]' <<<"$log" || true)
  echo "lines: $(wc -l <<<"$log")  errors: $errs  fatal: $fatal  warnings: $warns"
  [[ "$fatal" -gt 0 ]] && flag FAIL "$fatal fatal log line(s) in the API in the last $SINCE"
  [[ "$errs" -gt 0 ]] && flag WARN "$errs error log line(s) in the API in the last $SINCE"
  echo "-- most frequent errors / warnings (grouped, numbers and ids blanked) --"
  grep -E ' (ERR|FTL|WRN)\]' <<<"$log" | sed -E 's/^[^[]*\[[0-9:]+ //; s/[0-9a-f]{8}-[0-9a-f-]{27}/<id>/g; s/[0-9]+/N/g' | cut -c1-220 \
    | sort | uniq -c | sort -rn | head -25
  echo "-- last 40 errors with their exception lines --"
  grep -E -A6 ' (ERR|FTL)\]' <<<"$log" | grep -vE '^\s*at (Microsoft|System)\.' | tail -80
  echo "-- exception types --"
  grep -oE '[A-Za-z0-9_.]+(Exception|Error)\b:' <<<"$log" | sort | uniq -c | sort -rn | head -15
  sub "API start-up (migrations) in the last $SINCE"
  grep -E 'Applying migration|No migrations were applied|Now listening|Application started|Hangfire|Unhandled' <<<"$log" | tail -15

  sub "nginx: responses by status in the last $SINCE"
  local nlog; nlog=$("${COMPOSE[@]}" logs --no-color --since "$SINCE" nginx 2>/dev/null)
  awk '{ for (i = 1; i <= NF; i++) if ($i ~ /^HTTP\/[0-9.]+"$/) { print $(i+1); break } }' <<<"$nlog" | sort | uniq -c | sort -rn
  local n5; n5=$(awk '{ for (i = 1; i <= NF; i++) if ($i ~ /^HTTP\/[0-9.]+"$/ && $(i+1) ~ /^5/) c++ } END { print c+0 }' <<<"$nlog")
  [[ "$n5" -gt 0 ]] && flag WARN "$n5 HTTP 5xx response(s) from nginx in the last $SINCE"
  echo "-- last 5xx requests --"
  awk '{ for (i = 1; i <= NF; i++) if ($i ~ /^HTTP\/[0-9.]+"$/ && $(i+1) ~ /^5/) print }' <<<"$nlog" | tail -20
  echo "-- nginx errors --"
  grep -E '\[(error|crit|alert|emerg)\]' <<<"$nlog" | tail -20
  echo "-- busiest client addresses --"
  awk '{print $1}' <<<"$nlog" | grep -E '^[0-9a-f.:]+$' | sort | uniq -c | sort -rn | head -10
  echo "-- 429 (rate limited) --"; awk '{ for (i = 1; i <= NF; i++) if ($i ~ /^HTTP\/[0-9.]+"$/ && $(i+1) == "429") c++ } END { print c+0 }' <<<"$nlog"

  if [[ "${WHATSAPP_ENABLED:-false}" == "true" ]]; then
    sub "WhatsApp gateway log (last 30 lines)"
    "${COMPOSE[@]}" --profile whatsapp logs --no-color --tail 30 whatsapp 2>&1
  fi

  sub "From inside the api container"
  if [[ -n "$("${COMPOSE[@]}" ps -q api 2>/dev/null)" ]]; then
    run "${COMPOSE[@]}" exec -T api sh -c 'wget -qO- http://localhost:8080/health/ready; echo; df -h /app /tmp | tail -2; ls -la /app/logs 2>/dev/null | tail -5'
    if [[ "${ERPNEXT_ENABLED:-false}" == "true" && -n "${ERPNEXT_BASE_URL:-}" ]]; then
      echo "-- can the api container reach ERPNext? --"
      if "${COMPOSE[@]}" exec -T api wget -qO- -T 15 "${ERPNEXT_BASE_URL%/}/api/method/ping" 2>&1; then echo; else flag FAIL "the api container cannot reach ERPNext at ${ERPNEXT_BASE_URL}"; fi
    fi
  else
    flag FAIL "the api container is not running"
  fi
}

# ---------------------------------------------------------------------------------------------------------------------- redis
redis_section() {
  section "4. REDIS"
  if [[ -z "$("${COMPOSE[@]}" ps -q redis 2>/dev/null)" ]]; then flag FAIL "redis is not running"; return; fi
  export REDISCLI_AUTH="${REDIS_PASSWORD:-}"
  local r=("${COMPOSE[@]}" exec -T -e REDISCLI_AUTH redis redis-cli --no-auth-warning)
  local pong; pong=$("${r[@]}" ping 2>&1)
  echo "PING -> $pong"
  [[ "$pong" == *PONG* ]] || flag FAIL "redis does not answer PING ($pong)"
  run "${r[@]}" info server
  "${r[@]}" info memory 2>&1 | grep -E 'used_memory_human|used_memory_peak_human|maxmemory_human|maxmemory_policy|mem_fragmentation_ratio'
  "${r[@]}" info persistence 2>&1 | grep -E 'aof_enabled|aof_last_write_status|aof_last_bgrewrite_status|rdb_last_bgsave_status|rdb_last_save_time|loading'
  "${r[@]}" info stats 2>&1 | grep -E 'evicted_keys|expired_keys|keyspace_hits|keyspace_misses|rejected_connections|total_connections_received'
  "${r[@]}" info clients 2>&1 | grep -E 'connected_clients|blocked_clients'
  "${r[@]}" info keyspace 2>&1
  local aof; aof=$("${r[@]}" info persistence 2>/dev/null | grep -E 'aof_last_write_status|rdb_last_bgsave_status' | grep -vc ':ok' || true)
  [[ "$aof" -gt 0 ]] && flag FAIL "redis persistence reports an error (AOF/RDB status not ok)"
  "${r[@]}" info stats 2>/dev/null | grep -q '^evicted_keys:[1-9]' && flag WARN "redis evicted keys (memory limit reached)"
  echo "-- key prefixes (sample of 2000) --"
  "${r[@]}" --scan --count 1000 2>/dev/null | head -2000 | sed -E 's/[:{].*//' | sort | uniq -c | sort -rn | head -15
  sub "Redis log (errors)"
  "${COMPOSE[@]}" logs --no-color --since "$SINCE" redis 2>&1 | grep -iE 'error|warn|oom|refused|fail' | tail -15
}

# ---------------------------------------------------------------------------------------------------------------------- db
PSQL=()
psql_setup() {
  if [[ -n "${DIAG_PSQL:-}" ]]; then read -r -a PSQL <<<"$DIAG_PSQL"; PSQL+=(-X -q -P pager=off -v ON_ERROR_STOP=1); return 0; fi
  [[ -n "${POSTGRES_HOST:-}" ]] || { flag FAIL "POSTGRES_HOST is not set"; return 1; }
  export PGPASSWORD="${POSTGRES_PASSWORD:-}"
  docker run -d --rm --name "$PSQL_BOX" --add-host=host.docker.internal:host-gateway -e PGPASSWORD postgres:16-alpine sleep 1800 >/dev/null \
    || { flag FAIL "could not start a postgres:16-alpine client container"; return 1; }
  PSQL=(docker exec -i "$PSQL_BOX" psql -h "$POSTGRES_HOST" -p "${POSTGRES_PORT:-5432}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -P pager=off -v ON_ERROR_STOP=1)
}

# Prints a query's result (aligned).
q() { local title="$1" sql; sql=$(cat); printf '\n-- %s --\n' "$title"; printf '%s\n' "$sql" | "${PSQL[@]}" 2>&1; }

# A check: the query returns the offending rows (no trailing semicolon). 0 rows = OK; otherwise the level is flagged and up to 25 rows shown.
check() {
  local level="$1" title="$2" sql n
  sql=$(cat)
  n=$(printf 'SELECT count(*) FROM (\n%s\n) q;\n' "$sql" | "${PSQL[@]}" -At 2>&1)
  if [[ ! "$n" =~ ^[0-9]+$ ]]; then printf '\n-- CHECK %s: query failed --\n%s\n' "$title" "$n"; flag WARN "check could not run: $title"; return; fi
  if [[ "$n" -eq 0 ]]; then printf '\n-- CHECK %s: OK --\n' "$title"; return; fi
  printf '\n-- CHECK %s: %s row(s) --\n' "$title" "$n"
  printf 'SELECT * FROM (\n%s\n) q LIMIT 25;\n' "$sql" | "${PSQL[@]}" 2>&1
  flag "$level" "$title: $n"
}

db_section() {
  section "5. POSTGRESQL"
  psql_setup || return
  if ! printf 'SELECT 1;' | "${PSQL[@]}" -At >/dev/null 2>&1; then
    flag FAIL "cannot connect to PostgreSQL"; printf 'SELECT 1;' | "${PSQL[@]}" 2>&1; return
  fi

  sub "Server"
  q "version, size, uptime" <<'SQL'
SELECT version(), pg_size_pretty(pg_database_size(current_database())) AS db_size, now() - pg_postmaster_start_time() AS uptime,
       current_setting('max_connections') AS max_conn, current_setting('shared_buffers') AS shared_buffers,
       current_setting('work_mem') AS work_mem, current_setting('TimeZone') AS tz, current_setting('statement_timeout') AS stmt_timeout;
SQL
  q "connections by state" <<'SQL'
SELECT datname, usename, application_name, state, count(*) FROM pg_stat_activity WHERE backend_type = 'client backend' GROUP BY 1,2,3,4 ORDER BY 5 DESC;
SQL
  check WARN "queries or transactions running longer than 5 minutes" <<'SQL'
SELECT pid, usename, state, now() - xact_start AS xact_age, now() - query_start AS query_age, wait_event_type, left(query, 150) AS query
FROM pg_stat_activity WHERE backend_type = 'client backend' AND pid <> pg_backend_pid()
  AND (now() - xact_start > interval '5 minutes' OR (state = 'active' AND now() - query_start > interval '5 minutes'))
SQL
  check WARN "sessions idle inside a transaction" <<'SQL'
SELECT pid, usename, now() - state_change AS idle_for, left(query, 150) AS last_query FROM pg_stat_activity WHERE state LIKE 'idle in transaction%'
SQL
  check WARN "blocked queries (lock waits)" <<'SQL'
SELECT a.pid, pg_blocking_pids(a.pid) AS blocked_by, now() - a.query_start AS waiting, left(a.query, 150) AS query
FROM pg_stat_activity a WHERE cardinality(pg_blocking_pids(a.pid)) > 0
SQL
  q "database statistics (deadlocks, conflicts, temp files, cache hit %)" <<'SQL'
SELECT datname, numbackends, xact_commit, xact_rollback, deadlocks, conflicts, temp_files, pg_size_pretty(temp_bytes) AS temp_bytes,
       round(100.0 * blks_hit / nullif(blks_hit + blks_read, 0), 2) AS cache_hit_pct, stats_reset
FROM pg_stat_database WHERE datname = current_database();
SQL
  check INFO "transaction id age above 500 million (wraparound risk)" <<'SQL'
SELECT datname, age(datfrozenxid) AS xid_age FROM pg_database WHERE age(datfrozenxid) > 500000000
SQL
  q "largest tables" <<'SQL'
SELECT relname AS table, n_live_tup AS live_rows, n_dead_tup AS dead_rows, pg_size_pretty(pg_total_relation_size(relid)) AS total_size,
       last_autovacuum::timestamp(0), last_autoanalyze::timestamp(0)
FROM pg_stat_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 20;
SQL
  check WARN "tables with many dead rows (vacuum is behind)" <<'SQL'
SELECT schemaname, relname, n_live_tup, n_dead_tup, round(100.0 * n_dead_tup / nullif(n_live_tup + n_dead_tup, 0), 1) AS dead_pct,
       last_autovacuum, last_vacuum
FROM pg_stat_user_tables WHERE n_dead_tup > 1000 AND n_dead_tup > 0.2 * (n_live_tup + 1)
SQL
  check FAIL "invalid indexes (a failed CREATE INDEX CONCURRENTLY)" <<'SQL'
SELECT n.nspname, c.relname AS index, i.indrelid::regclass AS table FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
JOIN pg_namespace n ON n.oid = c.relnamespace WHERE NOT i.indisvalid OR NOT i.indisready
SQL
  check WARN "constraints not validated (NOT VALID — old rows are not checked)" <<'SQL'
SELECT conrelid::regclass AS table, conname, contype FROM pg_constraint WHERE NOT convalidated
SQL
  check FAIL "disabled triggers (the database's own guards are off)" <<'SQL'
SELECT tgrelid::regclass AS table, tgname, tgenabled FROM pg_trigger WHERE NOT tgisinternal AND tgenabled = 'D'
SQL
  check INFO "foreign keys without an index on the referencing column(s)" <<'SQL'
SELECT c.conrelid::regclass AS table, c.conname, pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c
WHERE c.contype = 'f' AND c.connamespace = 'public'::regnamespace
  AND NOT EXISTS (SELECT 1 FROM pg_index i WHERE i.indrelid = c.conrelid AND (i.indkey::int2[])[0:cardinality(c.conkey) - 1] @> c.conkey)
SQL
  check INFO "duplicate indexes (same table and columns)" <<'SQL'
SELECT indrelid::regclass AS table, array_agg(indexrelid::regclass) AS indexes FROM pg_index
GROUP BY indrelid, indkey, indclass, indexprs::text, indpred::text HAVING count(*) > 1
SQL
  check INFO "sequences above 50% of their maximum" <<'SQL'
SELECT schemaname, sequencename, last_value, max_value FROM pg_sequences WHERE last_value > max_value / 2
SQL
  q "extensions" <<'SQL'
SELECT extname, extversion FROM pg_extension ORDER BY 1;
SQL

  sub "Schema version"
  q "migrations applied (last 8)" <<'SQL'
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 8;
SQL
  local latest_code; latest_code=$(grep -rhoE '\[Migration\("[0-9]+_[A-Za-z0-9]+"\)\]' src/AutoPartsERP.Infrastructure 2>/dev/null | sed -E 's/.*"(.*)".*/\1/' | sort | tail -1)
  local latest_db; latest_db=$(printf 'SELECT max("MigrationId") FROM "__EFMigrationsHistory";' | "${PSQL[@]}" -At 2>/dev/null)
  echo "latest migration in the code: ${latest_code:-?}   in the database: ${latest_db:-?}"
  [[ -n "$latest_code" && "$latest_code" != "$latest_db" ]] && flag FAIL "the database is not on the latest migration (code $latest_code, db $latest_db) — the api did not start or migrate"
  q "row counts of the main tables" <<'SQL'
SELECT 'items' AS t, count(*) FROM items UNION ALL SELECT 'skus', count(*) FROM skus UNION ALL SELECT 'customers', count(*) FROM customers
UNION ALL SELECT 'parties', count(*) FROM parties UNION ALL SELECT 'invoices', count(*) FROM invoices UNION ALL SELECT 'invoice_lines', count(*) FROM invoice_lines
UNION ALL SELECT 'payments', count(*) FROM payments UNION ALL SELECT 'purchase_invoices', count(*) FROM purchase_invoices
UNION ALL SELECT 'supplier_payments', count(*) FROM supplier_payments UNION ALL SELECT 'journal_entries', count(*) FROM journal_entries
UNION ALL SELECT 'landed_cost_vouchers', count(*) FROM landed_cost_vouchers UNION ALL SELECT 'inventory_movements', count(*) FROM inventory_movements
UNION ALL SELECT 'audit_logs', count(*) FROM audit_logs UNION ALL SELECT 'outbox_messages', count(*) FROM outbox_messages
UNION ALL SELECT 'erpnext_sync_log', count(*) FROM erpnext_sync_log UNION ALL SELECT 'deleted_documents', count(*) FROM deleted_documents
UNION ALL SELECT 'users', count(*) FROM asp_net_users;
SQL

  sub "Document numbering (gapless, never reused)"
  q "series" <<'SQL'
SELECT code, prefix, last_number, updated_at::timestamp(0) FROM document_series ORDER BY code;
SQL
  check FAIL "numbers issued but neither present nor recorded as deleted" <<'SQL'
WITH live AS (
  SELECT series_code FROM invoices UNION ALL SELECT series_code FROM payments UNION ALL SELECT series_code FROM purchase_invoices
  UNION ALL SELECT series_code FROM supplier_payments UNION ALL SELECT series_code FROM journal_entries UNION ALL SELECT series_code FROM stock_adjustments
  UNION ALL SELECT series_code FROM transfer_orders UNION ALL SELECT series_code FROM receiving_documents UNION ALL SELECT series_code FROM issue_orders
  UNION ALL SELECT series_code FROM landed_cost_vouchers),
l AS (SELECT series_code, count(*) AS n FROM live GROUP BY 1), g AS (SELECT series_code, count(*) AS n FROM deleted_documents GROUP BY 1)
SELECT s.code, s.last_number, coalesce(l.n, 0) AS present, coalesce(g.n, 0) AS deleted, s.last_number - coalesce(l.n, 0) - coalesce(g.n, 0) AS unexplained
FROM document_series s LEFT JOIN l ON l.series_code = s.code LEFT JOIN g ON g.series_code = s.code
WHERE s.last_number - coalesce(l.n, 0) - coalesce(g.n, 0) <> 0
SQL
  check FAIL "serial numbers used twice in a series (present or deleted)" <<'SQL'
WITH all_numbers AS (
  SELECT series_code, serial_no FROM invoices UNION ALL SELECT series_code, serial_no FROM payments UNION ALL SELECT series_code, serial_no FROM purchase_invoices
  UNION ALL SELECT series_code, serial_no FROM supplier_payments UNION ALL SELECT series_code, serial_no FROM journal_entries
  UNION ALL SELECT series_code, serial_no FROM stock_adjustments UNION ALL SELECT series_code, serial_no FROM transfer_orders
  UNION ALL SELECT series_code, serial_no FROM receiving_documents UNION ALL SELECT series_code, serial_no FROM issue_orders
  UNION ALL SELECT series_code, serial_no FROM landed_cost_vouchers UNION ALL SELECT series_code, serial_no FROM deleted_documents)
SELECT series_code, serial_no, count(*) FROM all_numbers GROUP BY 1, 2 HAVING count(*) > 1
SQL
  check FAIL "documents whose series is unknown or serial is above the series counter" <<'SQL'
WITH all_numbers AS (
  SELECT 'invoices' AS t, series_code, serial_no FROM invoices UNION ALL SELECT 'payments', series_code, serial_no FROM payments
  UNION ALL SELECT 'purchase_invoices', series_code, serial_no FROM purchase_invoices UNION ALL SELECT 'supplier_payments', series_code, serial_no FROM supplier_payments
  UNION ALL SELECT 'journal_entries', series_code, serial_no FROM journal_entries UNION ALL SELECT 'stock_adjustments', series_code, serial_no FROM stock_adjustments
  UNION ALL SELECT 'transfer_orders', series_code, serial_no FROM transfer_orders UNION ALL SELECT 'receiving_documents', series_code, serial_no FROM receiving_documents
  UNION ALL SELECT 'issue_orders', series_code, serial_no FROM issue_orders UNION ALL SELECT 'landed_cost_vouchers', series_code, serial_no FROM landed_cost_vouchers)
SELECT a.t, a.series_code, a.serial_no, s.last_number FROM all_numbers a LEFT JOIN document_series s ON s.code = a.series_code
WHERE s.code IS NULL OR a.serial_no IS NULL OR a.serial_no > s.last_number
SQL
  check FAIL "numbering / delete-guard triggers missing on a numbered table" <<'SQL'
SELECT t.table_name, count(tr.tgname) FILTER (WHERE tr.tgname LIKE '%number%') AS numbering_triggers,
       count(tr.tgname) FILTER (WHERE tr.tgname LIKE '%delete%') AS delete_guard
FROM (VALUES ('invoices'), ('payments'), ('purchase_invoices'), ('supplier_payments'), ('journal_entries'), ('stock_adjustments'),
             ('transfer_orders'), ('receiving_documents'), ('issue_orders'), ('landed_cost_vouchers')) AS t(table_name)
LEFT JOIN pg_trigger tr ON tr.tgrelid = t.table_name::regclass AND NOT tr.tgisinternal
GROUP BY t.table_name HAVING count(tr.tgname) FILTER (WHERE tr.tgname LIKE '%number%') < 2 OR count(tr.tgname) FILTER (WHERE tr.tgname LIKE '%delete%') < 1
SQL
  q "deleted documents (latest 15)" <<'SQL'
SELECT d.document_number, d.status_at_deletion, left(d.reason, 60) AS reason, u.user_name AS deleted_by, d.deleted_at::timestamp(0)
FROM deleted_documents d LEFT JOIN asp_net_users u ON u.id = d.deleted_by ORDER BY d.deleted_at DESC LIMIT 15;
SQL

  sub "Sales: invoices, returns, payments"
  q "invoices by type and status" <<'SQL'
SELECT invoice_type, status, count(*), round(sum(total_usd), 2) AS total_usd, round(sum(balance_usd), 2) AS balance_usd FROM invoices GROUP BY 1, 2 ORDER BY 1, 2;
SQL
  check FAIL "invoice total ≠ subtotal − discount + delivery + tax" <<'SQL'
SELECT invoice_number, invoice_type, status, subtotal_usd, discount_amount_usd, delivery_fee_usd, tax_amount_usd, total_usd
FROM invoices WHERE status <> 'DRAFT'
  AND abs(abs(total_usd) - (abs(subtotal_usd) - discount_amount_usd + delivery_fee_usd + tax_amount_usd)) > 0.011
SQL
  check FAIL "invoice subtotal ≠ sum of its lines" <<'SQL'
SELECT i.invoice_number, i.invoice_type, i.status, i.subtotal_usd, s.lines_usd, i.created_at::date
FROM invoices i CROSS JOIN LATERAL (SELECT coalesce(sum(line_total_usd), 0) AS lines_usd FROM invoice_lines l WHERE l.invoice_id = i.id) s
WHERE i.status <> 'DRAFT' AND i.invoice_type <> 'CREDIT_NOTE' AND abs(abs(i.subtotal_usd) - s.lines_usd) > 0.011
SQL
  check FAIL "invoice lines whose total ≠ quantity × price − line discount" <<'SQL'
SELECT i.invoice_number, l.line_number, l.quantity, l.unit_price_usd, l.discount_pct, l.line_total_usd,
       round(l.quantity * l.unit_price_usd * (1 - coalesce(l.discount_pct, 0) / 100), 2) AS expected
FROM invoice_lines l JOIN invoices i ON i.id = l.invoice_id
WHERE i.status <> 'DRAFT' AND abs(l.line_total_usd - round(l.quantity * l.unit_price_usd * (1 - coalesce(l.discount_pct, 0) / 100), 2)) > 0.011
SQL
  check FAIL "posted sale lines without a cost (gross profit and COGS would be wrong)" <<'SQL'
SELECT i.invoice_number, l.line_number, l.quantity, l.unit_price_usd, l.cost_price_usd FROM invoice_lines l JOIN invoices i ON i.id = l.invoice_id
WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND l.sku_id IS NOT NULL AND coalesce(l.cost_price_usd, 0) <= 0
SQL
  check WARN "posted sale lines sold below cost" <<'SQL'
SELECT i.invoice_number, l.line_number, l.unit_price_usd * (1 - coalesce(l.discount_pct, 0) / 100) AS net_price, l.cost_price_usd
FROM invoice_lines l JOIN invoices i ON i.id = l.invoice_id
WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND l.unit_price_usd * (1 - coalesce(l.discount_pct, 0) / 100) < l.cost_price_usd - 0.005
SQL
  check FAIL "negative balances on sales invoices (paid or credited more than the total)" <<'SQL'
SELECT invoice_number, invoice_type, status, total_usd, paid_usd, credit_applied_usd, balance_usd FROM invoices
WHERE invoice_type = 'SALE' AND balance_usd < -0.011
SQL
  check FAIL "paid amount on an invoice ≠ allocations of payments that are not reversed" <<'SQL'
SELECT i.invoice_number, i.status, i.paid_usd, a.allocated FROM invoices i
CROSS JOIN LATERAL (SELECT coalesce(sum(pa.allocated_usd), 0) AS allocated FROM payment_allocations pa JOIN payments p ON p.id = pa.payment_id
                    WHERE pa.invoice_id = i.id AND NOT p.is_reversed) a
WHERE i.invoice_type = 'SALE' AND abs(i.paid_usd - a.allocated) > 0.011
SQL
  check FAIL "payments: amount ≠ allocated + unallocated, or allocated ≠ its allocations" <<'SQL'
SELECT p.payment_number, p.amount_usd, p.allocated_usd, p.unallocated_usd, a.sum_alloc, p.is_reversed FROM payments p
CROSS JOIN LATERAL (SELECT coalesce(sum(allocated_usd), 0) AS sum_alloc FROM payment_allocations WHERE payment_id = p.id) a
WHERE abs(p.amount_usd - p.allocated_usd - p.unallocated_usd) > 0.011 OR abs(p.allocated_usd - a.sum_alloc) > 0.011
SQL
  check FAIL "allocations to draft or void invoices" <<'SQL'
SELECT p.payment_number, i.invoice_number, i.status, pa.allocated_usd FROM payment_allocations pa JOIN payments p ON p.id = pa.payment_id
JOIN invoices i ON i.id = pa.invoice_id WHERE NOT p.is_reversed AND i.status IN ('DRAFT', 'VOID', 'CANCELLED')
SQL
  check FAIL "returns without their original invoice, or pointing at a non-sale" <<'SQL'
SELECT r.invoice_number, r.status, r.original_invoice_id, o.invoice_type AS original_type, o.status AS original_status FROM invoices r
LEFT JOIN invoices o ON o.id = r.original_invoice_id
WHERE r.invoice_type = 'RETURN' AND r.status <> 'DRAFT' AND (o.id IS NULL OR o.invoice_type <> 'SALE')
SQL
  check FAIL "sale lines returned more than sold" <<'SQL'
SELECT o.invoice_number, ol.line_number, ol.quantity AS sold, sum(rl.quantity) AS returned FROM invoice_lines rl
JOIN invoices r ON r.id = rl.invoice_id AND r.invoice_type = 'RETURN' AND r.status IN ('POSTED', 'CONFIRMED')
JOIN invoice_lines ol ON ol.id = rl.return_of_line_id JOIN invoices o ON o.id = ol.invoice_id
GROUP BY o.invoice_number, ol.line_number, ol.quantity HAVING sum(rl.quantity) > ol.quantity + 0.0001
SQL
  check WARN "posted sales whose due date has passed and are still open (older than 90 days)" <<'SQL'
SELECT invoice_number, invoice_date, due_date, balance_usd FROM invoices
WHERE invoice_type = 'SALE' AND status = 'POSTED' AND balance_usd > 0.01 AND due_date < current_date - 90
SQL
  check WARN "drafts or confirmed documents older than 30 days (forgotten?)" <<'SQL'
SELECT 'invoice' AS kind, invoice_number AS number, status, created_at::date FROM invoices WHERE status IN ('DRAFT', 'CONFIRMED') AND created_at < now() - interval '30 days'
UNION ALL SELECT 'purchase', bill_number, status, created_at::date FROM purchase_invoices WHERE status = 'DRAFT' AND created_at < now() - interval '30 days'
UNION ALL SELECT 'journal', entry_number, status, created_at::date FROM journal_entries WHERE status = 'DRAFT' AND created_at < now() - interval '30 days'
UNION ALL SELECT 'landed cost', voucher_number, status, created_at::date FROM landed_cost_vouchers WHERE status = 'DRAFT' AND created_at < now() - interval '30 days'
SQL
  check WARN "customers over their credit limit" <<'SQL'
SELECT c.code, c.name, c.credit_limit_usd, sum(i.balance_usd) AS open_usd FROM customers c JOIN invoices i ON i.customer_id = c.id
WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND c.credit_limit_usd > 0
GROUP BY c.id, c.code, c.name, c.credit_limit_usd HAVING sum(i.balance_usd) > c.credit_limit_usd + 0.01
SQL

  sub "Purchasing and landed cost"
  q "purchase invoices by kind, return flag and status" <<'SQL'
SELECT kind, is_return, status, count(*), round(sum(total_usd), 2) AS total_usd, round(sum(balance_usd), 2) AS balance_usd FROM purchase_invoices GROUP BY 1, 2, 3 ORDER BY 1, 2, 3;
SQL
  check FAIL "purchase invoice total ≠ subtotal − discount, or subtotal ≠ its lines" <<'SQL'
SELECT p.bill_number, p.kind, p.is_return, p.status, p.subtotal_usd, p.discount_amount_usd, p.total_usd, s.lines_usd FROM purchase_invoices p
CROSS JOIN LATERAL (SELECT coalesce(sum(line_total_usd), 0) AS lines_usd FROM purchase_invoice_lines l WHERE l.purchase_invoice_id = p.id) s
WHERE p.status <> 'DRAFT' AND (abs(abs(p.total_usd) - (abs(p.subtotal_usd) - p.discount_amount_usd)) > 0.011 OR abs(abs(p.subtotal_usd) - s.lines_usd) > 0.011)
SQL
  check FAIL "negative balances on purchase invoices" <<'SQL'
SELECT bill_number, status, total_usd, paid_usd, credit_applied_usd, balance_usd FROM purchase_invoices WHERE NOT is_return AND balance_usd < -0.011
SQL
  check FAIL "paid amount on a purchase invoice ≠ allocations of supplier payments that are not reversed" <<'SQL'
SELECT p.bill_number, p.status, p.paid_usd, a.allocated FROM purchase_invoices p
CROSS JOIN LATERAL (SELECT coalesce(sum(sa.allocated_usd), 0) AS allocated FROM supplier_payment_allocations sa
                    JOIN supplier_payments s ON s.id = sa.supplier_payment_id WHERE sa.purchase_invoice_id = p.id AND NOT s.is_reversed) a
WHERE abs(p.paid_usd - a.allocated) > 0.011
SQL
  check FAIL "purchase returns without their original bill" <<'SQL'
SELECT r.bill_number, r.status, o.bill_number AS original, o.status AS original_status FROM purchase_invoices r
LEFT JOIN purchase_invoices o ON o.id = r.return_against_id WHERE r.is_return AND r.status <> 'DRAFT' AND (o.id IS NULL OR o.is_return)
SQL
  check FAIL "purchase lines returned more than bought" <<'SQL'
SELECT o.bill_number, ol.line_number, ol.quantity AS bought, sum(rl.quantity) AS returned FROM purchase_invoice_lines rl
JOIN purchase_invoices r ON r.id = rl.purchase_invoice_id AND r.is_return AND r.status = 'POSTED'
JOIN purchase_invoice_lines ol ON ol.id = rl.return_of_line_id JOIN purchase_invoices o ON o.id = ol.purchase_invoice_id
GROUP BY o.bill_number, ol.line_number, ol.quantity HAVING sum(rl.quantity) > ol.quantity + 0.0001
SQL
  check FAIL "posted landed-cost vouchers whose allocations do not add up to their charges" <<'SQL'
SELECT v.voucher_number, v.total_usd, (SELECT coalesce(sum(amount_usd), 0) FROM landed_cost_charges c WHERE c.voucher_id = v.id) AS charges,
       (SELECT coalesce(sum(amount_usd), 0) FROM landed_cost_allocations a WHERE a.voucher_id = v.id) AS allocated
FROM landed_cost_vouchers v WHERE v.status = 'POSTED'
  AND abs((SELECT coalesce(sum(amount_usd), 0) FROM landed_cost_charges c WHERE c.voucher_id = v.id)
        - (SELECT coalesce(sum(amount_usd), 0) FROM landed_cost_allocations a WHERE a.voucher_id = v.id)) > 0.011
SQL

  sub "Journal entries"
  q "journal entries by type and status" <<'SQL'
SELECT t.code, j.status, count(*), round(sum(j.total_usd), 2) AS total_usd FROM journal_entries j LEFT JOIN entry_types t ON t.id = j.entry_type_id GROUP BY 1, 2 ORDER BY 1, 2;
SQL
  check FAIL "journal entries whose debits ≠ credits, or total ≠ debits" <<'SQL'
SELECT j.entry_number, j.status, j.total_usd, s.debit, s.credit FROM journal_entries j
CROSS JOIN LATERAL (SELECT coalesce(sum(debit_usd), 0) AS debit, coalesce(sum(credit_usd), 0) AS credit FROM journal_entry_lines l WHERE l.journal_entry_id = j.id) s
WHERE j.status <> 'DRAFT' AND (abs(s.debit - s.credit) > 0.005 OR abs(j.total_usd - s.debit) > 0.011)
SQL
  check FAIL "journal lines with both debit and credit, or neither, or negative" <<'SQL'
SELECT j.entry_number, l.line_number, l.account_name, l.debit_usd, l.credit_usd FROM journal_entry_lines l JOIN journal_entries j ON j.id = l.journal_entry_id
WHERE (l.debit_usd > 0 AND l.credit_usd > 0) OR (coalesce(l.debit_usd, 0) = 0 AND coalesce(l.credit_usd, 0) = 0) OR l.debit_usd < 0 OR l.credit_usd < 0
SQL
  check FAIL "posted documents dated inside a locked period" <<'SQL'
SELECT 'invoice' AS kind, i.invoice_number AS number, i.invoice_date AS date, i.posted_at::timestamp(0), pl.locked_at_utc::timestamp(0)
FROM invoices i JOIN period_locks pl ON pl.is_locked AND pl.module_code IN ('INVOICES', 'ALL') AND pl.period_key = to_char(i.invoice_date, 'YYYY-MM')
WHERE i.posted_at > pl.locked_at_utc
UNION ALL
SELECT 'journal', j.entry_number, j.entry_date, j.posted_at::timestamp(0), pl.locked_at_utc::timestamp(0)
FROM journal_entries j JOIN period_locks pl ON pl.is_locked AND pl.module_code IN ('ACCOUNTING', 'ALL') AND pl.period_key = to_char(j.entry_date, 'YYYY-MM')
WHERE j.posted_at > pl.locked_at_utc
SQL
  q "locked periods" <<'SQL'
SELECT period_key, module_code, is_locked, locked_at_utc::timestamp(0), left(reason, 60) AS reason FROM period_locks ORDER BY period_key DESC LIMIT 12;
SQL

  sub "Stock"
  check FAIL "negative stock (per item, location and status)" <<'SQL'
SELECT it.part_number, l.code AS location, b.status, b.qty FROM inventory_balances b JOIN items it ON it.id = b.item_id
LEFT JOIN locations l ON l.id = b.location_id WHERE b.qty < -0.0001
SQL
  check FAIL "negative on-hand quantity in inventory_stock" <<'SQL'
SELECT s.code AS sku, st.location_id, st.quantity_on_hand, st.quantity_reserved, st.quantity_available FROM inventory_stock st JOIN skus s ON s.id = st.sku_id
WHERE st.quantity_on_hand < -0.0001 OR st.quantity_available < -0.0001
SQL
  check INFO "warehouse balances (by item) that differ from the sum of their movements — review, seeded or imported stock has no movements" <<'SQL'
SELECT coalesce(m.item_id, b.item_id) AS item_id, coalesce(m.location_id, b.location_id) AS location_id, m.qty AS from_movements, b.qty AS balance
FROM (SELECT item_id, location_id, sum(CASE WHEN direction = 'IN' THEN qty ELSE -qty END) AS qty FROM inventory_movements
      WHERE movement_type <> 'STATUS_CHANGE' GROUP BY 1, 2) m
FULL JOIN (SELECT item_id, location_id, sum(qty) AS qty FROM inventory_balances GROUP BY 1, 2) b ON b.item_id = m.item_id AND b.location_id = m.location_id
WHERE abs(coalesce(m.qty, 0) - coalesce(b.qty, 0)) > 0.0001
SQL
  check WARN "sellable stock (inventory_stock) ≠ AVAILABLE warehouse balance" <<'SQL'
SELECT coalesce(b.sku_id, s.sku_id) AS sku_id, coalesce(b.location_id, s.location_id) AS location_id, b.qty AS balances, s.quantity_on_hand
FROM (SELECT it.sku_id, ib.location_id, sum(ib.qty) AS qty FROM inventory_balances ib JOIN items it ON it.id = ib.item_id WHERE ib.status = 'AVAILABLE' GROUP BY 1, 2) b
FULL JOIN inventory_stock s ON s.sku_id = b.sku_id AND s.location_id = b.location_id
WHERE abs(coalesce(b.qty, 0) - coalesce(s.quantity_on_hand, 0)) > 0.0001
SQL
  check WARN "items with stock but no cost (stock value and COGS understated)" <<'SQL'
SELECT s.code, s.name, s.cost_price_usd, sum(st.quantity_on_hand) AS on_hand FROM skus s JOIN inventory_stock st ON st.sku_id = s.id
GROUP BY s.id, s.code, s.name, s.cost_price_usd HAVING sum(st.quantity_on_hand) > 0 AND coalesce(s.cost_price_usd, 0) <= 0
SQL
  check WARN "batches with negative quantity or more than received" <<'SQL'
SELECT batch_number, sku_id, quantity_initial, quantity_current, status FROM batches WHERE quantity_current < -0.0001 OR quantity_current > quantity_initial + 0.0001
SQL
  q "open stock alerts" <<'SQL'
SELECT alert_type, severity, count(*) FROM inventory_alerts WHERE status NOT IN ('RESOLVED', 'DISMISSED') GROUP BY 1, 2 ORDER BY 3 DESC;
SQL
  q "stock value" <<'SQL'
SELECT round(sum(st.quantity_on_hand * s.cost_price_usd), 2) AS stock_value_usd, count(DISTINCT st.sku_id) FILTER (WHERE st.quantity_on_hand > 0) AS skus_in_stock
FROM inventory_stock st JOIN skus s ON s.id = st.sku_id;
SQL

  sub "Background work: outbox, ERPNext sync, Hangfire"
  q "outbox by event (unprocessed / failed)" <<'SQL'
SELECT event_type, count(*) AS total, count(*) FILTER (WHERE processed_at IS NULL) AS unprocessed, count(*) FILTER (WHERE processing_error IS NOT NULL) AS with_error,
       max(retry_count) AS max_retries, min(occurred_at) FILTER (WHERE processed_at IS NULL)::timestamp(0) AS oldest_unprocessed
FROM outbox_messages GROUP BY 1 ORDER BY 3 DESC, 2 DESC;
SQL
  check FAIL "outbox messages not processed after 15 minutes" <<'SQL'
SELECT event_type, aggregate_type, aggregate_id, occurred_at::timestamp(0), retry_count, left(processing_error, 200) AS error FROM outbox_messages
WHERE processed_at IS NULL AND occurred_at < now() - interval '15 minutes' ORDER BY occurred_at
SQL
  check WARN "outbox messages that failed at least once (latest)" <<'SQL'
SELECT event_type, occurred_at::timestamp(0), processed_at::timestamp(0), retry_count, left(processing_error, 250) AS error FROM outbox_messages
WHERE processing_error IS NOT NULL ORDER BY occurred_at DESC
SQL
  q "ERPNext sync log by entity and status" <<'SQL'
SELECT local_entity_type, status, count(*), max(updated_at)::timestamp(0) AS last_change FROM erpnext_sync_log GROUP BY 1, 2 ORDER BY 1, 2;
SQL
  check FAIL "ERPNext sync failures" <<'SQL'
SELECT local_entity_type, local_entity_id, erpnext_doctype, erpnext_name, attempt_count, updated_at::timestamp(0), left(last_error, 300) AS error
FROM erpnext_sync_log WHERE status = 'FAILED' ORDER BY updated_at DESC
SQL
  check WARN "ERPNext sync still pending after 15 minutes" <<'SQL'
SELECT local_entity_type, local_entity_id, attempt_count, created_at::timestamp(0), left(last_error, 200) AS error FROM erpnext_sync_log
WHERE status = 'PENDING' AND created_at < now() - interval '15 minutes'
SQL
  check FAIL "synced to ERPNext but no ERPNext document name recorded" <<'SQL'
SELECT local_entity_type, local_entity_id, erpnext_doctype, synced_at::timestamp(0) FROM erpnext_sync_log WHERE status = 'SYNCED' AND coalesce(erpnext_name, '') = ''
SQL
  if [[ "${ERPNEXT_ENABLED:-false}" == "true" || -n "${DIAG_PSQL:-}" ]]; then
    check FAIL "posted documents never sent to ERPNext (older than 15 minutes)" <<'SQL'
SELECT 'Invoice' AS kind, invoice_number AS number, status, posted_at::timestamp(0) FROM invoices i WHERE status IN ('POSTED', 'VOID') AND posted_at < now() - interval '15 minutes'
  AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log s WHERE s.local_entity_id = i.id AND s.local_entity_type = 'Invoice')
UNION ALL SELECT 'PurchaseInvoice', bill_number, status, posted_at::timestamp(0) FROM purchase_invoices p WHERE status IN ('POSTED', 'VOID') AND posted_at < now() - interval '15 minutes'
  AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log s WHERE s.local_entity_id = p.id AND s.local_entity_type = 'PurchaseInvoice')
UNION ALL SELECT 'JournalEntry', entry_number, status, posted_at::timestamp(0) FROM journal_entries j WHERE status IN ('POSTED', 'VOID') AND posted_at < now() - interval '15 minutes'
  AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log s WHERE s.local_entity_id = j.id AND s.local_entity_type = 'JournalEntry')
UNION ALL SELECT 'Payment', payment_number, CASE WHEN is_reversed THEN 'REVERSED' ELSE 'ACTIVE' END, created_at::timestamp(0) FROM payments p WHERE created_at < now() - interval '15 minutes'
  AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log s WHERE s.local_entity_id = p.id AND s.local_entity_type = 'Payment')
UNION ALL SELECT 'SupplierPayment', payment_number, CASE WHEN is_reversed THEN 'REVERSED' ELSE 'ACTIVE' END, created_at::timestamp(0) FROM supplier_payments p WHERE created_at < now() - interval '15 minutes'
  AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log s WHERE s.local_entity_id = p.id AND s.local_entity_type = 'SupplierPayment')
UNION ALL SELECT 'LandedCost', voucher_number, status, posted_at::timestamp(0) FROM landed_cost_vouchers v WHERE status IN ('POSTED', 'VOID') AND posted_at < now() - interval '15 minutes'
  AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log s WHERE s.local_entity_id = v.id AND s.local_entity_type = 'LandedCost')
SQL
    check FAIL "voided here but still active in ERPNext (sync not cancelled)" <<'SQL'
SELECT 'Invoice' AS kind, i.invoice_number AS number, i.voided_at::timestamp(0), s.status, s.erpnext_name FROM invoices i
JOIN erpnext_sync_log s ON s.local_entity_id = i.id AND s.local_entity_type = 'Invoice'
WHERE i.status = 'VOID' AND i.voided_at < now() - interval '15 minutes' AND s.status = 'SYNCED'
UNION ALL SELECT 'PurchaseInvoice', p.bill_number, p.voided_at::timestamp(0), s.status, s.erpnext_name FROM purchase_invoices p
JOIN erpnext_sync_log s ON s.local_entity_id = p.id AND s.local_entity_type = 'PurchaseInvoice'
WHERE p.status = 'VOID' AND p.voided_at < now() - interval '15 minutes' AND s.status = 'SYNCED'
UNION ALL SELECT 'JournalEntry', j.entry_number, j.voided_at::timestamp(0), s.status, s.erpnext_name FROM journal_entries j
JOIN erpnext_sync_log s ON s.local_entity_id = j.id AND s.local_entity_type = 'JournalEntry'
WHERE j.status = 'VOID' AND j.voided_at < now() - interval '15 minutes' AND s.status = 'SYNCED'
SQL
  fi
  q "Hangfire servers (heartbeat)" <<'SQL'
SELECT id, lastheartbeat::timestamp(0), now() - lastheartbeat AS since FROM hangfire.server ORDER BY lastheartbeat DESC;
SQL
  check FAIL "no Hangfire server alive in the last 5 minutes (scheduled jobs are not running)" <<'SQL'
SELECT 'none alive' AS problem WHERE NOT EXISTS (SELECT 1 FROM hangfire.server WHERE lastheartbeat > now() - interval '5 minutes')
SQL
  q "Hangfire jobs by state" <<'SQL'
SELECT statename, count(*) FROM hangfire.job GROUP BY 1 ORDER BY 2 DESC;
SQL
  check WARN "failed Hangfire jobs (latest)" <<'SQL'
SELECT j.id, j.createdat::timestamp(0), left(j.invocationdata::json ->> 'Type', 80) AS type, j.invocationdata::json ->> 'Method' AS method,
       left(coalesce(s.data::json ->> 'ExceptionMessage', s.reason), 250) AS error
FROM hangfire.job j LEFT JOIN hangfire.state s ON s.id = j.stateid WHERE j.statename = 'Failed' ORDER BY j.createdat DESC
SQL
  q "recurring jobs" <<'SQL'
SELECT key, value FROM hangfire.hash WHERE key LIKE 'recurring-job:%' AND field IN ('Cron', 'LastExecution', 'NextExecution', 'LastJobId') ORDER BY key, field;
SQL

  sub "Users, approvals, audit"
  q "users by role" <<'SQL'
SELECT coalesce(r.name, '(no role)') AS role, count(DISTINCT u.id) AS users, count(DISTINCT u.id) FILTER (WHERE u.lockout_end > now()) AS locked_out,
       max(u.last_login_at)::timestamp(0) AS last_login
FROM asp_net_users u LEFT JOIN asp_net_user_roles ur ON ur.user_id = u.id LEFT JOIN asp_net_roles r ON r.id = ur.role_id GROUP BY 1 ORDER BY 1;
SQL
  check WARN "SYSTEM_ADMIN accounts without two-factor sign-in" <<'SQL'
SELECT u.user_name, u.email, u.last_login_at::timestamp(0) FROM asp_net_users u JOIN asp_net_user_roles ur ON ur.user_id = u.id
JOIN asp_net_roles r ON r.id = ur.role_id WHERE r.normalized_name = 'SYSTEM_ADMIN' AND NOT u.two_factor_enabled
SQL
  check FAIL "the reserved permission documents:delete granted to a role other than SYSTEM_ADMIN" <<'SQL'
SELECT r.name, c.claim_value FROM asp_net_role_claims c JOIN asp_net_roles r ON r.id = c.role_id
WHERE c.claim_value = 'documents:delete' AND r.normalized_name <> 'SYSTEM_ADMIN'
SQL
  check WARN "accounts with many failed sign-ins" <<'SQL'
SELECT user_name, access_failed_count, lockout_end::timestamp(0) FROM asp_net_users WHERE access_failed_count >= 3
SQL
  check WARN "approval requests pending for more than 3 days" <<'SQL'
SELECT request_type, entity_type, entity_id, status, created_at::timestamp(0) FROM approval_requests WHERE status = 'PENDING' AND created_at < now() - interval '3 days'
SQL
  q "audit log activity (last 7 days)" <<'SQL'
SELECT date_trunc('day', created_at)::date AS day, count(*) FROM audit_logs WHERE created_at > now() - interval '7 days' GROUP BY 1 ORDER BY 1;
SQL
  check INFO "expired idempotency keys not yet purged" <<'SQL'
SELECT count(*) AS expired FROM idempotency_keys WHERE expires_at_utc < now() - interval '1 day' HAVING count(*) > 1000
SQL
  q "materialized views" <<'SQL'
SELECT matviewname, ispopulated, pg_size_pretty(pg_total_relation_size(format('%I.%I', schemaname, matviewname)::regclass)) AS size FROM pg_matviews;
SQL
}

# ---------------------------------------------------------------------------------------------------------------------- erpnext
erp_get() {
  # GET <path> against ERPNext as the API key's user (the same view the application has). Prints the status and up to 3000 characters.
  local path
  path=$(printf '%s' "$1" | sed -e 's/ /%20/g' -e 's/"/%22/g' -e 's/\[/%5B/g' -e 's/\]/%5D/g' -e 's/,/%2C/g' -e 's/>/%3E/g' -e 's/</%3C/g')
  ERP_AUTH="${ERPNEXT_API_KEY:-}:${ERPNEXT_API_SECRET:-}" docker run --rm --add-host=host.docker.internal:host-gateway -e ERP_AUTH \
    --entrypoint sh curlimages/curl:8.11.1 -c 'curl -sS -m 40 -g -H "Authorization: token $ERP_AUTH" -H "Accept: application/json" \
      -w "\n[HTTP %{http_code}, %{time_total}s]" "$1"' _ "${ERPNEXT_BASE_URL%/}$path" 2>&1 | head -c 3000
  echo
}
erp_code() { erp_get "$1" | grep -oE '\[HTTP [0-9]+' | grep -oE '[0-9]+$'; }

erpnext_section() {
  section "6. ERPNEXT"
  if [[ "${ERPNEXT_ENABLED:-false}" != "true" || -z "${ERPNEXT_BASE_URL:-}" ]]; then flag WARN "ERPNext integration disabled — section skipped"; return; fi
  echo "base url: ${ERPNEXT_BASE_URL}"
  export ERPNEXT_API_KEY ERPNEXT_API_SECRET

  sub "Reachability and identity"
  echo "ping:"; erp_get "/api/method/ping"
  [[ "$(erp_code /api/method/ping)" == "200" ]] || flag FAIL "ERPNext does not answer /api/method/ping"
  echo "API key's user:"; local who; who=$(erp_get "/api/method/frappe.auth.get_logged_user"); echo "$who"
  [[ "$who" == *"HTTP 200"* ]] || flag FAIL "the ERPNext API key/secret is not accepted"
  local user; user=$(grep -oE '"message":"[^"]+"' <<<"$who" | sed -E 's/"message":"(.*)"/\1/')
  echo "versions:"; erp_get "/api/method/frappe.utils.change_log.get_versions"
  if [[ -n "$user" ]]; then
    echo "roles of $user:"
    local roles; roles=$(erp_get "/api/resource/Has Role?fields=[\"role\"]&filters=[[\"parent\",\"=\",\"$user\"],[\"parenttype\",\"=\",\"User\"]]&limit_page_length=0")
    echo "$roles"
    if [[ "$roles" == *"HTTP 200"* ]]; then
      for role in "System Manager" "Accounts Manager" "Accounts User" "Stock Manager" "Sales Manager" "Purchase Manager"; do
        grep -q "\"$role\"" <<<"$roles" && echo "  has: $role" || echo "  MISSING: $role"
      done
      grep -q '"System Manager"' <<<"$roles" || flag WARN "ERPNext user $user lacks System Manager (needed by the demo-data reset)"
      grep -q '"Accounts Manager"' <<<"$roles" || flag WARN "ERPNext user $user lacks Accounts Manager"
    else
      # Has Role is readable only by System Manager; fall back to what the user may do.
      echo "  (Has Role not readable — probing permissions instead)"
      local dt code
      for dt in "Transaction Deletion Record" "Account" "Journal Entry" "Sales Invoice" "Purchase Invoice" "Payment Entry" "GL Entry" "Customer" "Supplier" "Item" "Error Log"; do
        code=$(erp_code "/api/resource/$dt?limit_page_length=1")
        echo "  read $dt -> $code"
      done
      [[ "$(erp_code '/api/resource/Transaction Deletion Record?limit_page_length=1')" == "200" ]] || flag WARN "ERPNext user $user lacks System Manager (needed by the demo-data reset)"
    fi
  fi

  sub "Company, fiscal years, settings"
  erp_get "/api/resource/Company?fields=[\"name\",\"default_currency\",\"country\",\"default_receivable_account\",\"default_payable_account\",\"default_income_account\",\"default_expense_account\",\"cost_center\",\"round_off_account\",\"stock_adjustment_account\",\"default_inventory_account\"]&limit_page_length=0"
  local fy; fy=$(erp_get "/api/resource/Fiscal Year?fields=[\"name\",\"year_start_date\",\"year_end_date\",\"disabled\"]&limit_page_length=0&order_by=year_start_date%20desc"); echo "$fy"
  grep -q "\"year_end_date\":\"$(date +%Y)" <<<"$fy" || grep -q "$(date +%Y)-" <<<"$fy" || flag FAIL "no ERPNext fiscal year covers $(date +%Y)"
  erp_get "/api/resource/Accounts Settings/Accounts Settings" | grep -oE '"(acc_frozen_upto|frozen_accounts_modifier|check_supplier_invoice_uniqueness|unlink_payment_on_cancellation_of_invoice|delete_linked_ledger_entries|book_asset_depreciation_entry_automatically|over_billing_allowance)":[^,}]*'
  local ss; ss=$(erp_get "/api/resource/System Settings/System Settings")
  grep -oE '"(enable_scheduler|time_zone|country|currency|number_format|float_precision|currency_precision|disable_rounded_total)":[^,}]*' <<<"$ss"
  grep -q '"enable_scheduler":0' <<<"$ss" && flag FAIL "the ERPNext scheduler is disabled (System Settings → enable_scheduler)"

  sub "Accounts this application relies on"
  erp_get "/api/resource/Account?fields=[\"name\",\"root_type\",\"account_type\",\"is_group\",\"disabled\",\"freeze_account\"]&filters=[[\"account_name\",\"like\",\"%25AutoParts%25\"]]&limit_page_length=0"
  erp_get "/api/resource/Account?fields=[\"name\",\"account_type\",\"disabled\"]&filters=[[\"account_type\",\"in\",[\"Receivable\",\"Payable\",\"Cash\",\"Bank\",\"Stock\",\"Cost of Goods Sold\"]],[\"is_group\",\"=\",0]]&limit_page_length=0"
  local frozen; frozen=$(erp_get "/api/resource/Account?fields=[\"name\"]&filters=[[\"freeze_account\",\"=\",\"Yes\"]]&limit_page_length=0"); echo "frozen accounts: $frozen"

  sub "Documents by status (0 draft, 1 submitted, 2 cancelled)"
  local dt ds
  for dt in "Sales Invoice" "Purchase Invoice" "Payment Entry" "Journal Entry"; do
    printf '  %-18s' "$dt"
    for ds in 0 1 2; do
      printf ' ds%s=%s' "$ds" "$(erp_get "/api/method/frappe.client.get_count?doctype=$dt&filters=[[\"docstatus\",\"=\",$ds]]" | grep -oE '"message":[0-9]+' | cut -d: -f2)"
    done
    echo
  done
  echo "drafts left behind (docstatus 0) — should be none, the application submits what it creates:"
  for dt in "Sales Invoice" "Purchase Invoice" "Payment Entry" "Journal Entry"; do
    local drafts; drafts=$(erp_get "/api/resource/$dt?fields=[\"name\",\"creation\"]&filters=[[\"docstatus\",\"=\",0]]&limit_page_length=10")
    grep -q '"name"' <<<"$drafts" && { echo "  $dt: $drafts"; flag WARN "ERPNext has draft $dt documents"; }
  done
  echo "returns (is_return):"
  erp_get "/api/method/frappe.client.get_count?doctype=Sales Invoice&filters=[[\"is_return\",\"=\",1],[\"docstatus\",\"=\",1]]"
  erp_get "/api/method/frappe.client.get_count?doctype=Purchase Invoice&filters=[[\"is_return\",\"=\",1],[\"docstatus\",\"=\",1]]"

  sub "General ledger"
  # Frappe v16 accepts aggregates only as {"SUM": field}; v15 only as "sum(field) as x" — try the new form, then the old.
  echo "debits vs credits of all active GL entries (must be equal):"
  local gl; gl=$(erp_get "/api/resource/GL Entry?fields=[{\"SUM\":\"debit\",\"as\":\"debit\"},{\"SUM\":\"credit\",\"as\":\"credit\"},{\"COUNT\":\"name\",\"as\":\"entries\"}]&filters=[[\"is_cancelled\",\"=\",0]]")
  [[ "$gl" == *"HTTP 200"* ]] || gl=$(erp_get "/api/resource/GL Entry?fields=[\"sum(debit) as debit\",\"sum(credit) as credit\",\"count(name) as entries\"]&filters=[[\"is_cancelled\",\"=\",0]]")
  echo "$gl"
  local d c
  d=$(grep -oE '"debit":[-0-9.eE]+' <<<"$gl" | cut -d: -f2); c=$(grep -oE '"credit":[-0-9.eE]+' <<<"$gl" | cut -d: -f2)
  if [[ -n "$d" && -n "$c" ]]; then
    awk -v d="$d" -v c="$c" 'BEGIN { exit !((d - c) > 0.01 || (c - d) > 0.01) }' && flag FAIL "ERPNext general ledger is out of balance (debit $d, credit $c)"
  fi
  echo "debit and credit per account:"
  local per; per=$(erp_get "/api/resource/GL Entry?fields=[\"account\",{\"SUM\":\"debit\",\"as\":\"debit\"},{\"SUM\":\"credit\",\"as\":\"credit\"}]&filters=[[\"is_cancelled\",\"=\",0]]&group_by=account&order_by=account%20asc&limit_page_length=300")
  [[ "$per" == *"HTTP 200"* ]] || per=$(erp_get "/api/resource/GL Entry?fields=[\"account\",\"sum(debit) as debit\",\"sum(credit) as credit\"]&filters=[[\"is_cancelled\",\"=\",0]]&group_by=account&order_by=account%20asc&limit_page_length=300")
  echo "$per"

  sub "Errors inside ERPNext"
  erp_get "/api/method/frappe.client.get_count?doctype=Error Log"
  echo "latest error logs:"
  erp_get "/api/resource/Error Log?fields=[\"name\",\"method\",\"creation\"]&order_by=creation%20desc&limit_page_length=20"
  local recent; recent=$(erp_get "/api/method/frappe.client.get_count?doctype=Error Log&filters=[[\"creation\",\">\",\"$(date -u -d '-1 day' +%F 2>/dev/null || date -u +%F)\"]]" | grep -oE '"message":[0-9]+' | cut -d: -f2)
  [[ "${recent:-0}" -gt 0 ]] && flag WARN "$recent ERPNext Error Log entries since yesterday"
  echo "failed scheduled jobs (latest):"
  erp_get "/api/resource/Scheduled Job Log?fields=[\"scheduled_job_type\",\"status\",\"creation\"]&filters=[[\"status\",\"=\",\"Failed\"]]&order_by=creation%20desc&limit_page_length=15"
  echo "background workers (RQ Worker):"
  local workers; workers=$(erp_get "/api/resource/RQ Worker?fields=[\"name\",\"status\",\"queue\",\"last_heartbeat\",\"failed_job_count\"]&limit_page_length=20"); echo "$workers"
  [[ "$workers" == *"HTTP 200"* && "$workers" != *'"name"'* ]] && flag FAIL "no ERPNext background worker is running (deletions, emails, reports wait forever)"
  echo "failed background jobs (RQ Job):"
  erp_get "/api/resource/RQ Job?fields=[\"job_name\",\"status\",\"queue\",\"exc_info\"]&filters=[[\"status\",\"=\",\"failed\"]]&limit_page_length=10"
  echo "Transaction Deletion Records:"
  erp_get "/api/resource/Transaction Deletion Record?fields=[\"name\",\"company\",\"status\",\"creation\"]&order_by=creation%20desc&limit_page_length=5"

  sub "Masters"
  for dt in Customer Supplier Item "Item Price" "Sales Person"; do
    printf '  %-14s %s\n' "$dt" "$(erp_get "/api/method/frappe.client.get_count?doctype=$dt" | grep -oE '"message":[0-9]+|HTTP [0-9]+' | tr '\n' ' ')"
  done

  sub "ERPNext containers on this host (if it runs here in Docker)"
  local erp_containers; erp_containers=$(docker ps -a --format '{{.Names}}\t{{.Image}}\t{{.Status}}' | grep -Ei 'frappe|erpnext' || true)
  if [[ -n "$erp_containers" ]]; then
    echo "$erp_containers"
    grep -viE '\bUp\b' <<<"$erp_containers" | grep -q . && flag FAIL "an ERPNext container is not running: $(grep -viE '\bUp\b' <<<"$erp_containers" | cut -f1 | tr '\n' ' ')"
    local backend; backend=$(docker ps --format '{{.Names}}\t{{.Image}}' | grep -Ei 'frappe|erpnext' | grep -Ei 'backend' | head -1 | cut -f1)
    [[ -z "$backend" ]] && backend=$(docker ps --format '{{.Names}}\t{{.Image}}' | grep -Ei 'frappe|erpnext' | grep -viE 'db|mariadb|redis|nginx|frontend|proxy|websocket|socketio' | head -1 | cut -f1)
    if [[ -n "$backend" ]]; then
      echo "-- bench in $backend --"
      run docker exec "$backend" bench version
      run docker exec "$backend" bench doctor
      run docker exec "$backend" bash -lc 'ls sites | grep -vE "\.(json|txt)$|^assets$"'
      run docker exec "$backend" bash -lc 'for s in $(ls sites | grep -vE "\.(json|txt)$|^assets$|^apps"); do echo "site $s:"; bench --site "$s" scheduler status 2>&1 | tail -2; done'
    fi
    local cname
    for cname in $(docker ps --format '{{.Names}}' | grep -Ei 'frappe|erpnext'); do
      echo "-- $cname: errors in the last $SINCE --"
      docker logs --since "$SINCE" "$cname" 2>&1 | grep -iE 'error|exception|traceback|critical|killed|timeout' | grep -viE 'error_log|errorlog' | tail -12
    done
  else
    echo "(no frappe/erpnext containers here)"
    if have bench || [[ -d /home/frappe/frappe-bench ]]; then
      echo "-- native bench install --"
      have supervisorctl && run supervisorctl status
      run sudo -u frappe bash -lc 'cd /home/frappe/frappe-bench && bench version && bench doctor'
    fi
  fi
}

# ---------------------------------------------------------------------------------------------------------------------- backup
backup_section() {
  section "7. BACKUPS"
  local dir="${BACKUP_DIR:-/var/backups/autoparts-erp}"
  run ls -la "$dir"
  local kind newest
  for kind in daily weekly monthly predeploy manual; do
    [[ -d "$dir/$kind" ]] || continue
    echo "-- $kind (newest 3) --"
    find "$dir/$kind" -maxdepth 1 -name '*.dump' -printf '%TY-%Tm-%Td %TH:%TM  %s bytes  %f\n' 2>/dev/null | sort -r | head -3
  done
  newest=$(find "$dir" -name '*.dump' -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1)
  if [[ -z "$newest" ]]; then
    flag FAIL "no database backup found in $dir"
  else
    local age_h=$(( ( $(date +%s) - ${newest%%.*} ) / 3600 ))
    echo "newest backup: ${newest#* } (${age_h} h old)"
    [[ "$age_h" -gt 30 ]] && flag FAIL "the newest database backup is ${age_h} hours old"
    local sum="${newest#* }.sha256"
    [[ -f "$sum" ]] && (cd "$(dirname "$sum")" && sha256sum -c "$(basename "$sum")" 2>&1 | tail -1)
  fi
  run cat /etc/cron.d/autoparts-erp-backup
  [[ -f /etc/cron.d/autoparts-erp-backup ]] || flag FAIL "backup cron job /etc/cron.d/autoparts-erp-backup is missing"
  echo "-- backup log (last 25 lines) --"
  tail -25 /var/log/autoparts-erp-backup.log 2>&1
  grep -qiE 'error|fail' <(tail -60 /var/log/autoparts-erp-backup.log 2>/dev/null) && flag WARN "the backup log reports errors recently"
  [[ -z "${BACKUP_REMOTE:-}" ]] && flag WARN "BACKUP_REMOTE is empty — backups exist only on this server"
  run du -sh "$dir"
}

# ---------------------------------------------------------------------------------------------------------------------- security
security_section() {
  section "8. SECURITY"
  if have sshd; then
    sub "SSH"
    sshd -T 2>/dev/null | grep -E '^(port|permitrootlogin|passwordauthentication|pubkeyauthentication|kbdinteractiveauthentication|maxauthtries|x11forwarding|allowusers) '
    sshd -T 2>/dev/null | grep -q '^passwordauthentication yes' && flag INFO "SSH password sign-in is on (turn off only after a key works — SETUP_HARDENING)"
    sshd -T 2>/dev/null | grep -q '^permitrootlogin yes' && flag WARN "SSH allows root to sign in with a password (PermitRootLogin yes)"
    echo "authorized keys for root: $(grep -cE '^(ssh|ecdsa|sk-)' /root/.ssh/authorized_keys 2>/dev/null || echo 0)"
    if have journalctl; then
      local fails; fails=$(journalctl --since "24 hours ago" --no-pager 2>/dev/null | grep -cE 'Failed password|Invalid user' || true)
      echo "failed SSH sign-ins in 24 h: $fails"
      [[ "$fails" -gt 200 ]] && flag WARN "$fails failed SSH sign-ins in 24 h (install/enable fail2ban)"
      echo "-- successful sign-ins (last 10) --"
      journalctl --since "7 days ago" --no-pager 2>/dev/null | grep -E 'Accepted (password|publickey)' | tail -10 | sed -E 's/port [0-9]+.*//'
    fi
  fi
  sub "Firewall"
  if have ufw; then run ufw status verbose; ufw status 2>/dev/null | grep -q 'Status: active' || flag WARN "the ufw firewall is not active"; fi
  have nft && run sh -c 'nft list ruleset 2>/dev/null | grep -cE "^\s*(accept|drop|reject)|dport" | sed "s/^/nftables rules: /"'
  have fail2ban-client && run fail2ban-client status
  have fail2ban-client || flag INFO "fail2ban is not installed"
  sub "Ports open to the internet (published by Docker or listening on all addresses)"
  have ss && ss -tulpn 2>/dev/null | awk 'NR>1 && ($5 ~ /^(0\.0\.0\.0|\*|\[::\]):/) {print $1, $5, $7}'
  if have ss && ss -tlnp 2>/dev/null | awk '{print $4}' | grep -qE '^(0\.0\.0\.0|\*|\[::\]):5432$'; then flag FAIL "PostgreSQL (5432) listens on all addresses"; fi
  if have ss && ss -tlnp 2>/dev/null | awk '{print $4}' | grep -qE '^(0\.0\.0\.0|\*|\[::\]):6379$'; then flag FAIL "Redis (6379) listens on all addresses"; fi
  sub "Files that must not be here"
  local f
  for f in "ADMIN PASSWORD.txt" src/AutoPartsERP.Api/appsettings.Development.json frontend/__auth.json; do
    [[ -e "$f" ]] && flag WARN "$f exists on the server"
  done
  find "$ROOT_DIR" -maxdepth 2 -iname '*password*.txt' 2>/dev/null | while read -r f; do flag WARN "password file on the server: $f"; done
  git ls-files --error-unmatch .env.vps >/dev/null 2>&1 && flag FAIL ".env.vps is tracked by git"
  sub "Public exposure of internal pages"
  local p code
  for p in /hangfire /metrics /swagger /swagger/index.html /.env /.git/config; do
    code=$(curl -sk -o /dev/null -m 10 -w '%{http_code}' "https://localhost$p" || true)
    echo "  https://localhost$p -> $code"
    [[ "$code" == "200" && "$p" =~ ^/(\.env|\.git) ]] && flag FAIL "$p is downloadable from the web"
  done
}

main() {
  echo "AutoParts ERP diagnostics — $(date -u '+%F %T') UTC — host $(hostname) — sections: ${SECTIONS[*]} — log window $SINCE"
  want system && system_section
  want docker && docker_section
  want app && app_section
  want redis && redis_section
  want db && db_section
  want erpnext && erpnext_section
  want backup && backup_section
  want security && security_section

  section "SUMMARY"
  if [[ -s "$FINDINGS_FILE" ]]; then
    for level in FAIL WARN INFO; do grep "^\[$level\]" "$FINDINGS_FILE" || true; done
    echo
    echo "FAIL: $(grep -c '^\[FAIL\]' "$FINDINGS_FILE")  WARN: $(grep -c '^\[WARN\]' "$FINDINGS_FILE")  INFO: $(grep -c '^\[INFO\]' "$FINDINGS_FILE")"
  else
    echo "No findings."
  fi
}

main 2>&1 | redact | tee "$OUT"
chmod 600 "$OUT" 2>/dev/null || true
echo
echo "Saved: $OUT"
