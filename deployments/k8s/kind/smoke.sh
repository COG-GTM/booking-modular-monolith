#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
k8s_dir="$(cd "$script_dir/.." && pwd)"
repo_root="$(cd "$k8s_dir/../.." && pwd)"
chart_dir="$k8s_dir/helm/booking"
cluster_name="${KIND_CLUSTER_NAME:-booking}"
http_port="${KIND_HTTP_PORT:-8088}"
base_url="http://localhost:$http_port"
ingress_manifest="https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.12.1/deploy/static/provider/kind/deploy.yaml"
metrics_manifest="https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.7.2/components.yaml"
storage_manifest_url="https://raw.githubusercontent.com/rancher/local-path-provisioner/v0.0.31/deploy/local-path-storage.yaml"
rendered_config=""
signing_key=""
jwks_file=""
storage_manifest="$(mktemp)"

cleanup() {
  if [[ -n "$rendered_config" ]]; then
    rm -f "$rendered_config"
  fi
  if [[ -n "$signing_key" ]]; then
    rm -f "$signing_key"
  fi
  if [[ -n "$jwks_file" ]]; then
    rm -f "$jwks_file"
  fi
  rm -f "$storage_manifest"
}
trap cleanup EXIT

for tool in docker helm kind kubectl curl; do
  command -v "$tool" >/dev/null || { printf 'Required tool not found: %s\n' "$tool" >&2; exit 1; }
done

if ! docker image inspect kindest/node:v1.32.2 >/dev/null 2>&1; then
  docker pull mirror.gcr.io/kindest/node:v1.32.2
  docker tag mirror.gcr.io/kindest/node:v1.32.2 kindest/node:v1.32.2
fi

if ! kind get clusters | grep -Fxq "$cluster_name"; then
  rendered_config="$(mktemp)"
  sed "s|\${KIND_HTTP_PORT:-8088}|$http_port|" "$script_dir/cluster.yaml" > "$rendered_config"
  kind create cluster --name "$cluster_name" --config "$rendered_config" --wait 5m
fi

kubectl config use-context "kind-$cluster_name"
kubectl cluster-info
curl -fsSLo "$storage_manifest" "$storage_manifest_url"
sed -i 's#image: rancher/local-path-provisioner:#image: mirror.gcr.io/rancher/local-path-provisioner:#' "$storage_manifest"
sed -i 's#image: busybox$#image: mirror.gcr.io/library/busybox:1.36.1#' "$storage_manifest"
if ! grep -Fq 'image: mirror.gcr.io/rancher/local-path-provisioner:v0.0.31' "$storage_manifest" ||
  ! grep -Fq 'image: mirror.gcr.io/library/busybox:1.36.1' "$storage_manifest"; then
  echo "Could not mirror the pinned local-path provisioner manifest images" >&2
  exit 1
fi
kubectl apply -f "$storage_manifest"
kubectl apply -f "$script_dir/storageclass.yaml"
kubectl rollout status deployment/local-path-provisioner -n local-path-storage --timeout=180s
kubectl apply -f "$ingress_manifest"
kubectl rollout status deployment/ingress-nginx-controller -n ingress-nginx --timeout=240s

if [[ "${INSTALL_METRICS_SERVER:-1}" == "1" ]]; then
  kubectl apply -f "$metrics_manifest"
  if ! kubectl get deployment metrics-server -n kube-system \
    -o jsonpath='{.spec.template.spec.containers[0].args[*]}' | grep -Fq -- '--kubelet-insecure-tls'; then
    kubectl patch deployment metrics-server -n kube-system --type=json \
      -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'
  fi
  kubectl rollout status deployment/metrics-server -n kube-system --timeout=180s
fi

