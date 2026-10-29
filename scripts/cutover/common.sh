#!/usr/bin/env bash
set -euo pipefail

cutover_token() {
    curl --max-time 30 -fsS --config - <<'CONFIG' | jq -er '.access_token'
url = "http://localhost:5000/connect/token"
user = "client:secret"
data-urlencode = "grant_type=password"
data-urlencode = "username=samh"
data-urlencode = "password=Admin@123456"
data-urlencode = "scope=booking-modular-monolith"
CONFIG
}

cutover_probe() {
    local cluster=$1 target=$2 token=$3
    local path expected host proof since status logs match
    expected=404
    case "$cluster" in
        flight)
            path=/api/v1/flight/get-available-flights
            expected=200
            ;;
        passenger)
            path=/api/v1/passenger/00000000-0000-0000-0000-000000000001
            ;;
        booking)
            path=/api/v1/booking/00000000-0000-0000-0000-000000000001
            ;;
        identity)
            path=/api/v1/identity/cutover-read-probe
            ;;
    esac
    host="$cluster-service:8080"
    if [[ "$target" == monolith ]]; then
        host=booking_modular_monolith
    fi
    proof=$(cat /proc/sys/kernel/random/uuid)
    since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    status=$(curl --max-time 30 -sS -o /dev/null -w '%{http_code}' --config - <<CONFIG
url = "http://localhost:5000$path?cutoverProof=$proof"
header = "Authorization: Bearer $token"
header = "correlationId: $proof"
CONFIG
    ) || return 1
    if [[ "$status" != "$expected" ]]; then
        echo "FAIL $cluster $target HTTP $status expected $expected" >&2
        return 1
    fi
    logs=$(docker logs --since "$since" gateway 2>&1) || return 1
    match=$(printf '%s\n' "$logs" | grep -E "Proxying to http://$host/.*$proof") || {
        echo "FAIL: no upstream proof for $proof" >&2
        return 1
    }
    printf 'PROOF %s %s HTTP %s correlationId=%s\n%s\n' "$cluster" "$target" "$status" "$proof" "$match"
}
