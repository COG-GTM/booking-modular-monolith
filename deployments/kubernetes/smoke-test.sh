#!/usr/bin/env bash
# End-to-end smoke test through the ingress: token -> flight read -> register user -> passenger created
# (identity publishes UserCreated over RabbitMQ, passenger-service consumes it).
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
NAMESPACE="${NAMESPACE:-booking}"
FLIGHT_ID="${FLIGHT_ID:-3c5c0000-97c6-fc34-2eb9-08db322230c9}"

step() { printf '\n==> %s\n' "$*"; }
json() { python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

step "Pods"
kubectl -n "$NAMESPACE" get pods

step "POST $BASE_URL/connect/token"
token_response="$(curl -fsS "$BASE_URL/connect/token" \
  -d grant_type=password -d client_id=client -d client_secret=secret \
  -d username=samh -d password=Admin@123456 --data-urlencode "scope=booking-modular-monolith role")"
TOKEN="$(json "['access_token']" <<<"$token_response")"
echo "access_token: ${TOKEN:0:24}... (expires_in $(json "['expires_in']" <<<"$token_response")s)"
echo "iss: $(python3 -c 'import base64,json,sys; p=sys.argv[1].split(".")[1]; print(json.loads(base64.urlsafe_b64decode(p+"="*(-len(p)%4)))["iss"])' "$TOKEN")"

auth=(-H "Authorization: Bearer $TOKEN")

step "GET /api/v1.0/flight/get-available-seats/$FLIGHT_ID"
seats="$(curl -fsS "${auth[@]}" "$BASE_URL/api/v1.0/flight/get-available-seats/$FLIGHT_ID")"
echo "$seats" | head -c 400; echo

step "Unauthenticated request is rejected by the gateway"
curl -s -o /dev/null -w 'HTTP %{http_code}\n' "$BASE_URL/api/v1.0/flight/get-available-seats/$FLIGHT_ID"

suffix="$(date +%s)"
passport="K8S${suffix: -7}"  # passport_number is varchar(10)
step "POST /api/v1.0/identity/register-user (passport $passport)"
register="$(curl -fsS "${auth[@]}" -H 'Content-Type: application/json' \
  "$BASE_URL/api/v1.0/identity/register-user" \
  -d "{\"firstName\":\"Kube\",\"lastName\":\"Smoke\",\"username\":\"k8s-$suffix\",\"passportNumber\":\"$passport\",\"email\":\"k8s-$suffix@example.com\",\"password\":\"Admin@12345\",\"confirmPassword\":\"Admin@12345\"}")"
echo "$register"

step "Waiting for passenger-service to create the passenger (RabbitMQ UserCreated)"
passenger_id=""
for _ in $(seq 1 60); do
  passenger_id="$(kubectl -n "$NAMESPACE" exec deploy/postgres -- psql -U postgres -d passenger_modular_monolith -tAc \
    "select id from passenger where passport_number = '$passport'" 2>/dev/null | tr -d '[:space:]')"
  [[ -n "$passenger_id" ]] && break
  sleep 2
done
[[ -n "$passenger_id" ]] || { echo "passenger with passport $passport was not created" >&2; exit 1; }
echo "passenger row: $passenger_id"

step "GET /api/v1.0/passenger/$passenger_id"
for _ in $(seq 1 30); do
  code="$(curl -s -o /tmp/passenger.json -w '%{http_code}' "${auth[@]}" "$BASE_URL/api/v1.0/passenger/$passenger_id")"
  [[ "$code" == "200" ]] && break
  sleep 2
done
echo "HTTP $code"; cat /tmp/passenger.json; echo
[[ "$code" == "200" ]]

step "Smoke test passed"