images=(
  "booking-identity-service:local"
  "booking-flight-service:local"
  "booking-passenger-service:local"
  "booking-booking-service:local"
  "booking-gateway:local"
)
if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  docker build -f src/Services/Identity.Host/Dockerfile -t "${images[0]}" "$repo_root"
  docker build -f src/Services/Flight.Host/Dockerfile -t "${images[1]}" "$repo_root"
  docker build -f src/Services/Passenger.Host/Dockerfile -t "${images[2]}" "$repo_root"
  docker build -f src/Services/Booking.Host/Dockerfile -t "${images[3]}" "$repo_root"
  docker build -f src/Gateway/Dockerfile -t "${images[4]}" "$repo_root"
fi
kind load docker-image --name "$cluster_name" "${images[@]}"

kubectl create namespace booking --dry-run=client -o yaml | kubectl apply -f -
signing_key=""
if kubectl get secret identity-signing-key -n booking >/dev/null 2>&1; then
  echo "Reusing existing identity-signing-key Secret"
else
  signing_key="$(mktemp)"
  "$k8s_dir/scripts/generate-signing-key.sh" "$signing_key"
  kubectl create secret generic identity-signing-key -n booking \
    --from-file=tempkey.jwk="$signing_key" --dry-run=client -o yaml | kubectl apply -f -
fi

helm upgrade --install booking "$chart_dir" \
  --namespace booking --create-namespace \
  --values "$k8s_dir/environments/kind.yaml" \
  --set-string global.publicUrl="$base_url"

for workload in identity-service flight-service passenger-service booking-service gateway; do
  kubectl rollout status "deployment/$workload" -n booking --timeout=600s
done
for workload in postgres mongo rabbitmq eventstore; do
  kubectl rollout status "statefulset/$workload" -n booking --timeout=600s
done

if [[ "${INSTALL_METRICS_SERVER:-1}" == "1" ]]; then
  printf 'Waiting for gateway HPA metrics...\n'
  hpa_cpu=""
  for attempt in $(seq 1 24); do
    hpa_cpu="$(kubectl get hpa gateway -n booking \
      -o jsonpath='{.status.currentMetrics[0].resource.current.averageUtilization}' 2>/dev/null || true)"
    if [[ "$hpa_cpu" =~ ^[0-9]+$ ]]; then
      printf 'Gateway HPA current CPU utilization: %s%%\n' "$hpa_cpu"
      break
    fi
    sleep 5
  done
fi

kubectl get pods -n booking -o wide
kubectl get deploy -n booking \
  -o jsonpath='{range .items[*]}{.metadata.name}{" startup="}{.spec.template.spec.containers[0].startupProbe.httpGet.path}{" liveness="}{.spec.template.spec.containers[0].livenessProbe.httpGet.path}{" readiness="}{.spec.template.spec.containers[0].readinessProbe.httpGet.path}{"\n"}{end}'
kubectl get svc,ingress,hpa -n booking

# shellcheck disable=SC2016
kubectl run booking-dns-check -n booking --rm -i --restart=Never \
  --image=mirror.gcr.io/library/busybox:1.36.1 --command -- /bin/sh -ec '
    for service in identity-service flight-service passenger-service booking-service postgres rabbitmq mongo eventstore; do
      echo "DNS $service"
      nslookup "${service}.booking.svc.cluster.local."
    done
    echo "Flight readiness"
    for attempt in $(seq 1 60); do
      if flight_ready="$(wget -qO- http://flight-service:8080/health/ready 2>/dev/null)"; then
        echo "$flight_ready"
        break
      fi
      if [[ "$attempt" == "60" ]]; then
        echo "Flight readiness did not become available" >&2
        exit 1
      fi
      sleep 2
    done
  '

for attempt in $(seq 1 120); do
  if curl --noproxy '*' -fsS "$base_url/.well-known/openid-configuration" >/dev/null; then
    break
  fi
  if [[ "$attempt" == "120" ]]; then
    echo "Ingress discovery endpoint did not become available" >&2
    exit 1
  fi
  sleep 2
done

echo "Discovery document (HTTP status and body)"
discovery_response="$(curl --noproxy '*' -sS -w '\n%{http_code}' \
  "$base_url/.well-known/openid-configuration")"
