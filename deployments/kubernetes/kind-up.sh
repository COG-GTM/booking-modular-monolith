#!/usr/bin/env bash
# Builds the five service images, loads them into a kind cluster, installs ingress-nginx (+ metrics-server
# for the HPAs), creates the IdentityServer signing-key Secret and installs the chart into namespace `booking`.
#
#   deployments/kubernetes/kind-up.sh            # create/reuse cluster, build, load, install
#   SKIP_BUILD=1 deployments/kubernetes/kind-up.sh
#   deployments/kubernetes/smoke-test.sh          # verify through http://localhost:8080
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLUSTER="${KIND_CLUSTER_NAME:-booking}"
NAMESPACE="${NAMESPACE:-booking}"
RELEASE="${RELEASE:-booking}"
TAG="${IMAGE_TAG:-dev}"
INGRESS_NGINX_VERSION="${INGRESS_NGINX_VERSION:-controller-v1.12.1}"
METRICS_SERVER_VERSION="${METRICS_SERVER_VERSION:-v0.7.2}"
INSTALL_METRICS_SERVER="${INSTALL_METRICS_SERVER:-1}"
SIGNING_SECRET="identity-signing-key"

declare -A DOCKERFILES=(
  [gateway]=src/Gateway/Dockerfile
  [identity-service]=src/Services/Identity/Dockerfile
  [flight-service]=src/Services/Flight/Flight.Host/Dockerfile
  [passenger-service]=src/Services/Passenger.Host/Dockerfile
  [booking-service]=src/Services/Booking/Dockerfile
)

log() { printf '\n==> %s\n' "$*"; }

for bin in docker kind kubectl helm; do
  command -v "$bin" >/dev/null || { echo "missing required tool: $bin" >&2; exit 1; }
done

if ! kind get clusters | grep -qx "$CLUSTER"; then
  log "Creating kind cluster '$CLUSTER'"
  kind create cluster --name "$CLUSTER" --config "$ROOT/deployments/kubernetes/kind-config.yaml"
fi
kubectl config use-context "kind-$CLUSTER" >/dev/null

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  for svc in "${!DOCKERFILES[@]}"; do
    log "Building booking/$svc:$TAG"
    docker build -t "booking/$svc:$TAG" -f "$ROOT/${DOCKERFILES[$svc]}" "$ROOT"
  done
fi

log "Loading images into kind"
for svc in "${!DOCKERFILES[@]}"; do
  kind load docker-image --name "$CLUSTER" "booking/$svc:$TAG"
done

log "Installing ingress-nginx ($INGRESS_NGINX_VERSION)"
kubectl apply -f "https://raw.githubusercontent.com/kubernetes/ingress-nginx/$INGRESS_NGINX_VERSION/deploy/static/provider/kind/deploy.yaml"
kubectl -n ingress-nginx rollout status deployment/ingress-nginx-controller --timeout=300s

if [[ "$INSTALL_METRICS_SERVER" == "1" ]]; then
  log "Installing metrics-server ($METRICS_SERVER_VERSION) for the HPAs"
  kubectl apply -f "https://github.com/kubernetes-sigs/metrics-server/releases/download/$METRICS_SERVER_VERSION/components.yaml"
  # kind kubelets use self-signed serving certs.
  kubectl -n kube-system patch deployment metrics-server --type=json \
    -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]' 2>/dev/null || true
fi

kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f - >/dev/null

if ! kubectl -n "$NAMESPACE" get secret "$SIGNING_SECRET" >/dev/null 2>&1; then
  log "Generating IdentityServer signing key (tempkey.jwk) -> secret/$SIGNING_SECRET"
  # AddDeveloperSigningCredential() writes /app/tempkey.jwk on startup; let the identity image create one.
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"; docker rm -f booking-keygen >/dev/null 2>&1 || true' EXIT
  docker rm -f booking-keygen >/dev/null 2>&1 || true
  docker run -d --name booking-keygen "booking/identity-service:$TAG" >/dev/null
  for _ in $(seq 1 60); do
    docker cp booking-keygen:/app/tempkey.jwk "$tmp/tempkey.jwk" >/dev/null 2>&1 && break
    sleep 1
  done
  [[ -s "$tmp/tempkey.jwk" ]] || { echo "identity image did not produce /app/tempkey.jwk" >&2; exit 1; }
  kubectl -n "$NAMESPACE" create secret generic "$SIGNING_SECRET" --from-file=tempkey.jwk="$tmp/tempkey.jwk"
fi

log "helm upgrade --install $RELEASE (namespace $NAMESPACE)"
helm upgrade --install "$RELEASE" "$ROOT/deployments/helm/booking" \
  --namespace "$NAMESPACE" --create-namespace \
  -f "$ROOT/deployments/helm/booking/values-kind.yaml" \
  --set global.image.tag="$TAG" \
  --wait --timeout 15m

kubectl -n "$NAMESPACE" get pods,svc,hpa,ingress
echo
echo "Gateway: http://localhost:8080  (run deployments/kubernetes/smoke-test.sh)"
