#!/usr/bin/env bash
# Rollback validation for one gateway route (AB-248): flips <context> monolith -> service -> monolith
# through route-flip.sh and proves after every flip, from three independent signals, that gateway
# traffic moved:
#   1. the X-Gateway-Upstream response header (gateway's proxied destination),
#   2. the gateway's own YARP "Proxying to" log line,
#   3. request log lines in the monolith vs the service container (only the expected one grows).
#
# Prerequisite: the cut-over topology is running, e.g.
#   docker compose -f deployments/docker-compose/docker-compose.services.yaml \
#                  -f deployments/docker-compose/docker-compose.cutover.yaml up -d --build --wait
#
#   validate-rollback.sh [flight|passenger]   (default: flight)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$(cd "$SCRIPT_DIR/../docker-compose" && pwd)"
COMPOSE=(docker compose -f "$COMPOSE_DIR/docker-compose.services.yaml" -f "$COMPOSE_DIR/docker-compose.cutover.yaml")
# Extra compose files (colon-separated), e.g. a local override that remaps busy host ports.
IFS=: read -ra _extra <<<"${EXTRA_COMPOSE_FILES:-}"
for _f in "${_extra[@]}"; do COMPOSE+=(-f "$_f"); done
GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
CTX="${1:-flight}"
REQUESTS="${REQUESTS:-5}"

case "$CTX" in
  flight) SERVICE=flight-service PATH_UNDER_TEST=/api/v1/flight/get-available-flights ;;
  passenger) SERVICE=passenger-service PATH_UNDER_TEST=/api/v1/passenger/00000000-0000-0000-0000-000000000000 ;;
  *) echo "only flight or passenger are supported (GET endpoints)" >&2; exit 2 ;;
esac
MONOLITH=booking_modular_monolith
FAILURES=0

section() { printf '\n=== %s ===\n' "$*"; }
container() { "${COMPOSE[@]}" ps -q "$1"; }
log_hits() { docker logs --since "$2" "$(container "$1")" 2>&1 | grep -c "$PATH_UNDER_TEST" || true; }

token() {
  curl -s -X POST "$GATEWAY_URL/connect/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode grant_type=password --data-urlencode client_id=client \
    --data-urlencode client_secret=secret --data-urlencode username=samh \
    --data-urlencode password=Admin@123456 --data-urlencode 'scope=booking-modular-monolith role' |
    sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}

probe() {
  local mode="$1" expected_upstream expected_container other_container since tok i code hdr
  case "$mode" in
    monolith) expected_upstream="$MONOLITH:80" expected_container=$MONOLITH other_container=$SERVICE ;;
    service)
      expected_container=$SERVICE other_container=$MONOLITH
      [[ "$CTX" == flight ]] && expected_upstream="flight-service:80" || expected_upstream="passenger-service:5102"
      ;;
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

trap 'rm -f /tmp/validate-body.$$ /tmp/validate-headers.$$' EXIT
section "rollback validation: $CTX monolith -> service -> monolith ($(date -u +%FT%TZ))"
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
