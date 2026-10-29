#!/usr/bin/env bash
set -euo pipefail

GATEWAY="${GATEWAY:-http://localhost:5000}"
FLIGHT_ID="3c5c0000-97c6-fc34-2eb9-08db322230c9"
TOKEN=""
HTTP_STATUS=""
HTTP_BODY=""

for tool in curl jq docker; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$tool" >&2
        exit 1
    fi
done

request() {
    local response
    response="$(curl --silent --show-error --write-out $'\n%{http_code}' "$@")"
    HTTP_STATUS="${response##*$'\n'}"
    HTTP_BODY="${response%$'\n'*}"
}

authorized_request() {
    request --config <(printf 'header = "Authorization: Bearer %s"\n' "$TOKEN") "$@"
}

assert_status() {
    local expected="$1"
    local message="$2"
    if [[ "$HTTP_STATUS" != "$expected" ]]; then
        printf '%s: expected HTTP %s, got %s\n%s\n' "$message" "$expected" "$HTTP_STATUS" "$HTTP_BODY" >&2
        exit 1
    fi
    printf '%s: HTTP %s\n' "$message" "$HTTP_STATUS"
}

until curl --fail --silent --show-error "$GATEWAY/health/ready" >/dev/null 2>&1; do
    printf 'Waiting for gateway /health/ready...\n'
    sleep 2
done
printf 'Gateway /health/ready: HTTP 200\n'

request "$GATEWAY/api/v1/flight/$FLIGHT_ID"
assert_status 401 "Unauthenticated flight request"

request \
    --request POST \
    --header 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=password' \
    --data-urlencode 'client_id=client' \
    --data-urlencode 'client_secret=secret' \
    --data-urlencode 'scope=booking-modular-monolith flight-api passenger-api booking-api identity-api' \
    --data-urlencode 'username=samh' \
    --data-urlencode 'password=Admin@123456' \
    "$GATEWAY/connect/token"
assert_status 200 "Password grant via gateway"
TOKEN="$(jq --exit-status --raw-output '.access_token' <<<"$HTTP_BODY")"

suffix="$(date +%s%N)-$RANDOM"
passport_number="P$(printf '%04x%04x' "$RANDOM" "$RANDOM")"
register_body="$(jq -n \
    --arg firstName Compose \
    --arg lastName Smoke \
    --arg username "compose-$suffix" \
    --arg email "compose-$suffix@example.test" \
    --arg password "ComposeSmoke123!" \
    --arg passportNumber "$passport_number" \
    '{
      firstName: $firstName,
      lastName: $lastName,
      username: $username,
      email: $email,
      password: $password,
      confirmPassword: $password,
      passportNumber: $passportNumber
    }')"
authorized_request \
    --request POST \
    --header 'Content-Type: application/json' \
    --data-binary "$register_body" \
    "$GATEWAY/api/v1/identity/register-user"
assert_status 200 "Register user"
user_id="$(jq --exit-status --raw-output '.id // .Id' <<<"$HTTP_BODY")"
printf 'Registered identity user %s with passport %s\n' "$user_id" "$passport_number"

passenger_count=""
for attempt in $(seq 1 60); do
    if passenger_count="$(docker exec mongo mongosh --quiet passenger_service_read --eval \
        "db.passenger.countDocuments({passportNumber: '$passport_number'})" 2>/dev/null)" &&
        [[ "$passenger_count" =~ ^[0-9]+$ ]] &&
        (( passenger_count > 0 )); then
        break
    fi
    if (( attempt == 60 )); then
        printf 'Passenger projection did not receive passport %s within 30 seconds\n' "$passport_number" >&2
        exit 1
    fi
    sleep 0.5
done
printf 'Passenger received UserCreated event: %s\n' "$passport_number"

complete_body="$(jq -n --arg passportNumber "$passport_number" \
    '{passportNumber: $passportNumber, passengerType: 1, age: 30}')"
