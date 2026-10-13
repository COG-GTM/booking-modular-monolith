#!/usr/bin/env bash
# Decommission the monolith host (src/Api) from a running cut-over topology (AB-248, runbook §8).
#
#   decommission-monolith.sh [--target compose|k8s] [--check-only] [--quiet-hours N]
#                            [--namespace NS] [--gateway-url URL]
#
# 1. Preconditions (all must hold, the script fails closed on missing evidence):
#    a. every gateway cluster points at its standalone service (route-flip.sh status);
#    b. the gateway's log covers the whole quiet period (default 168 h = 7 days): the gateway
#       container/pod must have been running that long, otherwise the log cannot prove silence —
#       use the aggregated log store (Loki: {container="gateway"} |= "Proxying to http://booking_modular_monolith")
#       or wait; the log query itself must succeed;
#    c. that log shows zero requests proxied to the monolith cluster.
# 2. Removes the monolith host. compose: stop + rm booking_modular_monolith, then the gateway is
#    recreated from docker-compose.services.yaml + docker-compose.phase-a.yaml, i.e. WITHOUT the
#    cut-over overlay, so a later `docker compose -f docker-compose.services.yaml -f docker-compose.phase-a.yaml up -d`
#    cannot bring the monolith back — and WITH the Phase A file, which keeps flight-service on the
#    shared Flight store and identity-service on the shared signing key until Phase B is done per
#    context. k8s: scales the monolith deployment to 0 (remove it and the gateway's monolith cluster
#    env from the manifests next).
# 3. Verifies, with the monolith gone, that the gateway serves all four contexts: one authenticated
#    request per context through the gateway must succeed and the gateway log must show it proxied
#    to the service, with nothing proxied to the monolith.
#
# Smoke credentials: SMOKE_USERNAME, SMOKE_PASSWORD, SMOKE_CLIENT_ID, SMOKE_CLIENT_SECRET.
# EXTRA_COMPOSE_FILES=a.yaml:b.yaml adds compose files (compose target).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$(cd "$SCRIPT_DIR/../docker-compose" && pwd)"
# After the monolith is gone the deployment is services + phase-a (never the cut-over overlay again).
POST_DECOMMISSION=(docker compose -f "$COMPOSE_DIR/docker-compose.services.yaml" -f "$COMPOSE_DIR/docker-compose.phase-a.yaml")
COMPOSE=("${POST_DECOMMISSION[@]}" -f "$COMPOSE_DIR/docker-compose.cutover.yaml")
IFS=: read -ra _extra <<<"${EXTRA_COMPOSE_FILES:-}"
for _f in "${_extra[@]}"; do COMPOSE+=(-f "$_f"); POST_DECOMMISSION+=(-f "$_f"); done
CONTEXTS=(identity flight passenger booking)
MONOLITH=booking_modular_monolith
MONOLITH_DEPLOYMENT="${MONOLITH_DEPLOYMENT:-booking-modular-monolith}"
GATEWAY_POD_SELECTOR="${GATEWAY_POD_SELECTOR:-app=gateway}"
GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"

TARGET=compose
CHECK_ONLY=false
QUIET_HOURS="${QUIET_HOURS:-168}"
NAMESPACE=""
while (($#)); do
  case "$1" in
    --target) TARGET="$2"; shift 2 ;;
    --check-only) CHECK_ONLY=true; shift ;;
    --quiet-hours) QUIET_HOURS="$2"; shift 2 ;;
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --gateway-url) GATEWAY_URL="$2"; shift 2 ;;
    -h|--help) sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
[[ "$TARGET" == compose || "$TARGET" == k8s ]] || { echo "--target must be compose or k8s" >&2; exit 2; }
if ! $CHECK_ONLY; then
  for v in SMOKE_USERNAME SMOKE_PASSWORD SMOKE_CLIENT_ID SMOKE_CLIENT_SECRET; do
    [[ -n "${!v:-}" ]] || { echo "$v must be set (smoke credentials, see the runbook)" >&2; exit 2; }
  done
fi

section() { printf '\n=== %s ===\n' "$*"; }
fail() { echo "FAIL: $*" >&2; exit 1; }
kubectl_ns() { if [[ -n "$NAMESPACE" ]]; then kubectl -n "$NAMESPACE" "$@"; else kubectl "$@"; fi; }
quiet_seconds() { awk -v h="$QUIET_HOURS" 'BEGIN { printf "%d", h * 3600 }'; }

service_host() {
  case "$1" in
    identity) echo identity-service ;;
    flight) echo flight-service ;;
    passenger) echo passenger-service ;;
    booking) echo booking-service ;;
  esac
}

gateway_started_at() {
  if [[ "$TARGET" == k8s ]]; then
    kubectl_ns get pods -l "$GATEWAY_POD_SELECTOR" --sort-by=.status.startTime -o jsonpath='{.items[0].status.startTime}'
  else
    local id; id="$("${COMPOSE[@]}" ps -q gateway)"
    [[ -n "$id" ]] || return 1
    docker inspect -f '{{.State.StartedAt}}' "$id"
  fi
}

# Writes the gateway log for the quiet period to $1; fails if the log cannot be read.
gateway_logs_into() {
  if [[ "$TARGET" == k8s ]]; then
    kubectl_ns logs deploy/gateway --all-containers --since="$(quiet_seconds)s" >"$1" 2>/dev/null
  else
    local id; id="$("${COMPOSE[@]}" ps -q gateway)"
    [[ -n "$id" ]] || return 1
    docker logs --since "$(quiet_seconds)s" "$id" >"$1" 2>&1
  fi
}

TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT

