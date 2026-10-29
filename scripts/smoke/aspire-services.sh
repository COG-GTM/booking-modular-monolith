#!/usr/bin/env bash
set -euo pipefail

gateway="${1:-http://localhost:5000}"
gateway="${gateway%/}"
for tool in curl jq; do
    command -v "$tool" >/dev/null || { echo "Required command not found: $tool" >&2; exit 1; }
done

token_response="$(curl --fail-with-body --silent --show-error --max-time 30 --data 'grant_type=password&client_id=client&client_secret=secret&scope=booking-modular-monolith%20flight-api%20passenger-api%20booking-api%20identity-api&username=samh&password=Admin%40123456' "$gateway/connect/token")"
token="$(jq -er '.access_token' <<<"$token_response")"
echo 'POST /connect/token: 200 (admin token)'

request() {
    curl --fail-with-body --silent --show-error --max-time 30 --config <(printf 'header = "Authorization: Bearer %s"\n' "$token") -H 'Content-Type: application/json' "$@"
}

suffix="$(date +%s)$RANDOM"
username="aspire$suffix"
passport="${suffix: -10}"
registration="$(jq -n --arg username "$username" --arg passport "$passport" '{firstName:"Aspire",lastName:"Smoke",username:$username,email:($username+"@example.com"),password:"Admin@123456",confirmPassword:"Admin@123456",passportNumber:$passport}')"
user="$(request -d "$registration" "$gateway/api/v1/identity/register-user")"
user_id="$(jq -er '.id' <<<"$user")"
echo "POST /api/v1/identity/register-user: 200 user=$user_id passport=$passport"

deadline=$((SECONDS + 150))
completion="$(jq -n --arg passport "$passport" '{passportNumber:$passport,passengerType:1,age:30}')"
while true; do
    completed="$(curl --silent --show-error --max-time 15 --config <(printf 'header = "Authorization: Bearer %s"\n' "$token") -H 'Content-Type: application/json' -d "$completion" -w '\n%{http_code}' "$gateway/api/v1/passenger/complete-registration")"
    status="${completed##*$'\n'}"
    if [[ "$status" == 200 ]]; then break; fi
    if [[ "$status" != 400 ]] || ! jq -e '.title == "PassengerNotExist"' >/dev/null <<<"${completed%$'\n'*}"; then printf 'Complete registration failed: %s\n' "$completed" >&2; exit 1; fi
    if (( SECONDS >= deadline )); then
        printf 'UserCreated delivery timed out: %s\n' "$completed" >&2
        exit 1
    fi
    sleep 3
done
passenger_id="$(jq -er '.passengerDto.id' <<<"${completed%$'\n'*}")"
echo "POST /api/v1/passenger/complete-registration: 200 passenger=$passenger_id"
deadline=$((SECONDS + 150))
while true; do
    passenger="$(curl --silent --show-error --max-time 15 --config <(printf 'header = "Authorization: Bearer %s"\n' "$token") -w '\n%{http_code}' "$gateway/api/v1/passenger/$passenger_id")"
    status="${passenger##*$'\n'}"
    if [[ "$status" == 200 ]]; then break; fi
    if [[ "$status" != 404 ]] || (( SECONDS >= deadline )); then
        printf 'Passenger projection failed: %s\n' "$passenger" >&2
        exit 1
    fi
    sleep 3
done
echo "GET /api/v1/passenger/$passenger_id: 200 (UserCreated projected through RabbitMQ)"

flight_id="3c5c0000-97c6-fc34-2eb9-08db322230c9"
flight="$(request "$gateway/api/v1/flight/$flight_id")"
jq -e . >/dev/null <<<"$flight"
echo "GET /api/v1/flight/$flight_id: 200"
booking_request="$(jq -n --arg passenger "$passenger_id" --arg flight "$flight_id" '{passengerId:$passenger,flightId:$flight,description:"Aspire services gateway smoke"}')"
booking="$(request -d "$booking_request" "$gateway/api/v1/booking")"
jq -e '.id != null' >/dev/null <<<"$booking"
echo "POST /api/v1/booking: 200 $booking"
