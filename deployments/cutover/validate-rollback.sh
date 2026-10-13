#!/usr/bin/env bash
# Prove that a route flip is reversible without losing data (AB-505 acceptance: "rollback validated
# for at least one route flip"). Runs against the cut-over compose topology:
#
#   flip-route.sh up && validate-rollback.sh            # default: flight
#
#   1. on the monolith:  create flight A through the gateway, read it back
#   2. flip -> service:  flight A is readable from flight-service (data copied), create flight B
#   3. flip -> monolith: flights A and B are readable from the monolith (service writes copied back)
#
# Flight is the reference context: it has a Postgres write store, a Mongo read model fed by internal
# commands through the outbox, and a gRPC surface used by Booking - every moving part of a flip.
set -euo pipefail
# shellcheck source=deployments/cutover/lib.sh
source "$(dirname "$0")/lib.sh"

API="${GATEWAY_URL}/api/v1.0"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-120}"
AIRCRAFT_ID="3c5c0000-97c6-fc34-fcd3-08db322230c8"
DEPARTURE_AIRPORT_ID="3c5c0000-97c6-fc34-a0cb-08db322230c8"
ARRIVE_AIRPORT_ID="3c5c0000-97c6-fc34-fc3c-08db322230c8"
BODY="$(mktemp)"
trap 'rm -f "${BODY}"' EXIT

step() { printf '\n==> %s\n' "$*"; }

TOKEN=""
refresh_token() { TOKEN="$(gateway_token)" || die "could not get a token through the gateway"; }

create_flight() {
  local number="$1" departure arrival code
  departure="$(date -u -d '+30 days' +%Y-%m-%dT10:00:00Z)"
  arrival="$(date -u -d '+30 days' +%Y-%m-%dT12:00:00Z)"
  code="$(curl -sS -o "${BODY}" -w '%{http_code}' -X POST "${API}/flight" \
    -H "Authorization: Bearer ${TOKEN}" -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg n "${number}" --arg d "${departure}" --arg a "${arrival}" \
      --arg ac "${AIRCRAFT_ID}" --arg da "${DEPARTURE_AIRPORT_ID}" --arg aa "${ARRIVE_AIRPORT_ID}" \
      '{flightNumber:$n, aircraftId:$ac, departureAirportId:$da, departureDate:$d, arriveDate:$a,
        arriveAirportId:$aa, durationMinutes:120, flightDate:$d, status:1, price:800}')")"
  [[ "${code}" == 201 ]] || die "POST /flight ${number} -> HTTP ${code}: $(cat "${BODY}")"
  jq -r .id "${BODY}"
}

# GET /flight/{id} reads the Mongo read model, which the owning host's outbox fills asynchronously.
expect_flight() {
  local id="$1" expected_upstream="$2" deadline code since served
  deadline=$((SECONDS + TIMEOUT_SECONDS))
  while :; do
    since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    code="$(curl -sS -o "${BODY}" -w '%{http_code}' -H "Authorization: Bearer ${TOKEN}" "${API}/flight/${id}")"
    [[ "${code}" == 200 ]] && break
    ((SECONDS < deadline)) || die "GET /flight/${id} -> HTTP ${code}: $(cat "${BODY}")"
    sleep 3
  done
  sleep 1
  served="$(last_upstream "${since}" "/api/v1.0/flight/${id}")"
  [[ "${served}" == "${expected_upstream}" ]] ||
    die "GET /flight/${id} served by '${served}', expected '${expected_upstream}'"
  printf 'GET /api/v1.0/flight/%s -> HTTP 200 from %s: %s\n' "${id}" "${served}" \
    "$(jq -c '.flightDto | {flightNumber, status}' "${BODY}")"
}

suffix="$(date +%s)"
monolith_host="$(upstream_host "${MONOLITH_UPSTREAM}")"
service_host="$(upstream_host "$(service_upstream flight)")"

step "Precondition: flight is routed to the monolith"
"${CUTOVER_DIR}/flip-route.sh" status
[[ "$(current_target flight)" == monolith ]] || "${CUTOVER_DIR}/flip-route.sh" flight monolith

step "1. Write flight A through the gateway while the monolith owns flight"
refresh_token
flight_a="$(create_flight "RA${suffix: -6}")"
echo "POST /api/v1.0/flight -> HTTP 201 id=${flight_a}"
expect_flight "${flight_a}" "${monolith_host}"

step "2. Flip flight -> service"
"${CUTOVER_DIR}/flip-route.sh" flight service
refresh_token
expect_flight "${flight_a}" "${service_host}"
flight_b="$(create_flight "RB${suffix: -6}")"
echo "POST /api/v1.0/flight -> HTTP 201 id=${flight_b} (written by flight-service)"
expect_flight "${flight_b}" "${service_host}"

step "3. Roll back flight -> monolith"
"${CUTOVER_DIR}/flip-route.sh" flight monolith
refresh_token
expect_flight "${flight_a}" "${monolith_host}"
expect_flight "${flight_b}" "${monolith_host}"

step "Final route state"
"${CUTOVER_DIR}/flip-route.sh" status
printf '\nROLLBACK VALIDATED: flight A=%s (monolith write) and B=%s (service write) survive %s\n' \
  "${flight_a}" "${flight_b}" "monolith -> service -> monolith"
