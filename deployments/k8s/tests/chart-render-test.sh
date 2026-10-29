#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CHART="${ROOT}/deployments/k8s/helm/booking"
KIND_VALUES="${ROOT}/deployments/k8s/environments/kind.yaml"
PRODUCTION_VALUES="${ROOT}/deployments/k8s/environments/production.yaml"
TMP_DIR="$(mktemp -d)"
trap 'rm -f "${TMP_DIR}"/*; rmdir "${TMP_DIR}"' EXIT

for tool in helm yq kubeconform; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$tool" >&2
        exit 1
    fi
done

fail() {
    printf 'Chart render test failed: %s\n' "$1" >&2
    exit 1
}

expect_failure() {
    local message="$1"
    local expected="$2"
    shift 2
    if "$@" >"${TMP_DIR}/failure.out" 2>&1; then
        fail "${message}: command unexpectedly succeeded"
    fi
    if ! grep -Fq "$expected" "${TMP_DIR}/failure.out"; then
        printf '%s\n' "--- ${message} output ---" >&2
        cat "${TMP_DIR}/failure.out" >&2
        fail "${message}: expected error containing ${expected}"
    fi
    printf 'PASS %s\n' "$message"
}

helm lint "$CHART" --values "$KIND_VALUES"
helm lint "$CHART" --values "$PRODUCTION_VALUES"
helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set services.identity.signingKey.existingSecret=identity-signing-key >"${TMP_DIR}/kind.yaml"
helm template booking "$CHART" --namespace booking --values "$PRODUCTION_VALUES" >"${TMP_DIR}/production.yaml"
kubeconform -strict -summary -kubernetes-version 1.31.0 "${TMP_DIR}/kind.yaml"
kubeconform -strict -summary -kubernetes-version 1.31.0 "${TMP_DIR}/production.yaml"
printf 'PASS kind and production manifests validate against Kubernetes 1.31 schemas\n'

kind_gateway_keys="$(yq -r 'select(.kind == "ConfigMap" and .metadata.name == "gateway-config") | .data | keys | .[]' "${TMP_DIR}/kind.yaml")"
if grep -q '^Cors__AllowedOrigins__' <<<"$kind_gateway_keys"; then
    fail "kind should leave gateway CORS origins to appsettings defaults"
fi
production_cors_origin="$(yq -r 'select(.kind == "ConfigMap" and .metadata.name == "gateway-config") | .data."Cors__AllowedOrigins__0"' "${TMP_DIR}/production.yaml")"
[[ "$production_cors_origin" == "https://booking.example.com" ]] || fail "production gateway CORS origin is missing"
printf 'PASS gateway CORS origins follow environment values\n'

for stale_key in \
    ReverseProxy__Clusters__monolith__Destinations__primary__Address \
    ReverseProxy__Routes__api-fallback__Transforms__0__PathSet \
    ReverseProxy__Routes__monolith-fallback__Transforms__0__PathSet; do
    if yq -e "select(.kind == \"ConfigMap\" and .metadata.name == \"gateway-config\") | .data | has(\"${stale_key}\")" "${TMP_DIR}/kind.yaml" >/dev/null; then
        fail "gateway ConfigMap unexpectedly contains retired key ${stale_key}"
    fi
done
printf 'PASS gateway ConfigMap has no retired monolith cluster or fallback route keys\n'

for mapping in identity:Identity flight:Flight passenger:Passenger booking:Booking; do
    service="${mapping%%:*}-service"
    module="${mapping#*:}"
    yq -e "select(.kind == \"ConfigMap\" and .metadata.name == \"${service}-config\") | .data.\"Modules__${module}__BackgroundProcessingEnabled\" == \"true\"" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${service} should enable background processing by default"
done
printf 'PASS service hosts enable background processing by default\n'

expect_failure "base values require explicit production secrets" \
    "global.allowInsecureDefaults=false requires services.identity.existingSecret" \
    helm template booking "$CHART" --namespace booking
