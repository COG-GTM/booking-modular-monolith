#!/usr/bin/env bash
# Shared helpers for the AB-505 cut-over scripts. Sourced, not executed.
# shellcheck disable=SC2034

CUTOVER_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${CUTOVER_DIR}/../.." && pwd)"
COMPOSE_DIR="${COMPOSE_DIR:-${REPO_ROOT}/deployments/docker-compose}"
STATE_DIR="${STATE_DIR:-${CUTOVER_DIR}/.state}"
STATE_FILE="${STATE_FILE:-${STATE_DIR}/routes.env}"
GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-postgres}"
MONGO_CONTAINER="${MONGO_CONTAINER:-mongo}"
PGUSER_ADMIN="${PGUSER_ADMIN:-postgres}"
DRAIN_TIMEOUT_SECONDS="${DRAIN_TIMEOUT_SECONDS:-180}"
DRY_RUN="${DRY_RUN:-0}"

MODULES=(identity flight passenger booking)
MONOLITH_UPSTREAM="${MONOLITH_UPSTREAM:-http://booking_modular_monolith:80}"
FROZEN_UPSTREAM="${FROZEN_UPSTREAM:-http://127.0.0.1:9}"

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*" >&2; }
die() {
  log "ERROR: $*"
  exit 1
}

require_module() {
  local m
  for m in "${MODULES[@]}"; do [[ "$1" == "$m" ]] && return 0; done
  die "unknown module '$1' (expected one of: ${MODULES[*]})"
}

upper() { printf '%s' "$1" | tr '[:lower:]' '[:upper:]'; }
title() { printf '%s%s' "$(printf '%s' "${1:0:1}" | tr '[:lower:]' '[:upper:]')" "${1:1}"; }

service_container() { printf '%s-service' "$1"; }
service_upstream() { printf 'http://%s-service:80' "$1"; }

# --- databases per module ------------------------------------------------------------------------
monolith_db() { [[ "$1" == booking ]] || printf '%s_modular_monolith' "$1"; }
service_db() { [[ "$1" == booking ]] || printf '%s_service' "$1"; }
monolith_persist_db() { printf '%s_modular_monolith_persist_message' "$1"; }
service_persist_db() { printf '%s_service_persist_message' "$1"; }
monolith_read_db() { [[ "$1" == identity ]] || printf '%s_modular_monolith_read' "$1"; }
service_read_db() { [[ "$1" == identity ]] || printf '%s_service_read' "$1"; }

# --- route state ---------------------------------------------------------------------------------
state_init() {
  mkdir -p "${STATE_DIR}"
  [[ -f "${STATE_FILE}" ]] || : >"${STATE_FILE}"
}

state_get() {
  local key="$1" default="${2:-}" value
  value="$(grep -E "^${key}=" "${STATE_FILE}" 2>/dev/null | tail -n1 | cut -d= -f2-)"
  printf '%s' "${value:-$default}"
}

state_set() {
  local key="$1" value="$2" tmp
  state_init
  tmp="$(mktemp)"
  grep -vE "^${key}=" "${STATE_FILE}" >"${tmp}" || true
  printf '%s=%s\n' "${key}" "${value}" >>"${tmp}"
  sort -o "${STATE_FILE}" "${tmp}"
  rm -f "${tmp}"
  log "state: ${key}=${value}"
}

current_target() {
  local m="$1" upstream
  upstream="$(state_get "GATEWAY_$(upper "$m")_UPSTREAM" "${MONOLITH_UPSTREAM}")"
  case "${upstream}" in
    "${MONOLITH_UPSTREAM}") echo monolith ;;
    "$(service_upstream "$m")") echo service ;;
    "${FROZEN_UPSTREAM}") echo frozen ;;
    *) echo "custom(${upstream})" ;;
  esac
}

# --- compose -------------------------------------------------------------------------------------
compose() {
  local files=(-f "${COMPOSE_DIR}/docker-compose.yaml" -f "${CUTOVER_DIR}/docker-compose.cutover.yaml")
  state_init
  docker compose "${files[@]}" --env-file "${STATE_FILE}" "$@"
}

# Recreate only the given containers with the current state file and wait until healthy.
apply() {
  if [[ "${DRY_RUN}" == 1 ]]; then
    log "dry-run: would recreate $*"
    return 0
  fi
  log "recreating: $*"
  compose up -d --no-deps --no-build --wait "$@" >&2
}

# --- postgres / mongo ----------------------------------------------------------------------------
psql_admin() {
  local db="$1"
  shift
  docker exec -i "${POSTGRES_CONTAINER}" psql -U "${PGUSER_ADMIN}" -d "${db}" -v ON_ERROR_STOP=1 -X "$@"
}

pg_db_exists() {
  [[ "$(psql_admin postgres -Atc "select 1 from pg_database where datname = '$1'")" == 1 ]]
}

# Number of outbox/inbox/internal-command rows not yet processed (MessageStatus.Processed = 2).
pending_messages() {
  local db="$1" table
  pg_db_exists "${db}" || {
    echo 0
    return
  }
  table="$(psql_admin "${db}" -Atc "select format('%I.%I', table_schema, table_name) from information_schema.columns
           where column_name = 'message_status' and table_schema not in ('pg_catalog','information_schema') limit 1")"
  [[ -n "${table}" ]] || {
    echo 0
    return
  }
  psql_admin "${db}" -Atc "select count(*) from ${table} where message_status <> 2"
}

wait_for_drain() {
  local db="$1" deadline pending
  deadline=$(($(date +%s) + DRAIN_TIMEOUT_SECONDS))
  while :; do
    pending="$(pending_messages "${db}")"
    log "drain ${db}: ${pending} unprocessed message(s)"
    [[ "${pending}" == 0 ]] && return 0
    (($(date +%s) > deadline)) && die "${db} did not drain within ${DRAIN_TIMEOUT_SECONDS}s"
    sleep 5
  done
}

mongo_eval() {
  docker exec -i "${MONGO_CONTAINER}" mongosh --quiet --norc "$@"
}

# --- gateway checks ------------------------------------------------------------------------------
gateway_token() {
  curl -fsS -X POST "${GATEWAY_URL}/connect/token" \
    -d grant_type=password -d client_id=client -d client_secret=secret \
    -d username=samh -d password=Admin@123456 \
    --data-urlencode 'scope=booking-modular-monolith role' | jq -r .access_token
}

# Which upstream did the gateway proxy the most recent request for this path prefix to?
last_upstream() {
  local since="$1" path="$2"
  docker logs --since "${since}" gateway 2>&1 | grep -oE "Proxying to http://[^/ ]+${path}" | tail -n1 |
    sed -E 's#Proxying to http://([^/ ]+).*#\1#; s/:80$//'
}

# host[:port] as YARP logs it (default port omitted).
upstream_host() {
  local host="${1#http://}"
  printf '%s' "${host%:80}"
}
