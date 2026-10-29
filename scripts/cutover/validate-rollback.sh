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
: "${MONOLITH_IMAGE:?set to the previous release image}"
compose+=(-f "$root/deployments/docker-compose/docker-compose.monolith-rollback.yaml")
unset IDENTITY_UPSTREAM FLIGHT_UPSTREAM PASSENGER_UPSTREAM BOOKING_UPSTREAM
state="$root/deployments/docker-compose/.cutover.env"
# shellcheck source=scripts/cutover/common.sh
source "$root/scripts/cutover/common.sh"
probes=("$cluster")
if [[ "$cluster" == identity || "$cluster" == passenger ]]; then
    probes=(identity passenger)
fi
initial=()
exec 9>"$state.lock"
flock 9
touch "$state"
for c in "${probes[@]}"; do
    address=$(sed -n "s/^${c^^}_UPSTREAM=//p" "$state")
    case "$address" in
        ""|"http://$c-service:8080/") initial+=(service) ;;
        http://booking_modular_monolith/) initial+=(monolith) ;;
        *)
            echo "Cannot restore custom destination for $c: $address" >&2
            exit 2
            ;;
    esac
done
flock -u 9
exec 9>&-
if [[ "${#probes[@]}" == 2 && "${initial[0]}" != "${initial[1]}" ]]; then
    echo "Refusing drill: Identity and Passenger initial destinations differ" >&2
    exit 2
fi
restore_on_failure() {
    local status=$?
    trap - EXIT
    if ((status != 0)); then
        echo "Drill failed; restoring initial destinations" >&2
        if ! "$root/scripts/cutover/flip-route.sh" "$cluster" "${initial[0]}"; then
            echo "RESTORATION FAILED: rollback with: MONOLITH_IMAGE=<previous-release-image> $root/scripts/cutover/flip-route.sh $cluster ${initial[0]}" >&2
        else
            echo "RESTORED: $cluster -> ${initial[0]}" >&2
        fi
    fi
    exit "$status"
}
trap restore_on_failure EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
"${compose[@]}" --env-file "$state" up -d --wait --wait-timeout 300 \
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
token=$(cutover_token)
for target in service monolith service; do
    "$root/scripts/cutover/flip-route.sh" "$cluster" "$target"
    for c in "${probes[@]}"; do
        cutover_probe "$c" "$target" "$token"
    done
done
echo "PASS: authenticated service -> monolith -> service upstream proofs"