authorized_request \
    --request POST \
    --header 'Content-Type: application/json' \
    --data-binary "$complete_body" \
    "$GATEWAY/api/v1/passenger/complete-registration"
assert_status 200 "Complete passenger registration"
passenger_id="$(jq --exit-status --raw-output '(.passengerDto // .PassengerDto).id // (.passengerDto // .PassengerDto).Id' <<<"$HTTP_BODY")"
printf 'Registered passenger %s\n' "$passenger_id"

authorized_request "$GATEWAY/api/v1/flight/$FLIGHT_ID"
assert_status 200 "Get seeded flight"
flight_id="$(jq --exit-status --raw-output '(.flightDto // .FlightDto).id // (.flightDto // .FlightDto).Id' <<<"$HTTP_BODY")"

authorized_request "$GATEWAY/api/v1/flight/get-available-seats/$FLIGHT_ID"
assert_status 200 "Get available seats before booking"
seats_before="$(jq --exit-status '(.seatDtos // .SeatDtos) | length' <<<"$HTTP_BODY")"
if (( seats_before == 0 )); then
    printf 'Seeded flight %s has no available seats\n' "$FLIGHT_ID" >&2
    exit 1
fi

if booking_count_before="$(docker exec mongo mongosh --quiet booking_service_read --eval \
    'db.booking.countDocuments({})' 2>/dev/null)" &&
    [[ "$booking_count_before" =~ ^[0-9]+$ ]]; then
    :
else
    booking_count_before=""
fi

booking_body="$(jq -n --arg passengerId "$passenger_id" --arg flightId "$flight_id" \
    '{passengerId: $passengerId, flightId: $flightId, description: "Compose E2E booking"}')"
authorized_request \
    --request POST \
    --header 'Content-Type: application/json' \
    --data-binary "$booking_body" \
    "$GATEWAY/api/v1/booking"
assert_status 200 "Create booking through gateway"
printf 'Booking response: %s\n' "$(jq -c . <<<"$HTTP_BODY")"

booking_effect_confirmed=false
read_model_confirmed=false
for attempt in $(seq 1 60); do
    authorized_request "$GATEWAY/api/v1/flight/get-available-seats/$FLIGHT_ID"
    if [[ "$HTTP_STATUS" == 200 ]]; then
        seats_after="$(jq --exit-status '(.seatDtos // .SeatDtos) | length' <<<"$HTTP_BODY")"
        if (( seats_after < seats_before )); then
            booking_effect_confirmed=true
        fi
    fi

    if [[ "$read_model_confirmed" == false && -n "$booking_count_before" ]] &&
        booking_count_after="$(docker exec mongo mongosh --quiet booking_service_read --eval \
            'db.booking.countDocuments({})' 2>/dev/null)" &&
        [[ "$booking_count_after" =~ ^[0-9]+$ ]] &&
        (( booking_count_after > booking_count_before )); then
        printf 'Booking read model appeared in booking_service_read: %s -> %s\n' \
            "$booking_count_before" "$booking_count_after"
        read_model_confirmed=true
    elif [[ -z "$booking_count_before" ]]; then
        read_model_confirmed=true
    fi

    if [[ "$booking_effect_confirmed" == true && "$read_model_confirmed" == true ]]; then
        printf 'Booking reserved a seat: available seats %s -> %s\n' "$seats_before" "$seats_after"
        break
    fi

    if (( attempt == 60 )); then
        printf 'Booking seat reservation or read model was not visible after 30 seconds\n' >&2
        exit 1
    fi
    sleep 0.5
done
[[ "$booking_effect_confirmed" == true ]]

if [[ -z "$booking_count_before" ]]; then
    printf 'Booking read-model count check skipped: Mongo collection was not reachable before booking\n'
fi

request "$GATEWAY/__compose-smoke-unmatched"
assert_status 404 "Unmatched gateway path"
