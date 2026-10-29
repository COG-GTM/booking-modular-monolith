#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cluster=${1:-flight}
case "$cluster" in
    flight|identity|passenger|booking) ;;
    *)
        echo "Unknown cluster: $cluster" >&2
        exit 2
        ;;
esac
compose=(docker compose -f "$root/deployments/docker-compose/docker-compose.yaml")
unset IDENTITY_UPSTREAM FLIGHT_UPSTREAM PASSENGER_UPSTREAM BOOKING_UPSTREAM
state="$root/deployments/docker-compose/.cutover.env"
touch "$state"
"${compose[@]}" --env-file "$state" --profile monolith up -d --wait --wait-timeout 300 \
    identity-service flight-service passenger-service booking-service gateway booking_modular_monolith
for ((i=0; i<90; i++)); do
    if docker exec booking_modular_monolith bash -c '
        exec 3<>/dev/tcp/127.0.0.1/80
        printf "GET /.well-known/openid-configuration HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3
        head -n 1 <&3
    ' | grep -q ' 200 '; then
        break
    fi
    sleep 2
done
token=$(curl --max-time 30 -fsS http://localhost:5000/connect/token -u client:secret \
    --data-urlencode grant_type=password \
    --data-urlencode username=samh \
    --data-urlencode password=Admin@123456 \
    --data-urlencode 'scope=booking-modular-monolith')
token=$(jq -er '.access_token' <<< "$token")
for target in service monolith service; do
    "$root/scripts/cutover/flip-route.sh" "$cluster" "$target"
    probes=("$cluster")
    if [[ "$cluster" == identity || "$cluster" == passenger ]]; then
        probes=(identity passenger)
    fi
    for c in "${probes[@]}"; do
        proof=$(cat /proc/sys/kernel/random/uuid)
        case "$c" in
            flight)
                path=/api/v1/flight/get-available-flights
                expected=200
                ;;
            passenger)
                path=/api/v1/passenger/00000000-0000-0000-0000-000000000001
                expected=404
                ;;
            booking)
                path=/api/v1/booking/00000000-0000-0000-0000-000000000001
                expected=404
                ;;
            identity)
                path=/api/v1/identity/cutover-read-probe
                expected=404
                ;;
        esac
        host="$c-service:8080"
        if [[ "$target" == monolith ]]; then
            host=booking_modular_monolith
        fi
        since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
        status=$(curl --max-time 30 -sS -o /dev/null -w '%{http_code}' \
            -H "Authorization: Bearer $token" \
            -H "correlationId: $proof" \
            "http://localhost:5000$path?cutoverProof=$proof")
        if [[ "$status" != "$expected" ]]; then
            echo "FAIL $c $target HTTP $status expected $expected"
            exit 1
        fi
        logs=$(docker logs --since "$since" gateway 2>&1)
        match=$(printf '%s\n' "$logs" | grep -E "Proxying to http://$host/.*$proof") || {
            echo "FAIL: no upstream proof for $proof"
            exit 1
        }
        printf 'PROOF %s %s HTTP %s correlationId=%s\n%s\n' "$c" "$target" "$status" "$proof" "$match"
    done
done
echo "PASS: authenticated service -> monolith -> service upstream proofs"
