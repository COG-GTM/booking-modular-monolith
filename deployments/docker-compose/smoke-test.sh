#!/usr/bin/env bash
# End-to-end smoke test for the multi-service topology (docker-compose.yaml). Every request goes
# through the YARP gateway, so it exercises gateway routing, Identity token issuance, the
# Identity -> Passenger RabbitMQ integration event and Booking -> Flight/Passenger gRPC calls.
#
#   docker compose -f deployments/docker-compose/docker-compose.yaml up -d --build
#   deployments/docker-compose/smoke-test.sh
#
# Env: GATEWAY_URL (default http://localhost:5000), TIMEOUT_SECONDS (default 180).
# Requires curl and jq. Exits non-zero on the first failed step.
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-180}"
API="$GATEWAY_URL/api/v1.0"

# Seeded by the Flight service (src/Modules/Flight/src/Data/Seed/InitialData.cs).
AIRCRAFT_ID="3c5c0000-97c6-fc34-fcd3-08db322230c8"
DEPARTURE_AIRPORT_ID="3c5c0000-97c6-fc34-a0cb-08db322230c8"
ARRIVE_AIRPORT_ID="3c5c0000-97c6-fc34-fc3c-08db322230c8"

for tool in curl jq; do
  command -v "$tool" >/dev/null || { echo "smoke-test: '$tool' is required" >&2; exit 2; }
done

RESPONSE_BODY="$(mktemp)"
trap 'rm -f "$RESPONSE_BODY"' EXIT

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }

# request METHOD URL [JSON_BODY] -> prints HTTP status, body in $RESPONSE_BODY
request() {
  local method="$1" url="$2" body="${3:-}"
  local args=(-sS -o "$RESPONSE_BODY" -w '%{http_code}' -X "$method" "$url")
  [[ -n "${TOKEN:-}" ]] && args+=(-H "Authorization: Bearer $TOKEN")
  [[ -n "$body" ]] && args+=(-H 'Content-Type: application/json' -d "$body")
  curl "${args[@]}" || echo 000
}

# expect STATUS METHOD URL [JSON_BODY]: one call, fail unless the status matches
expect() {
  local want="$1"; shift
  local got
  got="$(request "$@")"
  [[ "$got" == "$want" ]] || fail "$1 $2 -> HTTP $got (expected $want): $(cat "$RESPONSE_BODY")"
  echo "$1 ${2#"$GATEWAY_URL"} -> HTTP $got"
}

# eventually STATUS METHOD URL [JSON_BODY]: retry until the status matches or the timeout hits
eventually() {
  local want="$1"; shift
  local deadline=$((SECONDS + TIMEOUT_SECONDS)) got
  while :; do
    got="$(request "$@")"
    if [[ "$got" == "$want" ]]; then
      echo "$1 ${2#"$GATEWAY_URL"} -> HTTP $got"
      return 0
    fi
    ((SECONDS < deadline)) || fail "$1 $2 never returned $want (last HTTP $got: $(cat "$RESPONSE_BODY"))"
    sleep 3
  done
}

suffix="$(date +%s)$RANDOM"
passport="P${suffix: -9}"
flight_number="SM${suffix: -6}"
departure="$(date -u -d '+30 days' +%Y-%m-%dT10:00:00Z)"
arrival="$(date -u -d '+30 days' +%Y-%m-%dT12:00:00Z)"

step "Gateway health ($GATEWAY_URL/health)"
eventually 200 GET "$GATEWAY_URL/health"

step "Token from identity-service via gateway (POST /connect/token)"
deadline=$((SECONDS + TIMEOUT_SECONDS))
until status="$(curl -sS -o "$RESPONSE_BODY" -w '%{http_code}' -X POST "$GATEWAY_URL/connect/token" \
  -d grant_type=password -d client_id=client -d client_secret=secret \
  -d username=samh -d password=Admin@123456 \
  --data-urlencode 'scope=booking-modular-monolith role' || echo 000)" && [[ "$status" == 200 ]]; do
  ((SECONDS < deadline)) || fail "POST /connect/token -> HTTP $status: $(cat "$RESPONSE_BODY")"
  sleep 3