discovery_status="${discovery_response##*$'\n'}"
discovery_body="${discovery_response%$'\n'*}"
printf 'HTTP %s\n%s\n' "$discovery_status" "$discovery_body"
[[ "$discovery_status" == "200" ]]
printf '%s\n' "$discovery_body" | python3 -c '
import json,sys
doc=json.load(sys.stdin)
expected=sys.argv[1]
assert doc["issuer"] == expected, doc["issuer"]
assert doc["jwks_uri"].startswith(expected + "/"), doc["jwks_uri"]
print("issuer:", doc["issuer"])
print("jwks_uri:", doc["jwks_uri"])
' "$base_url"
jwks_uri="$(printf '%s\n' "$discovery_body" | python3 -c 'import json,sys; print(json.load(sys.stdin)["jwks_uri"])')"
if [[ -n "$signing_key" ]]; then
  generated_key_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["kid"])' "$signing_key")"
else
  generated_key_id="$(kubectl get secret identity-signing-key -n booking -o json | \
    python3 -c 'import base64,json,sys; secret=json.load(sys.stdin); print(json.loads(base64.b64decode(secret["data"]["tempkey.jwk"]))["kid"])')"
fi
jwks_file="$(mktemp)"
curl --noproxy '*' -fsS -o "$jwks_file" "$jwks_uri"
published_key_id="$(python3 -c '
import json,sys
expected=sys.argv[2]
keys=json.load(open(sys.argv[1]))["keys"]
match=next((key["kid"] for key in keys if key.get("kid") == expected), None)
if match is None:
    raise SystemExit("Expected signing kid {} not found in JWKS: {}".format(expected, [key.get("kid") for key in keys]))
print(match)
' "$jwks_file" "$generated_key_id")"
printf 'Duende loaded the generated RSA JWK (kid=%s)\n' "$published_key_id"

echo "Password-grant token (HTTP status and selected claims)"
token_response="$(curl --noproxy '*' -sS -w '\n%{http_code}' \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'grant_type=password' \
  --data-urlencode 'username=samh' \
  --data-urlencode 'password=Admin@123456' \
  --data-urlencode 'scope=booking-modular-monolith' \
  --data-urlencode 'client_id=client' \
  --data-urlencode 'client_secret=secret' \
  "$base_url/connect/token")"
token_status="${token_response##*$'\n'}"
token_json="${token_response%$'\n'*}"
printf 'HTTP %s\n' "$token_status"
[[ "$token_status" == "200" ]]
printf '%s\n' "$token_json" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(json.dumps({"token_type":d.get("token_type"),"expires_in":d.get("expires_in"),"scope":d.get("scope")}))'
access_token="$(printf '%s\n' "$token_json" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"

request_json() {
  local method="$1" path="$2" body="${3:-}"
  local args=(--noproxy '*' -sS -w '\nHTTP %{http_code}\n' -X "$method" -H "Authorization: Bearer $access_token")
  if [[ -n "$body" ]]; then
    args+=(-H 'Content-Type: application/json' --data "$body")
  fi
  curl "${args[@]}" "$base_url$path"
}

response_body() {
  printf '%s' "${1%$'\n'HTTP *}"
}

response_status() {
  printf '%s' "${1##*$'\n'HTTP }"
}

echo "Register user response"
user_name="ab491-${RANDOM}${RANDOM}"
passport_number="A$(printf '%05d%05d' "$RANDOM" "$RANDOM" | cut -c1-9)"
email="${user_name}@example.test"
printf -v register_body '{"username":"%s","email":"%s","password":"Admin@123456","confirmPassword":"Admin@123456","firstName":"Sam","lastName":"H","passportNumber":"%s"}' \
  "$user_name" "$email" "$passport_number"
register_response="$(request_json POST /api/v1/identity/register-user "$register_body")"
printf '%s\n' "$register_response"
[[ "$(response_status "$register_response")" == "200" ]]