expect_failure "background-processing service cannot scale out" \
    "only one replica may run background processing" \
    helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" --set services.flight.replicas=2
expect_failure "Booking background processing cannot use HPA" \
    "only one replica may run background processing" \
    helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" --set services.booking.hpa.enabled=true
expect_failure "extraEnv cannot override background-processing settings" \
    "is chart-owned and cannot override" \
    helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set 'services.flight.extraEnv.Modules__Flight__BackgroundProcessingEnabled=false'
expect_failure "extraEnv cannot override RabbitMQ virtual host" \
    "is chart-owned and cannot override" \
    helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set 'services.flight.extraEnv.RabbitMqOptions__VirtualHost=other'
expect_failure "extraEnv cannot override gateway CORS origins" \
    "is chart-owned and cannot override" \
    helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set 'services.gateway.extraEnv.Cors__AllowedOrigins__0=https://example.org'
expect_failure "invalid database identifiers are rejected" \
    "database identifiers must match" \
    helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set 'services.flight.database.name=flight-service;x'
helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set services.flight.backgroundProcessing=false \
    --set services.flight.replicas=2 >"${TMP_DIR}/scaled-flight.yaml"
printf 'PASS backgroundProcessing=false permits multiple flight replicas\n'

for deployment in identity-service flight-service passenger-service booking-service; do
    yq -e "select(.kind == \"Deployment\" and .metadata.name == \"${deployment}\") | .spec.strategy.type == \"Recreate\"" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${deployment} should use Recreate while background processing is enabled"
done
yq -e 'select(.kind == "Deployment" and .metadata.name == "gateway") | .spec.strategy.type == "RollingUpdate" and .spec.strategy.rollingUpdate.maxUnavailable == 0 and .spec.strategy.rollingUpdate.maxSurge == 1' "${TMP_DIR}/kind.yaml" >/dev/null ||
    fail "gateway should use RollingUpdate"
yq -e 'select(.kind == "Deployment" and .metadata.name == "flight-service") | .spec.strategy.type == "RollingUpdate" and .spec.strategy.rollingUpdate.maxUnavailable == 0 and .spec.strategy.rollingUpdate.maxSurge == 1' "${TMP_DIR}/scaled-flight.yaml" >/dev/null ||
    fail "backgroundProcessing=false should use RollingUpdate"
printf 'PASS background-processing services use Recreate and other services use RollingUpdate\n'

helm template booking "$CHART" --namespace booking --values "$KIND_VALUES" \
    --set services.identity.signingKey.existingSecret=identity-signing-key \
    --set services.flight.name=flights >"${TMP_DIR}/renamed-flight.yaml"
gateway_flight_address="$(yq -r 'select(.kind == "ConfigMap" and .metadata.name == "gateway-config") | .data."ReverseProxy__Clusters__flight__Destinations__primary__Address"' "${TMP_DIR}/renamed-flight.yaml")"
booking_flight_grpc_address="$(yq -r 'select(.kind == "ConfigMap" and .metadata.name == "booking-service-config") | .data."Services__flight__grpc__0"' "${TMP_DIR}/renamed-flight.yaml")"
[[ "$gateway_flight_address" == "http://flights:80/" ]] || fail "gateway flight destination did not follow services.flight.name"
[[ "$booking_flight_grpc_address" == "http://flights:81" ]] || fail "Booking flight gRPC destination did not follow services.flight.name"
yq -e 'select(.kind == "Deployment" and .metadata.name == "flights") | .spec.selector.matchLabels."app.kubernetes.io/component" == "flight" and .spec.template.metadata.labels."app.kubernetes.io/component" == "flight"' "${TMP_DIR}/renamed-flight.yaml" >/dev/null ||
    fail "renamed flight Deployment should retain the stable flight component selector and pod label"
