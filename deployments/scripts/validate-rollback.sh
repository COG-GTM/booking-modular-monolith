#!/usr/bin/env bash
# Rollback validation for one gateway route (AB-248): flips <context> monolith -> service -> monolith
# through route-flip.sh and proves after every flip, from three independent signals, that gateway
# traffic moved:
#   1. the X-Gateway-Upstream response header (gateway's proxied destination),
#   2. the gateway's own YARP "Proxying to" log line,
#   3. request log lines in the monolith vs the service container (only the expected one grows).
#
# For identity it additionally checks that both Identity hosts publish the same signing key ids
# (JWKS), i.e. tokens issued before the flip stay valid after it.
#
# Prerequisite: the cut-over topology is running, e.g.
#   docker compose -f deployments/docker-compose/docker-compose.services.yaml \
#                  -f deployments/docker-compose/docker-compose.phase-a.yaml \
#                  -f deployments/docker-compose/docker-compose.cutover.yaml up -d --build --wait
# and SMOKE_USERNAME / SMOKE_PASSWORD / SMOKE_CLIENT_ID / SMOKE_CLIENT_SECRET are exported
# (route-flip.sh uses the same variables; local dev values are in docs/migration/cutover-runbook.md).
#
#   validate-rollback.sh [identity|flight|passenger]   (default: flight)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$(cd "$SCRIPT_DIR/../docker-compose" && pwd)"
COMPOSE=(docker compose -f "$COMPOSE_DIR/docker-compose.services.yaml" -f "$COMPOSE_DIR/docker-compose.phase-a.yaml" -f "$COMPOSE_DIR/docker-compose.cutover.yaml")
# Extra compose files (colon-separated), e.g. a local override that remaps busy host ports.
IFS=: read -ra _extra <<<"${EXTRA_COMPOSE_FILES:-}"
for _f in "${_extra[@]}"; do COMPOSE+=(-f "$_f"); done
GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
CTX="${1:-flight}"
REQUESTS="${REQUESTS:-5}"

case "$CTX" in
  identity) SERVICE=identity-service SERVICE_UPSTREAM=identity-service:80 PATH_UNDER_TEST=/.well-known/openid-configuration ;;
  flight) SERVICE=flight-service SERVICE_UPSTREAM=flight-service:80 PATH_UNDER_TEST=/api/v1/flight/get-available-flights ;;
  passenger) SERVICE=passenger-service SERVICE_UPSTREAM=passenger-service:5102 PATH_UNDER_TEST=/api/v1/passenger/00000000-0000-0000-0000-000000000000 ;;
  *) echo "only identity, flight or passenger are supported (GET endpoints)" >&2; exit 2 ;;
esac
MONOLITH=booking_modular_monolith
MONOLITH_URL="${MONOLITH_URL:-http://localhost:3001}"
IDENTITY_SERVICE_URL="${IDENTITY_SERVICE_URL:-http://localhost:5103}"
FAILURES=0

for v in SMOKE_USERNAME SMOKE_PASSWORD SMOKE_CLIENT_ID SMOKE_CLIENT_SECRET; do
  [[ -n "${!v:-}" ]] || { echo "$v must be set (smoke credentials, see the runbook)" >&2; exit 2; }
done

section() { printf '\n=== %s ===\n' "$*"; }
container() { "${COMPOSE[@]}" ps -q "$1"; }
log_hits() { docker logs --since "$2" "$(container "$1")" 2>&1 | grep -c "$PATH_UNDER_TEST" || true; }

token() {
  curl -s -X POST "$GATEWAY_URL/connect/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode grant_type=password --data-urlencode "client_id=$SMOKE_CLIENT_ID" \
    --data-urlencode "client_secret=$SMOKE_CLIENT_SECRET" --data-urlencode "username=$SMOKE_USERNAME" \
    --data-urlencode "password=$SMOKE_PASSWORD" --data-urlencode 'scope=booking-modular-monolith role' |
    sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}

