#!/usr/bin/env bash

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/cutover/lib.sh
source "$SCRIPT_DIR/lib.sh"

context="${1:-flight}"
[[ "$context" == flight ]] || { printf 'The live rollback drill currently supports flight only.\n' >&2; exit 2; }
load_state
initial_owner="$(owner_for "$context")"
flight_id=

restore_initial_owner() {
  local result=$?
  trap - EXIT
  if [[ "$result" -ne 0 ]]; then
    printf 'Drill failed; restoring initial %s owner (%s).\n' "$context" "$initial_owner" >&2
    load_state || exit "$result"
    current_owner="$(owner_for "$context")"
    if [[ -n "${FLIP_IN_PROGRESS:-}" ]]; then
      cutover_target="${initial_owner}"
      "$SCRIPT_DIR/cutover.sh" flip "$context" "$cutover_target" || true
    elif [[ "$current_owner" != "$initial_owner" ]]; then
      "$SCRIPT_DIR/cutover.sh" flip "$context" "$initial_owner" || true
    fi
  fi
  exit "$result"
}
trap restore_initial_owner EXIT

"$SCRIPT_DIR/cutover.sh" flip "$context" service
token="$(cutover_token)"
flight_number="AB505$(date +%H%M%S)"
departure_date="$(date -u -d '+2 days' +%Y-%m-%dT%H:%M:%SZ)"
arrival_date="$(date -u -d '+2 days +2 hours' +%Y-%m-%dT%H:%M:%SZ)"
flight_response="$(curl --silent --show-error --config - <<CURL
url = "http://localhost:5000/api/v1.0/flight"
request = "POST"
header = "Authorization: Bearer $token"
header = "Content-Type: application/json"
data = "{\"flightNumber\":\"$flight_number\",\"aircraftId\":\"3c5c0000-97c6-fc34-fcd3-08db322230c8\",\"departureAirportId\":\"3c5c0000-97c6-fc34-a0cb-08db322230c8\",\"departureDate\":\"$departure_date\",\"arriveDate\":\"$arrival_date\",\"arriveAirportId\":\"3c5c0000-97c6-fc34-fc3c-08db322230c8\",\"durationMinutes\":120,\"flightDate\":\"$departure_date\",\"status\":1,\"price\":250}"
CURL
)"
flight_id="$(jq -er '.id' <<<"$flight_response")"
printf 'Created flight %s (%s) while flight-service owned the route.\n' "$flight_number" "$flight_id"

read_from_gateway() {
  curl --silent --show-error --config - <<CURL
url = "http://localhost:5000/api/v1.0/flight/$flight_id"
header = "Authorization: Bearer $token"
CURL
}

for attempt in {1..60}; do
  if read_from_gateway | jq -e --arg flight_id "$flight_id" 'any(.. | objects; .id? == $flight_id)' >/dev/null; then
    break
  fi
  if [[ "$attempt" == 60 ]]; then
    printf 'Created flight did not appear in the read model after 60 seconds.\n' >&2
    exit 1
  fi
  sleep 1
done

"$SCRIPT_DIR/cutover.sh" flip "$context" monolith
token="$(cutover_token)"
for attempt in {1..60}; do
  if read_from_gateway | jq -e --arg flight_id "$flight_id" 'any(.. | objects; .id? == $flight_id)' >/dev/null; then
    break
  fi
  if [[ "$attempt" == 60 ]]; then
    printf 'Flight %s was not visible through the monolith after reverse synchronization.\n' "$flight_id" >&2
    exit 1
  fi
  sleep 1
done
printf 'Verified reverse synchronization: flight %s is visible through the monolith.\n' "$flight_id"

"$SCRIPT_DIR/cutover.sh" flip "$context" service
printf 'Rollback drill complete; flight is returned to service ownership.\n'
