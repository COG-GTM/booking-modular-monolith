#!/usr/bin/env bash
# Decommission the monolith host (src/Api) from a running cut-over topology (AB-248, runbook §8).
#
#   decommission-monolith.sh [--target compose|k8s] [--check-only] [--quiet-hours N] [options...]
#
# 1. Preconditions: every gateway cluster points at its standalone service and the gateway has not
#    proxied a single request to the monolith cluster during the last N hours (default 168 = 7 days;
#    compose: gateway container logs, k8s: gateway pod logs).
# 2. Stops and removes the monolith host (compose: stop + rm booking_modular_monolith; k8s: scale the
#    monolith deployment to 0). The gateway keeps running; its monolith fallback cluster now has no
#    destination, exactly like the plain docker-compose.services.yaml topology.
# 3. Proves the gateway still serves all four contexts from the services (route-flip.sh smoke per
#    context) with the monolith gone.
# Bring the host back with `docker compose ... up -d booking_modular_monolith` (compose) or by
# scaling the deployment up again (k8s). Removing src/Api from the repository is the follow-up
# checklist in docs/migration/cutover-runbook.md once this has run in production.
#
# Options after the ones above are passed to route-flip.sh (e.g. --gateway-url, --namespace).
# Smoke credentials: SMOKE_USERNAME, SMOKE_PASSWORD, SMOKE_CLIENT_ID, SMOKE_CLIENT_SECRET.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$(cd "$SCRIPT_DIR/../docker-compose" && pwd)"
COMPOSE=(docker compose -f "$COMPOSE_DIR/docker-compose.services.yaml" -f "$COMPOSE_DIR/docker-compose.cutover.yaml")
IFS=: read -ra _extra <<<"${EXTRA_COMPOSE_FILES:-}"
for _f in "${_extra[@]}"; do COMPOSE+=(-f "$_f"); done
CONTEXTS=(identity flight passenger booking)
MONOLITH=booking_modular_monolith
MONOLITH_DEPLOYMENT="${MONOLITH_DEPLOYMENT:-booking-modular-monolith}"

TARGET=compose
CHECK_ONLY=false
QUIET_HOURS="${QUIET_HOURS:-168}"
PASSTHRU=()
while (($#)); do
  case "$1" in
    --target) TARGET="$2"; shift 2 ;;
    --check-only) CHECK_ONLY=true; shift ;;
    --quiet-hours) QUIET_HOURS="$2"; shift 2 ;;
    --namespace) NAMESPACE="$2"; PASSTHRU+=("$1" "$2"); shift 2 ;;
    -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) PASSTHRU+=("$1"); shift ;;
  esac
done
[[ "$TARGET" == compose || "$TARGET" == k8s ]] || { echo "--target must be compose or k8s" >&2; exit 2; }
PASSTHRU+=(--target "$TARGET")

section() { printf '\n=== %s ===\n' "$*"; }
fail() { echo "FAIL: $*" >&2; exit 1; }
kubectl_ns() { if [[ -n "${NAMESPACE:-}" ]]; then kubectl -n "$NAMESPACE" "$@"; else kubectl "$@"; fi; }

gateway_logs() {
  if [[ "$TARGET" == k8s ]]; then
    kubectl_ns logs deploy/gateway --all-containers --since="${QUIET_HOURS}h" 2>/dev/null
  else
    docker logs --since "${QUIET_HOURS}h" "$("${COMPOSE[@]}" ps -q gateway)" 2>&1
  fi
}

section "precondition 1: every cluster served by its service"
"$SCRIPT_DIR/route-flip.sh" status "${PASSTHRU[@]}" | tee /tmp/decommission-status.$$
for ctx in "${CONTEXTS[@]}"; do
  grep -qE "^${ctx} .* service$" /tmp/decommission-status.$$ || fail "$ctx is not served by its service; flip it first (route-flip.sh $ctx service)"
done
rm -f /tmp/decommission-status.$$

section "precondition 2: no gateway request proxied to the monolith in the last ${QUIET_HOURS}h"
hits="$(gateway_logs | grep -c "Proxying to http://${MONOLITH}" || true)"
echo "requests proxied to ${MONOLITH}: ${hits}"
((hits == 0)) || fail "the monolith still received traffic (fallback routes or a stale client); investigate before decommissioning"

if $CHECK_ONLY; then echo "preconditions met (check-only, nothing changed)"; exit 0; fi

section "remove the monolith host"
if [[ "$TARGET" == k8s ]]; then
  kubectl_ns scale "deploy/$MONOLITH_DEPLOYMENT" --replicas=0
  kubectl_ns rollout status "deploy/$MONOLITH_DEPLOYMENT" --timeout=180s
else
  "${COMPOSE[@]}" stop "$MONOLITH"
  "${COMPOSE[@]}" rm -f "$MONOLITH"
  "${COMPOSE[@]}" ps --format '{{.Name}} {{.Status}}'
fi

section "verify: gateway serves all contexts from the services without the monolith"
for ctx in "${CONTEXTS[@]}"; do
  "$SCRIPT_DIR/route-flip.sh" "$ctx" service "${PASSTHRU[@]}"
done
"$SCRIPT_DIR/route-flip.sh" status "${PASSTHRU[@]}"

section "result"
echo "PASS: monolith host removed; all four contexts smoke-tested against their services through the gateway."
echo "Next: the code-removal checklist in docs/migration/cutover-runbook.md §8 (delete src/Api, monolith compose/Aspire/CI wiring, gateway monolith cluster)."
