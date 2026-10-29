#!/usr/bin/env bash
set -Eeuo pipefail

CLUSTER_NAME="${CLUSTER_NAME:-booking}"
KIND_HTTP_PORT="${KIND_HTTP_PORT:-8088}"
SKIP_BUILD="${SKIP_BUILD:-0}"
IMAGE_TAG="${IMAGE_TAG:-local}"
KEEP_CLUSTER="${KEEP_CLUSTER:-1}"
TRAEFIK_CHART_VERSION="42.1.3"
METRICS_SERVER_VERSION="v0.7.2"
KIND_CONFIG="deployments/k8s/kind/cluster.yaml"
CHART="deployments/k8s/helm/booking"
CONFIG_FILE=""
JWK_FILE=""

for tool in kind kubectl helm docker jq curl python3; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$tool" >&2
        exit 1
    fi
done

on_exit() {
    local status=$?
    trap - EXIT
    if (( status != 0 )); then
        printf '\nKind smoke failed with status %s; cluster diagnostics follow.\n' "$status" >&2
        if kind get clusters 2>/dev/null | awk -v name="$CLUSTER_NAME" '$1 == name { found = 1 } END { exit !found }'; then
            kubectl get pods,deployments,services,ingress,hpa -n booking -o wide >&2 || true
            while IFS= read -r pod; do
                [[ -n "$pod" ]] || continue
                printf '\n--- logs: %s ---\n' "$pod" >&2
                kubectl logs -n booking "$pod" --all-containers=true --tail=200 >&2 || true
            done < <(kubectl get pods -n booking -o json 2>/dev/null | jq -r '.items[] | select(.status.phase != "Running" or any(.status.containerStatuses[]?; .ready != true)) | .metadata.name')
        fi
    fi
    if [[ "$KEEP_CLUSTER" == "0" ]] &&
        kind get clusters 2>/dev/null | awk -v name="$CLUSTER_NAME" '$1 == name { found = 1 } END { exit !found }'; then
        kind delete cluster --name "$CLUSTER_NAME" || true
    fi
    [[ -z "$CONFIG_FILE" ]] || rm -f "$CONFIG_FILE"
    [[ -z "$JWK_FILE" ]] || rm -f "$JWK_FILE"
    exit "$status"
}
trap on_exit EXIT

if ! kind get clusters | awk -v name="$CLUSTER_NAME" '$1 == name { found = 1 } END { exit !found }'; then
    CONFIG_FILE="$(mktemp)"
    sed "s/hostPort: 8088/hostPort: ${KIND_HTTP_PORT}/" "$KIND_CONFIG" >"$CONFIG_FILE"
    kind create cluster --name "$CLUSTER_NAME" --config "$CONFIG_FILE" --wait 5m
fi

helm repo add traefik https://traefik.github.io/charts --force-update
helm repo update traefik
helm upgrade --install traefik traefik/traefik \
    --version "$TRAEFIK_CHART_VERSION" \
    --namespace traefik \
    --create-namespace \
    --set service.spec.type=NodePort \
    --set ports.web.nodePort=30080 \
    --set image.registry=mirror.gcr.io \
    --set providers.kubernetesIngress.enabled=true \
    --set ingressClass.enabled=true \
    --set ingressClass.name=traefik \
    --wait \
    --timeout 5m

kubectl apply -f "https://github.com/kubernetes-sigs/metrics-server/releases/download/${METRICS_SERVER_VERSION}/components.yaml"
if ! kubectl get deployment metrics-server -n kube-system -o jsonpath='{.spec.template.spec.containers[0].args}' |
    grep -Fq -- "--kubelet-insecure-tls"; then
    kubectl patch deployment metrics-server -n kube-system --type=json \
        -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'
fi
kubectl rollout status deployment/metrics-server -n kube-system --timeout=5m

if [[ "$SKIP_BUILD" != "1" ]]; then
    docker build -t "booking-identity-service:${IMAGE_TAG}" -f src/Services/Identity.Host/Dockerfile .
    docker build -t "booking-flight-service:${IMAGE_TAG}" -f src/Services/Flight.Host/Dockerfile .
    docker build -t "booking-passenger-service:${IMAGE_TAG}" -f src/Services/Passenger.Host/Dockerfile .
    docker build -t "booking-booking-service:${IMAGE_TAG}" -f src/Services/Booking.Host/Dockerfile .
    docker build -t "booking-gateway:${IMAGE_TAG}" -f src/Gateway/Dockerfile .
fi
kind load docker-image \
    "booking-identity-service:${IMAGE_TAG}" \
    "booking-flight-service:${IMAGE_TAG}" \
    "booking-passenger-service:${IMAGE_TAG}" \
    "booking-booking-service:${IMAGE_TAG}" \
    "booking-gateway:${IMAGE_TAG}" \
    --name "$CLUSTER_NAME"

kubectl create namespace booking --dry-run=client -o yaml | kubectl apply -f -
JWK_FILE="$(mktemp)"
EXPECTED_KID="$(python3 - "$JWK_FILE" <<'PY'
import base64
import json
import secrets
import sys