section "precondition a: every cluster served by its service"
"$SCRIPT_DIR/route-flip.sh" status --target "$TARGET" ${NAMESPACE:+--namespace "$NAMESPACE"} | tee "$TMP/status"
for ctx in "${CONTEXTS[@]}"; do
  grep -qE "^${ctx} .* service$" "$TMP/status" || fail "$ctx is not served by its service; flip it first (route-flip.sh $ctx service)"
done

section "precondition b: gateway log covers the last ${QUIET_HOURS}h"
started="$(gateway_started_at)" || fail "gateway not found; cannot read its log"
started_s="$(date -d "$started" +%s)" now_s="$(date +%s)"
printf 'gateway running since %s (%d s); quiet period %d s\n' "$started" "$((now_s - started_s))" "$(quiet_seconds)"
((now_s - started_s >= $(quiet_seconds))) || fail "the gateway was (re)created inside the quiet period, so its log cannot prove the monolith was idle; check the aggregated log store for the full period or wait"

section "precondition c: no gateway request proxied to the monolith in the last ${QUIET_HOURS}h"
gateway_logs_into "$TMP/gateway.log" || fail "could not read the gateway log"
[[ -s "$TMP/gateway.log" ]] || fail "gateway log is empty; cannot prove the monolith was idle"
hits="$(grep -c "Proxying to http://${MONOLITH}" "$TMP/gateway.log" || true)"
echo "gateway log lines: $(wc -l <"$TMP/gateway.log"); requests proxied to ${MONOLITH}: ${hits}"
((hits == 0)) || fail "the monolith still received traffic (fallback routes or a stale client); investigate before decommissioning"

if $CHECK_ONLY; then echo "preconditions met (check-only, nothing changed)"; exit 0; fi

section "remove the monolith host"
if [[ "$TARGET" == k8s ]]; then
  kubectl_ns scale "deploy/$MONOLITH_DEPLOYMENT" --replicas=0
  kubectl_ns rollout status "deploy/$MONOLITH_DEPLOYMENT" --timeout=180s
else
  "${COMPOSE[@]}" stop "$MONOLITH"
  "${COMPOSE[@]}" rm -f "$MONOLITH"
  # Gateway without the cut-over overlay: no monolith dependency, clusters fixed to the services.
  # The Phase A file stays, so flight-service / identity-service keep their shared store and key.
  "${POST_DECOMMISSION[@]}" up -d --no-deps --wait gateway
  "${POST_DECOMMISSION[@]}" ps --format '{{.Name}} {{.Status}}'
fi

section "verify: gateway serves every context from its service with the monolith gone"
since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"; sleep 1
token="$(curl -s -X POST "$GATEWAY_URL/connect/token" -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode grant_type=password --data-urlencode "client_id=$SMOKE_CLIENT_ID" \
  --data-urlencode "client_secret=$SMOKE_CLIENT_SECRET" --data-urlencode "username=$SMOKE_USERNAME" \
  --data-urlencode "password=$SMOKE_PASSWORD" --data-urlencode 'scope=booking-modular-monolith role' |
  sed -nE 's/.*"access_token":"([^"]+)".*/\1/p')"
[[ -n "$token" ]] || fail "no token from $GATEWAY_URL/connect/token (identity-service via gateway)"
FAILURES=0
for ctx in "${CONTEXTS[@]}"; do
  case "$ctx" in
    identity) code="$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" "$GATEWAY_URL/.well-known/openid-configuration")" ;;
    flight) code="$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" "$GATEWAY_URL/api/v1/flight/get-available-flights")" ;;
    passenger) code="$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" "$GATEWAY_URL/api/v1/passenger/00000000-0000-0000-0000-000000000000")" ;;
    booking) code="$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d '{}' "$GATEWAY_URL/api/v1/booking")" ;;
  esac
  printf '%-10s HTTP %s\n' "$ctx" "$code"
  [[ "$code" =~ ^(2..|400|404)$ ]] || { echo "FAIL: $ctx did not get a module response through the gateway" >&2; FAILURES=$((FAILURES + 1)); }
done
sleep 2
if [[ "$TARGET" == k8s ]]; then
  kubectl_ns logs deploy/gateway --all-containers --since-time="$since" >"$TMP/verify.log" 2>/dev/null || fail "could not read the gateway log"
else
  docker logs --since "$since" "$("${POST_DECOMMISSION[@]}" ps -q gateway)" >"$TMP/verify.log" 2>&1 || fail "could not read the gateway log"
fi
grep -E "Proxying to" "$TMP/verify.log" | sed -E 's/^.*Proxying to (http:\/\/[^/]+).*$/\1/' | sort | uniq -c
for ctx in "${CONTEXTS[@]}"; do
  grep -q "Proxying to http://$(service_host "$ctx")" "$TMP/verify.log" || { echo "FAIL: no request proxied to $(service_host "$ctx")" >&2; FAILURES=$((FAILURES + 1)); }
done
if grep -q "Proxying to http://${MONOLITH}" "$TMP/verify.log"; then echo "FAIL: the gateway still proxies to the monolith" >&2; FAILURES=$((FAILURES + 1)); fi

section "result"
((FAILURES == 0)) || fail "$FAILURES check(s) failed after removing the monolith host; start it again with: ${COMPOSE[*]} up -d $MONOLITH"
echo "PASS: monolith host removed; the gateway serves identity, flight, passenger and booking from the services."
echo "From now on deploy with: ${POST_DECOMMISSION[*]} up -d   (never docker-compose.cutover.yaml again)."
echo "Next: Phase B per context (runbook §4), then the code-removal checklist in docs/migration/cutover-runbook.md §8."