yq -e 'select(.kind == "Service" and .metadata.name == "flights") | .spec.selector."app.kubernetes.io/component" == "flight"' "${TMP_DIR}/renamed-flight.yaml" >/dev/null ||
    fail "renamed flight Service should retain the stable flight component selector"
printf 'PASS gateway and Booking gRPC addresses follow the configurable service name\n'
printf 'PASS application selectors use stable service keys when names change\n'

for deployment in identity-service flight-service passenger-service booking-service gateway; do
    yq -e "select(.kind == \"Deployment\" and .metadata.name == \"${deployment}\") | .spec.template.spec.containers[0].livenessProbe.httpGet.path == \"/health/live\" and .spec.template.spec.containers[0].readinessProbe.httpGet.path == \"/health/ready\"" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${deployment} does not use the expected liveness/readiness endpoints"
done
for deployment in identity-service flight-service passenger-service booking-service gateway postgres mongo rabbitmq eventstore; do
    yq -e "select(.kind == \"Deployment\" and .metadata.name == \"${deployment}\") | .spec.template.spec.enableServiceLinks == false" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${deployment} does not disable legacy service environment variables"
done
printf 'PASS pod Service DNS is not shadowed by legacy service environment variables\n'
for service in identity-service flight-service passenger-service booking-service; do
    yq -e "select(.kind == \"ConfigMap\" and .metadata.name == \"${service}-config\") | .data.\"RabbitMqOptions__VirtualHost\" == \"/\"" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${service} ConfigMap does not contain the configured RabbitMQ virtual host"
done
printf 'PASS RabbitMQ virtual host is rendered for all application services\n'
for service in flight-service passenger-service; do
    yq -e "select(.kind == \"Service\" and .metadata.name == \"${service}\") | .spec.ports[] | select(.name == \"grpc\" and .port == 81 and .appProtocol == \"kubernetes.io/h2c\")" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${service} does not expose h2c gRPC port 81"
done
for service in identity-service booking-service gateway; do
    if yq -e "select(.kind == \"Service\" and .metadata.name == \"${service}\") | .spec.ports[].name" "${TMP_DIR}/kind.yaml" | grep -qx grpc; then
        fail "${service} unexpectedly exposes a gRPC port"
    fi
done
yq -e 'select(.kind == "Deployment" and .metadata.name == "identity-service") | .spec.template.spec.containers[0].volumeMounts[] | select(.mountPath == "/app/tempkey.jwk" and .subPath == "tempkey.jwk" and .readOnly == true)' "${TMP_DIR}/kind.yaml" >/dev/null ||
    fail "identity signing key is not mounted read-only at its expected subPath"
if yq 'select(.kind == "ConfigMap") | (.data | to_json | contains("Password="))' "${TMP_DIR}/kind.yaml" | grep -qx true; then
    fail "a ConfigMap contains a PostgreSQL password"
fi
ingress_count="$(yq 'select(.kind == "Ingress") | .kind' "${TMP_DIR}/kind.yaml" | wc -l)"
[[ "$ingress_count" -eq 1 ]] || fail "expected exactly one Ingress, found ${ingress_count}"
yq -e 'select(.kind == "Ingress") | .spec.rules[].http.paths[].backend.service | select(.name == "gateway" and .port.number == 80)' "${TMP_DIR}/kind.yaml" >/dev/null ||
    fail "Ingress does not target the gateway service"
for deployment in identity-service flight-service passenger-service booking-service gateway; do
    yq -e "select(.kind == \"Deployment\" and .metadata.name == \"${deployment}\") | .spec.template.spec.containers[0].resources.requests.cpu and .spec.template.spec.containers[0].resources.requests.memory and .spec.template.spec.containers[0].resources.limits.cpu and .spec.template.spec.containers[0].resources.limits.memory" "${TMP_DIR}/kind.yaml" >/dev/null ||
        fail "${deployment} is missing a resource request or limit"
done
printf 'PASS probes, ports, signing mount, secret separation, ingress, and resource requests/limits\n'