echo "Waiting for Passenger consumer to process UserCreated"
postgres_pod="$(kubectl get pod -n booking -l app.kubernetes.io/name=postgres -o jsonpath='{.items[0].metadata.name}')"
passenger_id=""
for attempt in $(seq 1 60); do
  passenger_id="$(kubectl exec -n booking "$postgres_pod" -- \
    psql -U postgres -d passenger_service -Atc \
    "select id from passenger where passport_number='$passport_number' limit 1" | tr -d '\r')"
  if [[ -n "$passenger_id" ]]; then
    break
  fi
  sleep 2
done
[[ -n "$passenger_id" ]]
echo "UserCreated created passenger id=$passenger_id passport=$passport_number"

echo "Complete registration response"
printf -v complete_body '{"passportNumber":"%s","passengerType":2,"age":30}' "$passport_number"
complete_response="$(request_json POST /api/v1/passenger/complete-registration "$complete_body")"
printf '%s\n' "$complete_response"
[[ "$(response_status "$complete_response")" == "200" ]]

echo "Waiting for Passenger read model"
for attempt in $(seq 1 60); do
  passenger_read_response="$(request_json GET "/api/v1/passenger/$passenger_id" 2>/dev/null || true)"
  if [[ "$(response_status "$passenger_read_response")" == "200" ]]; then
    break
  fi
  sleep 2
done
[[ "$(response_status "$passenger_read_response")" == "200" ]]
printf '%s\n' "$passenger_read_response"

echo "Available seats response"
seats_response="$(request_json GET '/api/v1/flight/get-available-seats/3c5c0000-97c6-fc34-2eb9-08db322230c9')"
printf '%s\n' "$seats_response"
[[ "$(response_status "$seats_response")" == "200" ]]
initial_seats="$(response_body "$seats_response" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["seatDtos"]))')"

echo "Create booking response"
printf -v booking_body '{"passengerId":"%s","flightId":"3c5c0000-97c6-fc34-2eb9-08db322230c9","description":"AB-491 kind smoke booking"}' \
  "$passenger_id"
booking_response="$(request_json POST /api/v1/booking "$booking_body")"
printf '%s\n' "$booking_response"
[[ "$(response_status "$booking_response")" == "200" ]]
printf '%s\n' "$booking_response" | python3 -c 'import json,sys; print("booking id:", json.loads(sys.stdin.read().split("\nHTTP ")[0])["id"])'

echo "Waiting for Flight gRPC reservation to reduce available seats"
updated_seats="$initial_seats"
for attempt in $(seq 1 60); do
  seats_response="$(request_json GET '/api/v1/flight/get-available-seats/3c5c0000-97c6-fc34-2eb9-08db322230c9')"
  updated_seats="$(response_body "$seats_response" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["seatDtos"]))')"
  if (( updated_seats == initial_seats - 1 )); then
    break
  fi
  sleep 2
done
[[ "$updated_seats" -eq "$((initial_seats - 1))" ]]
printf 'available seats before=%s after=%s\n' "$initial_seats" "$updated_seats"

echo "Signing-key persistence: restarting Identity and reusing the previously minted token"
kubectl rollout restart deployment/identity-service -n booking
kubectl rollout status deployment/identity-service -n booking --timeout=300s
post_restart_response="$(request_json GET "/api/v1/passenger/$passenger_id")"
printf '%s\n' "$post_restart_response"
[[ "$(response_status "$post_restart_response")" == "200" ]]

if [[ "${INSTALL_METRICS_SERVER:-1}" == "1" ]]; then
  for attempt in $(seq 1 60); do
    if kubectl get hpa gateway -n booking -o jsonpath='{.status.currentMetrics[0].resource.current.averageUtilization}' 2>/dev/null | grep -Eq '^[0-9]+$'; then
      break
    fi
    sleep 5
  done
fi
kubectl get hpa -n booking

if [[ "${DELETE_CLUSTER:-0}" == "1" ]]; then
  kind delete cluster --name "$cluster_name"
fi
