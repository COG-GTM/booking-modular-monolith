#!/usr/bin/env bash
# Flip one bounded context's gateway cluster between the monolith (src/Api) and its standalone service.
# Runbook: docs/migration/cutover-runbook.md (AB-248).
#
#   route-flip.sh <identity|flight|passenger|booking> <monolith|service> [options]
#   route-flip.sh status [options]
#
# Options:
#   --target compose|k8s   Where the gateway runs (default: compose).
#   --dry-run              Print what would change; change nothing.
#   --no-smoke             Skip the post-flip health/smoke check.
#   --gateway-url URL      Gateway base URL for the smoke check (default: http://localhost:5000).
#   --namespace NS         Kubernetes namespace (k8s target, default: current context namespace).
#   --deployment NAME      Kubernetes gateway deployment (default: gateway).
#   --allow-unverified-upstream
#                          Accept a smoke response that carries no X-Gateway-Upstream header
#                          (gateway runs with UpstreamHeaderOptions:Enabled=false). Default: fail.
#
# EXTRA_COMPOSE_FILES=a.yaml:b.yaml adds compose files (compose target).
# Upstream addresses can be overridden with MONOLITH_URL and <CTX>_SERVICE_URL (e.g. FLIGHT_SERVICE_URL).
# Smoke credentials (required unless --no-smoke; no defaults): SMOKE_USERNAME, SMOKE_PASSWORD,
# SMOKE_CLIENT_ID, SMOKE_CLIENT_SECRET.
# Compose state: the chosen upstream per cluster is written to deployments/docker-compose/.env
# (git-ignored), which docker compose loads automatically, so a later plain `docker compose up`
# keeps the flipped routes. Override the location with ROUTE_FLIP_STATE_FILE.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$(cd "$SCRIPT_DIR/../docker-compose" && pwd)"
STATE_FILE="${ROUTE_FLIP_STATE_FILE:-$COMPOSE_DIR/.env}"
COMPOSE_FILES=(-f "$COMPOSE_DIR/docker-compose.services.yaml" -f "$COMPOSE_DIR/docker-compose.phase-a.yaml" -f "$COMPOSE_DIR/docker-compose.cutover.yaml")
# Extra compose files (colon-separated), e.g. a local override that remaps busy host ports.
IFS=: read -ra _extra <<<"${EXTRA_COMPOSE_FILES:-}"
for _f in "${_extra[@]}"; do COMPOSE_FILES+=(-f "$_f"); done
CONTEXTS=(identity flight passenger booking)

TARGET=compose
DRY_RUN=false
SMOKE=true
REQUIRE_UPSTREAM_HEADER=true
GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
NAMESPACE=""
DEPLOYMENT=gateway
SMOKE_TIMEOUT="${SMOKE_TIMEOUT:-120}"

MONOLITH_URL="${MONOLITH_URL:-http://booking_modular_monolith:80}"
IDENTITY_SERVICE_URL="${IDENTITY_SERVICE_URL:-http://identity-service:80}"
FLIGHT_SERVICE_URL="${FLIGHT_SERVICE_URL:-http://flight-service:80}"
PASSENGER_SERVICE_URL="${PASSENGER_SERVICE_URL:-http://passenger-service:5102}"
BOOKING_SERVICE_URL="${BOOKING_SERVICE_URL:-http://booking-service:80}"

log() { printf '[route-flip] %s\n' "$*" >&2; }
die() { log "ERROR: $*"; exit 1; }
usage() { sed -n '2,26p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit "${1:-0}"; }

upper() { tr '[:lower:]' '[:upper:]' <<<"$1"; }
state_var() { echo "GATEWAY_$(upper "$1")_UPSTREAM"; }
cluster_env() { echo "ReverseProxy__Clusters__$1__Destinations__monolith__Address"; }
service_url() { local v; v="$(upper "$1")_SERVICE_URL"; echo "${!v}"; }
authority() { sed -E 's#^[a-z]+://##; s#/.*$##' <<<"$1"; }

upstream_for() {
  case "$2" in
    monolith) echo "$MONOLITH_URL" ;;
    service) service_url "$1" ;;
    *) die "mode must be 'monolith' or 'service', got '$2'" ;;
  esac
}

run() {
  if $DRY_RUN; then
    printf '[route-flip] (dry-run) %q' "$1" >&2; shift; printf ' %q' "$@" >&2; printf '\n' >&2
  else
    "$@"
  fi
}

# --- compose ---------------------------------------------------------------------------------

state_get() {
  local var; var="$(state_var "$1")"
  [[ -f "$STATE_FILE" ]] && grep -E "^${var}=" "$STATE_FILE" | tail -1 | cut -d= -f2- || true
}

