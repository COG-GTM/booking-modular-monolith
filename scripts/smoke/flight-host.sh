#!/usr/bin/env bash
set -euo pipefail

FLIGHT_HTTP="${FLIGHT_HTTP:-http://localhost:5102}"
FLIGHT_GRPC="${FLIGHT_GRPC:-localhost:5103}"
TOKEN_URL="${TOKEN_URL:-http://localhost:3001/connect/token}"

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
PROTO_DIR="$REPO_ROOT/src/Contracts/Contracts.Grpc/Protos"
FLIGHT_ID="3c5c0000-97c6-fc34-2eb9-08db322230c9"

export PATH="$HOME/.local/bin:$PATH"

for tool in curl grpcurl jq; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$tool" >&2
        exit 1
    fi
done

for endpoint in /health/live /health/ready; do
    curl --fail --silent --show-error "$FLIGHT_HTTP$endpoint" >/dev/null
    printf 'HTTP GET %s: OK\n' "$endpoint"
done

token_response="$(
    curl --fail --silent --show-error --data @- "$TOKEN_URL" <<'FORM'
grant_type=password&client_id=client&client_secret=secret&scope=booking-modular-monolith%20flight-api&username=samh&password=Admin%40123456
FORM
)"
token="$(jq --exit-status --raw-output '.access_token' <<<"$token_response")"

curl --fail --silent --show-error \
    --config <(printf 'header = "Authorization: Bearer %s"\n' "$token") \
    "$FLIGHT_HTTP/api/v1/flight/$FLIGHT_ID" >/dev/null
printf 'HTTP GET seeded flight %s: OK\n' "$FLIGHT_ID"

grpcurl \
    -plaintext \
    -import-path "$PROTO_DIR" \
    -proto flight.proto \
    -d "{\"Id\":\"$FLIGHT_ID\"}" \
    "$FLIGHT_GRPC" \
    flight.FlightGrpcService/GetById >/dev/null
printf 'gRPC FlightGrpcService/GetById: OK\n'

seats_response="$(
    grpcurl \
        -plaintext \
        -import-path "$PROTO_DIR" \
        -proto flight.proto \
        -d "{\"FlightId\":\"$FLIGHT_ID\"}" \
        "$FLIGHT_GRPC" \
        flight.FlightGrpcService/GetAvailableSeats
)"
seat_number="$(jq --exit-status --raw-output '(.seatDtos // .SeatDtos)[0] | (.seatNumber // .SeatNumber)' <<<"$seats_response")"
printf 'gRPC FlightGrpcService/GetAvailableSeats: OK\n'

grpcurl \
    -plaintext \
    -import-path "$PROTO_DIR" \
    -proto flight.proto \
    -d "{\"FlightId\":\"$FLIGHT_ID\",\"SeatNumber\":\"$seat_number\"}" \
    "$FLIGHT_GRPC" \
    flight.FlightGrpcService/ReserveSeat >/dev/null
printf 'gRPC FlightGrpcService/ReserveSeat (%s): OK\n' "$seat_number"

for service in "" "flight.FlightGrpcService"; do
    health_response="$(
        grpcurl \
        -plaintext \
        -import-path "$SCRIPT_DIR" \
        -proto health.proto \
        -d "{\"service\":\"$service\"}" \
        "$FLIGHT_GRPC" \
        grpc.health.v1.Health/Check
    )"
    jq --exit-status '.status == "SERVING"' <<<"$health_response" >/dev/null
done
printf 'gRPC health Check (overall and FlightGrpcService): OK\n'
