#!/usr/bin/env bash
# Deploys the booking Helm chart to a local kind cluster and smoke-tests it through the ingress:
#   1. kind cluster (kind/cluster.yaml) + ingress-nginx + metrics-server (for the HPAs)
#   2. builds the five images from their Dockerfiles and `kind load`s them
#   3. helm upgrade --install with environments/kind.yaml (in-chart Postgres/Mongo/RabbitMQ/ESDB)
#   4. waits for every pod to be Ready, checks in-cluster DNS + REST/gRPC ports from a test pod
#   5. booking flow via the ingress: token -> register user -> complete passenger registration
#      (RabbitMQ event) -> create flight + seat -> create booking (Booking -> Flight/Passenger gRPC)
#
# Run from anywhere: deployments/k8s/kind-smoke.sh
# Env: KIND_CLUSTER (booking), KIND_HTTP_PORT (8080), NAMESPACE (booking), RELEASE (booking),
#      SKIP_BUILD=1 (use existing booking-<svc>:dev images), BUILD_CONTEXT (repo root),
#      INSTALL_METRICS_SERVER (1), DELETE_CLUSTER=1 (delete the cluster at the end),
#      TIMEOUT_SECONDS (300). Requires docker, kind, kubectl, helm, curl, jq.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
KIND_CLUSTER="${KIND_CLUSTER:-booking}"
KIND_HTTP_PORT="${KIND_HTTP_PORT:-8080}"
NAMESPACE="${NAMESPACE:-booking}"
RELEASE="${RELEASE:-booking}"
BUILD_CONTEXT="${BUILD_CONTEXT:-$REPO_ROOT}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-300}"
# Probe paths (AB-495). For images built before AB-495: LIVE_PATH=/alive READY_PATH=/health.
LIVE_PATH="${LIVE_PATH:-/health/live}"
READY_PATH="${READY_PATH:-/health/ready}"
INGRESS_NGINX_VERSION="controller-v1.11.3"
METRICS_SERVER_VERSION="v0.7.2"
GATEWAY_URL="http://localhost:$KIND_HTTP_PORT"
API="$GATEWAY_URL/api/v1.0"

declare -A DOCKERFILES=(
  [gateway]=src/Gateway/Dockerfile
  [identity]=src/Services/Identity.Host/Dockerfile
  [flight]=src/Services/Flight.Host/Dockerfile
  [passenger]=src/Services/Passenger.Host/Dockerfile
  [booking]=src/Services/Booking.Host/Dockerfile
)
SERVICES=(gateway identity flight passenger booking)

# Seeded by the Flight service (src/Modules/Flight/src/Data/Seed/InitialData.cs).
AIRCRAFT_ID="3c5c0000-97c6-fc34-fcd3-08db322230c8"
DEPARTURE_AIRPORT_ID="3c5c0000-97c6-fc34-a0cb-08db322230c8"
ARRIVE_AIRPORT_ID="3c5c0000-97c6-fc34-fc3c-08db322230c8"

for tool in docker kind kubectl helm curl jq; do
  command -v "$tool" >/dev/null || { echo "kind-smoke: '$tool' is required" >&2; exit 2; }
done

RESPONSE_BODY="$(mktemp)"
trap 'rm -f "$RESPONSE_BODY"' EXIT
[[ "${DELETE_CLUSTER:-0}" == 1 ]] && trap 'rm -f "$RESPONSE_BODY"; kind delete cluster --name "$KIND_CLUSTER"' EXIT

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
kc() { kubectl --context "kind-$KIND_CLUSTER" "$@"; }

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

step "kind cluster '$KIND_CLUSTER' (ingress on localhost:$KIND_HTTP_PORT)"
if kind get clusters | grep -qx "$KIND_CLUSTER"; then
  echo "reusing existing cluster"
else
  sed "s/hostPort: 8080/hostPort: $KIND_HTTP_PORT/" "$SCRIPT_DIR/kind/cluster.yaml" |
    kind create cluster --name "$KIND_CLUSTER" --config - --wait 120s
fi

step "ingress-nginx ($INGRESS_NGINX_VERSION)"
kc apply -f "https://raw.githubusercontent.com/kubernetes/ingress-nginx/$INGRESS_NGINX_VERSION/deploy/static/provider/kind/deploy.yaml" >/dev/null
kc -n ingress-nginx rollout status deployment/ingress-nginx-controller --timeout="${TIMEOUT_SECONDS}s"
# The admission webhook only accepts Ingress objects once its endpoint is up.
kc -n ingress-nginx wait --for=condition=complete job/ingress-nginx-admission-patch --timeout=120s >/dev/null