compose_current() {
  local id; id="$(docker compose "${COMPOSE_FILES[@]}" ps -q gateway 2>/dev/null || true)"
  if [[ -n "$id" ]]; then
    docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$id" | grep -E "^$(cluster_env "$1")=" | cut -d= -f2- || true
  fi
}

compose_flip() {
  local ctx="$1" url="$2" var tmp
  var="$(state_var "$ctx")"
  if $DRY_RUN; then
    log "(dry-run) would set ${var}=${url} in ${STATE_FILE}"
  else
    tmp="$(mktemp)"
    [[ -f "$STATE_FILE" ]] && grep -vE "^${var}=" "$STATE_FILE" >"$tmp" || true
    echo "${var}=${url}" >>"$tmp"
    mv "$tmp" "$STATE_FILE"
  fi
  # Only the gateway is recreated; services and the monolith keep running. The state file is the
  # compose project's .env, so it is also picked up by plain `docker compose up` later on.
  run docker compose "${COMPOSE_FILES[@]}" --env-file "$STATE_FILE" up -d --no-deps --wait gateway
}

# --- kubernetes ------------------------------------------------------------------------------

kubectl_ns() { if [[ -n "$NAMESPACE" ]]; then kubectl -n "$NAMESPACE" "$@"; else kubectl "$@"; fi; }

k8s_current() {
  kubectl_ns get "deploy/$DEPLOYMENT" \
    -o jsonpath="{.spec.template.spec.containers[0].env[?(@.name=='$(cluster_env "$1")')].value}" 2>/dev/null || true
}

k8s_flip() {
  local ctx="$1" url="$2" ns=()
  [[ -n "$NAMESPACE" ]] && ns=(-n "$NAMESPACE")
  run kubectl "${ns[@]}" set env "deploy/$DEPLOYMENT" "$(cluster_env "$ctx")=$url"
  run kubectl "${ns[@]}" rollout status "deploy/$DEPLOYMENT" --timeout=180s
}

# --- smoke -----------------------------------------------------------------------------------

http() { curl -s -o /tmp/route-flip-body.$$ -D /tmp/route-flip-headers.$$ -w '%{http_code}' "$@" || echo 000; }
header() { { grep -i "^$1:" /tmp/route-flip-headers.$$ || true; } | tail -1 | cut -d: -f2- | tr -d ' \r'; }

require_smoke_credentials() {
  local v missing=()
  for v in SMOKE_USERNAME SMOKE_PASSWORD SMOKE_CLIENT_ID SMOKE_CLIENT_SECRET; do
    [[ -n "${!v:-}" ]] || missing+=("$v")
  done
  ((${#missing[@]} == 0)) || die "smoke check needs ${missing[*]} (export them, or pass --no-smoke)"
}

wait_healthy() {
  local deadline=$((SECONDS + SMOKE_TIMEOUT)) code
  until [[ "$(http "$GATEWAY_URL/health")" == 200 ]]; do
    ((SECONDS < deadline)) || die "gateway $GATEWAY_URL/health not 200 after ${SMOKE_TIMEOUT}s"
    sleep 2
  done
  log "gateway healthy: GET $GATEWAY_URL/health -> 200"
}

token() {
  http -X POST "$GATEWAY_URL/connect/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode grant_type=password \
    --data-urlencode "client_id=${SMOKE_CLIENT_ID}" \
    --data-urlencode "client_secret=${SMOKE_CLIENT_SECRET}" \
    --data-urlencode "username=${SMOKE_USERNAME}" \
    --data-urlencode "password=${SMOKE_PASSWORD}" \
    --data-urlencode 'scope=booking-modular-monolith role' >/dev/null
  grep -q '"access_token"' /tmp/route-flip-body.$$ || return 1
  sed -E 's/.*"access_token":"([^"]+)".*/\1/' /tmp/route-flip-body.$$
}

smoke() {
  local ctx="$1" expected="$2" code path upstream deadline tok
  require_smoke_credentials
  wait_healthy
  case "$ctx" in
    identity) path=/.well-known/openid-configuration ;;
    flight) path=/api/v1/flight/get-available-flights ;;
    passenger) path=/api/v1/passenger/00000000-0000-0000-0000-000000000000 ;;
    booking) path=/api/v1/booking ;;
  esac
  deadline=$((SECONDS + SMOKE_TIMEOUT))
  while :; do
    # Every request is authenticated (identity's discovery document too): the token proves the
    # identity cluster issues tokens, and the gateway only emits X-Gateway-Upstream to authenticated callers.
    if tok="$(token)"; then
      if [[ "$ctx" == booking ]]; then
        code="$(http -X POST "$GATEWAY_URL$path" -H "Authorization: Bearer $tok" -H 'Content-Type: application/json' -d '{}')"
      else
        code="$(http "$GATEWAY_URL$path" -H "Authorization: Bearer $tok")"
      fi
      upstream="$(header X-Gateway-Upstream)"
    else
      code="token:$(tr -d '\n' </tmp/route-flip-body.$$ | cut -c1-120)" upstream=""
    fi
    # 2xx, or a 4xx produced by the module itself (e.g. 404 unknown passenger / 400 empty booking),
    # proves the request reached a live upstream. 401/403/5xx/000 fail the check. The upstream header
    # must name the expected destination; a missing header only passes with --allow-unverified-upstream.
    if [[ "$code" =~ ^(2..|400|404)$ ]]; then
      if [[ "$upstream" == "$(authority "$expected")" ]]; then
        break
      elif [[ -z "$upstream" ]] && ! $REQUIRE_UPSTREAM_HEADER; then
        log "WARNING: no X-Gateway-Upstream header; destination not verified (--allow-unverified-upstream)"
        break
      fi
    fi
    ((SECONDS < deadline)) || die "smoke failed: ${ctx} ${path} -> ${code}, X-Gateway-Upstream='${upstream}', expected '$(authority "$expected")'$([[ -z "$upstream" ]] && echo ' (enable UpstreamHeaderOptions:Enabled on the gateway, or pass --allow-unverified-upstream)')"
    sleep 3
  done
  log "smoke ok: ${ctx} ${path} -> ${code}, X-Gateway-Upstream=${upstream:-<unverified>}, correlationId=$(header correlationId)"
}