probe() {
  local mode="$1" expected_upstream expected_container other_container since tok i code hdr
  case "$mode" in
    monolith) expected_upstream="$MONOLITH:80" expected_container=$MONOLITH other_container=$SERVICE ;;
    service) expected_upstream="$SERVICE_UPSTREAM" expected_container=$SERVICE other_container=$MONOLITH ;;
  esac

  section "flip $CTX -> $mode"
  "$SCRIPT_DIR/route-flip.sh" "$CTX" "$mode"
  "$SCRIPT_DIR/route-flip.sh" status

  since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  sleep 1
  tok="$(token)"
  section "$REQUESTS x GET $GATEWAY_URL$PATH_UNDER_TEST (expect upstream $expected_upstream)"
  for ((i = 1; i <= REQUESTS; i++)); do
    code="$(curl -s -o /tmp/validate-body.$$ -D /tmp/validate-headers.$$ -w '%{http_code}' \
      -H "Authorization: Bearer $tok" "$GATEWAY_URL$PATH_UNDER_TEST")"
    hdr="$(grep -i '^X-Gateway-Upstream:' /tmp/validate-headers.$$ | cut -d: -f2- | tr -d ' \r')"
    printf 'request %d: HTTP %s  X-Gateway-Upstream: %s  X-Trace-Id: %s  body: %.80s\n' "$i" "$code" "$hdr" \
      "$(grep -i '^X-Trace-Id:' /tmp/validate-headers.$$ | cut -d: -f2- | tr -d ' \r')" "$(tr -d '\n' </tmp/validate-body.$$)"
    if [[ "$code" != 200 && "$code" != 404 ]] || [[ "$hdr" != "$expected_upstream" ]]; then
      echo "FAIL: expected 200/404 from $expected_upstream" >&2
      FAILURES=$((FAILURES + 1))
    fi
  done
  sleep 2

  section "gateway log (YARP destination) since $since"
  docker logs --since "$since" "$(container gateway)" 2>&1 | grep -E "Proxying to .*$PATH_UNDER_TEST" | sed -E 's/^.*(Proxying to [^ ]+).*$/\1/' | sort | uniq -c

  section "backend request log lines for $PATH_UNDER_TEST since $since"
  local hits_expected hits_other
  hits_expected="$(log_hits "$expected_container" "$since")"
  hits_other="$(log_hits "$other_container" "$since")"
  printf '%-26s %s\n' "$expected_container" "$hits_expected" "$other_container" "$hits_other"
  if ((hits_expected == 0 || hits_other != 0)); then
    echo "FAIL: expected log lines only in $expected_container" >&2
    FAILURES=$((FAILURES + 1))
  else
    echo "OK: traffic for $CTX served by $expected_container only"
  fi
}

kids() { curl -sf "$1/.well-known/openid-configuration/jwks" | tr ',' '\n' | grep -o '"kid":"[^"]*"' | sort -u | tr '\n' ' '; }

signing_keys_shared() {
  section "signing keys: both Identity hosts must publish the same kids (token continuity across a flip)"
  local a b
  a="$(kids "$MONOLITH_URL")" b="$(kids "$IDENTITY_SERVICE_URL")"
  printf '%-26s %s\n' "$MONOLITH" "${a:-<none>}" "$SERVICE" "${b:-<none>}"
  if [[ -z "$a" || "$a" != "$b" ]]; then
    echo "FAIL: JWKS differ; a token issued by one host would be rejected after the flip (AuthOptions:SigningKeyPath must point both hosts at the same key)" >&2
    FAILURES=$((FAILURES + 1))
  else
    echo "OK: same signing key ids on both hosts"
  fi
}

trap 'rm -f /tmp/validate-body.$$ /tmp/validate-headers.$$' EXIT
section "rollback validation: $CTX monolith -> service -> monolith ($(date -u +%FT%TZ))"
[[ "$CTX" == identity ]] && signing_keys_shared
probe monolith
probe service
probe monolith

section "result"
if ((FAILURES == 0)); then
  echo "PASS: $CTX flipped monolith -> service -> monolith; every flip moved traffic as expected"
else
  echo "FAIL: $FAILURES check(s) failed"
  exit 1
fi