done
TOKEN="$(jq -r .access_token "$RESPONSE_BODY")"
issuer="$(jq -rR 'split(".")[1] | gsub("-"; "+") | gsub("_"; "/")
  | . + ("=" * ((4 - length % 4) % 4)) | @base64d | fromjson | .iss' <<<"$TOKEN")"
echo "POST /connect/token -> HTTP 200 (iss=$issuer)"

step "Register user (Identity) -> UserCreated published to RabbitMQ"
expect 200 POST "$API/identity/register-user" "$(jq -nc --arg u "smoke$suffix" --arg p "$passport" \
  '{firstName:"Smoke", lastName:"Test", username:$u, email:($u + "@example.com"),
    password:"Smoke@12345", confirmPassword:"Smoke@12345", passportNumber:$p}')"
jq -c . "$RESPONSE_BODY"

step "Passenger created from the RabbitMQ event; complete its registration (Passenger)"
eventually 200 POST "$API/passenger/complete-registration" \
  "$(jq -nc --arg p "$passport" '{passportNumber:$p, passengerType:1, age:30}')"
passenger_id="$(jq -r '.passengerDto.id' "$RESPONSE_BODY")"
jq -c . "$RESPONSE_BODY"

step "Create flight + seat (Flight)"
expect 201 POST "$API/flight" "$(jq -nc --arg n "$flight_number" --arg d "$departure" --arg a "$arrival" \
  --arg ac "$AIRCRAFT_ID" --arg da "$DEPARTURE_AIRPORT_ID" --arg aa "$ARRIVE_AIRPORT_ID" \
  '{flightNumber:$n, aircraftId:$ac, departureAirportId:$da, departureDate:$d, arriveDate:$a,
    arriveAirportId:$aa, durationMinutes:120, flightDate:$d, status:1, price:800}')"
flight_id="$(jq -r .id "$RESPONSE_BODY")"
jq -c . "$RESPONSE_BODY"
expect 200 POST "$API/flight/seat" \
  "$(jq -nc --arg f "$flight_id" '{seatNumber:"12A", type:1, class:3, flightId:$f}')"
jq -c . "$RESPONSE_BODY"

step "Create booking (Booking -> Flight/Passenger over gRPC)"
eventually 200 POST "$API/booking" "$(jq -nc --arg p "$passenger_id" --arg f "$flight_id" \
  '{passengerId:$p, flightId:$f, description:"AB-503 smoke test"}')"
booking_id="$(jq -r .id "$RESPONSE_BODY")"
jq -c . "$RESPONSE_BODY"

step "Seat reserved by Booking's gRPC ReserveSeat call (Flight read model, async projection)"
# 12A is the flight's only seat, so once reserved Flight answers 400 AllSeatsFullException.
seat_reserved() {
  local got
  got="$(request GET "$API/flight/get-available-seats/$flight_id")"
  case "$got" in
    200) jq -e '.seatDtos | map(select(.seatNumber == "12A")) | length == 0' "$RESPONSE_BODY" >/dev/null ;;
    400) jq -e '.title == "AllSeatsFullException"' "$RESPONSE_BODY" >/dev/null ;;
    *) return 1 ;;
  esac
}
deadline=$((SECONDS + TIMEOUT_SECONDS))
until seat_reserved; do
  ((SECONDS < deadline)) || fail "seat 12A still available after booking $booking_id: $(cat "$RESPONSE_BODY")"
  sleep 3
done
echo "GET /api/v1.0/flight/get-available-seats/$flight_id -> $(jq -c '{title, detail}' "$RESPONSE_BODY")"

printf '\nSMOKE TEST PASSED: passenger=%s flight=%s booking=%s\n' "$passenger_id" "$flight_id" "$booking_id"