# --- main ------------------------------------------------------------------------------------

status() {
  printf '%-10s %-45s %s\n' CONTEXT UPSTREAM SERVED-BY
  for ctx in "${CONTEXTS[@]}"; do
    local cur
    if [[ "$TARGET" == k8s ]]; then cur="$(k8s_current "$ctx")"; else cur="$(compose_current "$ctx")"; fi
    local who=unknown
    [[ "$cur" == "$MONOLITH_URL" ]] && who=monolith
    [[ "$cur" == "$(service_url "$ctx")" ]] && who=service
    printf '%-10s %-45s %s\n' "$ctx" "${cur:-<not running>}" "$who"
  done
}

POSITIONAL=()
while (($#)); do
  case "$1" in
    --target) TARGET="$2"; shift 2 ;;
    --dry-run) DRY_RUN=true; shift ;;
    --no-smoke) SMOKE=false; shift ;;
    --gateway-url) GATEWAY_URL="$2"; shift 2 ;;
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --deployment) DEPLOYMENT="$2"; shift 2 ;;
    --allow-unverified-upstream) REQUIRE_UPSTREAM_HEADER=false; shift ;;
    -h|--help) usage 0 ;;
    -*) log "unknown option $1"; usage 1 ;;
    *) POSITIONAL+=("$1"); shift ;;
  esac
done
set -- "${POSITIONAL[@]}"
trap 'rm -f /tmp/route-flip-body.$$ /tmp/route-flip-headers.$$' EXIT

[[ "$TARGET" == compose || "$TARGET" == k8s ]] || die "--target must be compose or k8s"
(($#)) || usage 1
if [[ "$1" == status ]]; then status; exit 0; fi
(($# == 2)) || usage 1

CTX="$1" MODE="$2"
[[ " ${CONTEXTS[*]} " == *" $CTX "* ]] || die "unknown context '$CTX' (expected: ${CONTEXTS[*]})"
URL="$(upstream_for "$CTX" "$MODE")"

if [[ "$TARGET" == k8s ]]; then BEFORE="$(k8s_current "$CTX")"; else BEFORE="$(compose_current "$CTX")"; fi
log "${CTX}: ${BEFORE:-<unset>} -> ${URL} (${MODE}, target=${TARGET}$($DRY_RUN && echo ', dry-run'))"
log "rollback: $(basename "$0") ${CTX} $([[ "$MODE" == service ]] && echo monolith || echo service) --target ${TARGET}"

if [[ "$TARGET" == k8s ]]; then k8s_flip "$CTX" "$URL"; else compose_flip "$CTX" "$URL"; fi

if $DRY_RUN; then log "dry-run: no changes made"; exit 0; fi
if $SMOKE; then smoke "$CTX" "$URL"; fi
log "done: ${CTX} now served by ${MODE} (${URL})"