from cryptography.hazmat.primitives.asymmetric import rsa


def encode(value):
    raw = value.to_bytes((value.bit_length() + 7) // 8, "big")
    return base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")


key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
numbers = key.private_numbers()
public = numbers.public_numbers
kid = secrets.token_hex(16)
jwk = {
    "kty": "RSA",
    "kid": kid,
    "alg": "RS256",
    "use": "sig",
    "n": encode(public.n),
    "e": encode(public.e),
    "d": encode(numbers.d),
    "p": encode(numbers.p),
    "q": encode(numbers.q),
    "dp": encode(numbers.dmp1),
    "dq": encode(numbers.dmq1),
    "qi": encode(numbers.iqmp),
}
with open(sys.argv[1], "w", encoding="utf-8") as key_file:
    json.dump(jwk, key_file)
print(kid)
PY
)"
kubectl create secret generic identity-signing-key \
    --namespace booking \
    --from-file="tempkey.jwk=${JWK_FILE}" \
    --dry-run=client \
    -o yaml | kubectl apply -f -
rm -f "$JWK_FILE"
JWK_FILE=""

helm upgrade --install booking "$CHART" \
    --namespace booking \
    --create-namespace \
    --values deployments/k8s/environments/kind.yaml \
    --set "global.image.tag=${IMAGE_TAG}" \
    --set services.identity.signingKey.existingSecret=identity-signing-key \
    --wait=false

kubectl rollout restart deployment/identity-service -n booking
for deployment in postgres mongo rabbitmq eventstore identity-service flight-service passenger-service booking-service gateway; do
    kubectl rollout status "deployment/${deployment}" -n booking --timeout=15m
done
kubectl rollout status deployment/traefik -n traefik --timeout=5m

# shellcheck disable=SC2016
kubectl exec -n booking deployment/booking-service -- bash -c '
for host in identity-service flight-service passenger-service gateway postgres mongo rabbitmq eventstore; do
  getent hosts "$host" >/dev/null || { echo "DNS lookup failed: $host" >&2; exit 1; }
  echo "DNS $host: resolved"
done
for host in flight-service passenger-service; do
  (exec 3<>"/dev/tcp/${host}/81") || { echo "gRPC port is unreachable: ${host}:81" >&2; exit 1; }
  echo "h2c TCP ${host}:81: connected"
done
'

jwks_url="http://localhost:${KIND_HTTP_PORT}/.well-known/openid-configuration/jwks"
jwks_payload=""
attempt=0
while ((attempt < 30)); do
    attempt=$((attempt + 1))
    if jwks_payload="$(curl --fail --silent --show-error --max-time 20 "$jwks_url" 2>/dev/null)"; then
        break
    fi
    if ((attempt < 30)); then
        sleep 2
    fi
done
if [[ -z "$jwks_payload" ]]; then
    printf 'IdentityServer JWKS did not become available after %d attempts\n' "$attempt" >&2
    exit 1
fi
if ! jq --exit-status --arg kid "$EXPECTED_KID" 'any(.keys[]?; .kid == $kid)' <<<"$jwks_payload" >/dev/null; then
    published_kids="$(jq --raw-output '[.keys[]?.kid] | join(",")' <<<"$jwks_payload")"
    printf 'IdentityServer JWKS does not contain mounted secret kid %s; published kids: %s\n' \
        "$EXPECTED_KID" "$published_kids" >&2
    exit 1
fi
printf 'IdentityServer JWKS contains mounted signing-key kid: %s\n' "$EXPECTED_KID"

access_token="$(curl --fail --silent --show-error \
    --request POST \
    --header 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=password' \
    --data-urlencode 'client_id=client' \
    --data-urlencode 'client_secret=secret' \
    --data-urlencode 'scope=booking-modular-monolith' \
    --data-urlencode 'username=samh' \
    --data-urlencode 'password=Admin@123456' \
    "http://localhost:${KIND_HTTP_PORT}/connect/token" | jq --exit-status --raw-output '.access_token')"
token_header_segment="${access_token%%.*}"
case $((${#token_header_segment} % 4)) in
    2) token_header_segment+="==" ;;
    3) token_header_segment+="=" ;;
esac
token_kid="$(printf '%s' "$token_header_segment" | tr '_-' '/+' | base64 --decode | jq --exit-status --raw-output '.kid')"
unset access_token token_header_segment
if [[ "$token_kid" != "$EXPECTED_KID" ]]; then
    printf 'Password-grant token kid does not match mounted secret: expected %s, got %s\n' \
        "$EXPECTED_KID" "$token_kid" >&2
    exit 1
fi
printf 'Password-grant token kid matches mounted secret: %s\n' "$token_kid"

GATEWAY="http://localhost:${KIND_HTTP_PORT}" \
MONGO_SHELL="kubectl -n booking exec deploy/mongo -- mongosh" \
    bash scripts/smoke/compose-e2e.sh
curl --fail --silent --show-error --output /dev/null --write-out 'Ingress /health/ready: HTTP %{http_code}\n' \
    "http://localhost:${KIND_HTTP_PORT}/health/ready"
kubectl get hpa -n booking
