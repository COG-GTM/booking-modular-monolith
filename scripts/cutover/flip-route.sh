#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
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
        exit 0
    fi
    sleep 2
done
echo "Gateway failed readiness; inspect state and explicitly roll back" >&2
exit 1