if [[ "${INSTALL_METRICS_SERVER:-1}" == 1 ]]; then
  step "metrics-server ($METRICS_SERVER_VERSION, kubelet-insecure-tls for kind)"
  kc apply -f "https://github.com/kubernetes-sigs/metrics-server/releases/download/$METRICS_SERVER_VERSION/components.yaml" >/dev/null
  kc -n kube-system patch deployment metrics-server --type=json \
    -p '[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]' >/dev/null || true
fi

step "images"
for svc in "${SERVICES[@]}"; do
  image="booking-$svc:dev"
  if [[ "${SKIP_BUILD:-0}" != 1 ]]; then
    echo "building $image from ${DOCKERFILES[$svc]}"
    docker build -q -t "$image" -f "$BUILD_CONTEXT/${DOCKERFILES[$svc]}" "$BUILD_CONTEXT" >/dev/null
  fi
  kind load docker-image "$image" --name "$KIND_CLUSTER" >/dev/null
  echo "loaded $image"
done

step "helm upgrade --install $RELEASE (namespace $NAMESPACE)"
helm --kube-context "kind-$KIND_CLUSTER" upgrade --install "$RELEASE" "$SCRIPT_DIR/helm/booking" \
  --namespace "$NAMESPACE" --create-namespace \
  -f "$SCRIPT_DIR/environments/kind.yaml" \
  --set global.publicUrl="$GATEWAY_URL" \
  --set defaults.probes.livenessPath="$LIVE_PATH" --set defaults.probes.readinessPath="$READY_PATH" \
  --wait --timeout "$((TIMEOUT_SECONDS * 3))s"

step "pods / services / ingress / hpa"
for workload in $(kc -n "$NAMESPACE" get deploy,statefulset -o name); do
  kc -n "$NAMESPACE" rollout status "$workload" --timeout="${TIMEOUT_SECONDS}s"
done
kc -n "$NAMESPACE" get pods -o wide
kc -n "$NAMESPACE" get svc,ingress,hpa

step "in-cluster DNS + REST ($READY_PATH on :80) + gRPC port (:81) from a test pod"
# shellcheck disable=SC2016 # expanded by the test pod's shell
kc -n "$NAMESPACE" run dns-check --rm -i --restart=Never --image=mirror.gcr.io/library/busybox:1.36 \
  --quiet -- sh -c '
    set -e
    for svc in gateway identity flight passenger booking postgres mongo rabbitmq eventstore; do
      addr="$(nslookup "$svc" | awk "/^Address/ && !/#53/ {print \$2}" | tail -1)"
      [ -n "$addr" ] || { echo "nslookup $svc failed"; exit 1; }
      echo "$svc.$1.svc.cluster.local -> $addr"
    done
    for svc in gateway identity flight passenger booking; do
      echo "GET http://$svc$2 -> $(wget -qO- "http://$svc$2")"
    done
    for svc in flight passenger; do nc -z -w 3 "$svc" 81 && echo "tcp $svc:81 (grpc) open"; done' _ "$NAMESPACE" "$READY_PATH"

step "gateway via ingress ($GATEWAY_URL$READY_PATH)"
eventually 200 GET "$GATEWAY_URL$READY_PATH"

suffix="$(date +%s)$RANDOM"
passport="P${suffix: -9}"
flight_number="K8${suffix: -6}"
departure="$(date -u -d '+30 days' +%Y-%m-%dT10:00:00Z)"
arrival="$(date -u -d '+30 days' +%Y-%m-%dT12:00:00Z)"

step "Token from identity via ingress -> gateway (POST /connect/token)"
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
expect 200 POST "$API/identity/register-user" "$(jq -nc --arg u "k8s$suffix" --arg p "$passport" \
  '{firstName:"Kind", lastName:"Smoke", username:$u, email:($u + "@example.com"),
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

step "Create booking (Booking -> Flight/Passenger over gRPC via flight:81 / passenger:81)"
eventually 200 POST "$API/booking" "$(jq -nc --arg p "$passenger_id" --arg f "$flight_id" \
  '{passengerId:$p, flightId:$f, description:"AB-491 kind smoke test"}')"
booking_id="$(jq -r .id "$RESPONSE_BODY")"
jq -c . "$RESPONSE_BODY"

step "Seat reserved by Booking's gRPC ReserveSeat call (Flight read model)"
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

printf '\nKIND SMOKE TEST PASSED: passenger=%s flight=%s booking=%s\n' "$passenger_id" "$flight_id" "$booking_id"
