#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
# shellcheck source=scripts/cutover/common.sh
source "$root/scripts/cutover/common.sh"
state="$root/deployments/docker-compose/.cutover.env"
cluster=${1:-}
target=${2:-}
case "$cluster" in
    identity|flight|passenger|booking) ;;
    *)
        echo "Usage: $0 <identity|flight|passenger|booking> <monolith|service>" >&2
        exit 2
        ;;
esac
case "$target" in
    monolith|service) ;;
    *)
        echo "Target must be monolith or service" >&2
        exit 2
        ;;
esac
compose=(docker compose -f "$root/deployments/docker-compose/docker-compose.yaml")
unset IDENTITY_UPSTREAM FLIGHT_UPSTREAM PASSENGER_UPSTREAM BOOKING_UPSTREAM
if [[ "$target" == monolith ]]; then
    : "${MONOLITH_IMAGE:?set to the previous release image}"
    compose+=(-f "$root/deployments/docker-compose/docker-compose.monolith-rollback.yaml")
    docker exec booking_modular_monolith bash -c '
        exec 3<>/dev/tcp/127.0.0.1/80
        printf "GET /.well-known/openid-configuration HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3
        head -n 1 <&3
    ' | grep -q ' 200 ' || {
        echo "Refusing flip: monolith readiness failed" >&2
        exit 1
    }
fi
# Serialize read/modify/recreate so paired changes cannot overwrite one another.
exec 9>"$state.lock"
flock 9
touch "$state"
clusters=("$cluster")
if [[ "$cluster" == identity || "$cluster" == passenger ]]; then
    clusters=(identity passenger)
    echo "Identity and Passenger move together (ADR 0007)"
fi
if [[ "$target" == service ]]; then
    for c in "${clusters[@]}"; do
        docker exec "$c-service" bash -c '
            exec 3<>/dev/tcp/127.0.0.1/8080
            printf "GET /health/ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3
            head -n 1 <&3
        ' | grep -q ' 200 ' || {
            echo "Refusing flip: $c-service readiness failed; routes unchanged" >&2
            exit 1
        }
    done
fi
token=$(cutover_token)
previous=service
if grep -q "^${cluster^^}_UPSTREAM=http://booking_modular_monolith/$" "$state"; then
    previous=monolith
fi
flip_failed() {
    echo "Flip failed; inspect destinations and rollback with: MONOLITH_IMAGE=<previous-release-image> $0 $cluster $previous" >&2
}
trap flip_failed ERR
for c in "${clusters[@]}"; do
    key="${c^^}_UPSTREAM"
    address="http://$c-service:8080/"
    if [[ "$target" == monolith ]]; then
        address="http://booking_modular_monolith/"
    fi
    sed -i "/^$key=/d" "$state"
    printf '%s=%s\n' "$key" "$address" >> "$state"
    echo "$c -> $address"
done
"${compose[@]}" --env-file "$state" up -d --no-deps gateway
for ((i=0; i<60; i++)); do
    if curl --max-time 5 -fsS http://localhost:5000/health/ready >/dev/null; then
        for c in "${clusters[@]}"; do
            cutover_probe "$c" "$target" "$token"
        done
        trap - ERR
        exit 0
    fi
    sleep 2
done
echo "Gateway failed readiness" >&2
flip_failed
exit 1
