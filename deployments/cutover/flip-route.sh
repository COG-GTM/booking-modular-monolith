#!/usr/bin/env bash
# Move one bounded context's gateway route between the monolith and its service host (AB-505).
#
#   flip-route.sh status
#   flip-route.sh <identity|flight|passenger|booking> service|monolith [--route-only]
#   flip-route.sh up                 # start the cut-over topology with the current state file
#
# A flip is a write-frozen hand-over of ownership, executed in this order:
#   1. freeze   gateway cluster <m> -> unroutable upstream (clients get 502, nothing writes);
#               flight/passenger also freeze booking, which reaches them over gRPC
#   2. drain    the current owner's outbox/inbox/internal commands until nothing is unprocessed
#   3. release  current owner: Modules:<M>:BackgroundProcessingEnabled=false
#               (flight/passenger: the monolith's Booking gRPC client follows the move to the
#               service here, and comes back in step 5 of a rollback)
#   4. copy     copy-data.sh <m> to-service|to-monolith (Postgres + Mongo, verified row counts)
#   5. acquire  new owner: Modules:<M>:BackgroundProcessingEnabled=true
#   6. route    ReverseProxy__Clusters__<m>__Destinations__monolith__Address -> new owner
#   7. verify   gateway answers the context's routes from the new upstream
# Rerunning the same flip after a failure resumes after the last completed stage (FLIP_<M>_STAGE), so
# a finished copy is never replayed. --route-only skips only the copy (4), for contexts whose stores
# are shared or empty (e.g. a fresh environment); ownership still moves.
#
# Environment: COMPOSE_DIR (compose topology, default deployments/docker-compose), STATE_FILE,
# GATEWAY_URL (default http://localhost:5000), DRY_RUN=1, DRAIN_TIMEOUT_SECONDS.
set -euo pipefail
# shellcheck source=deployments/cutover/lib.sh
source "$(dirname "$0")/lib.sh"

usage() {
  sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

status() {
  local m
  printf '%-10s %-9s %-36s %s\n' CONTEXT TARGET UPSTREAM BACKGROUND-OWNER
  for m in "${MODULES[@]}"; do
    local U owner=monolith
    U="$(upper "$m")"
    [[ "$(state_get "SERVICE_${U}_BACKGROUND" false)" == true ]] && owner=service
    [[ "$(state_get "MONOLITH_${U}_BACKGROUND" true)" == true && "${owner}" == service ]] && owner=BOTH
    [[ "$(state_get "MONOLITH_${U}_BACKGROUND" true)" == false && "${owner}" == monolith ]] && owner=none
    printf '%-10s %-9s %-36s %s\n' "$m" "$(current_target "$m")" \
      "$(state_get "GATEWAY_${U}_UPSTREAM" "${MONOLITH_UPSTREAM}")" "${owner}"
  done
}

# Booking-service calls flight-service/passenger-service over gRPC, so Booking only moves after both
# of them, and they only move back after Booking has.
check_order() {
  local m="$1" to="$2" dep
  if [[ "$m" == booking && "$to" == service ]]; then
    for dep in flight passenger; do
      [[ "$(current_target "$dep")" == service ]] || die "flip ${dep} to service before booking"
    done
  fi
  if [[ "$to" == monolith && ("$m" == flight || "$m" == passenger) ]]; then
    [[ "$(current_target booking)" == monolith ]] || die "roll booking back to the monolith before ${m}"
  fi
}

probe() {
  local m="$1" path since got code auth=()
  case "$m" in
    identity) path=/.well-known/openid-configuration ;;
    *)
      # The gateway authenticates /api routes itself, so the probe needs a token to be proxied.
      path="/api/v1.0/${m}/__cutover-probe"
      auth=(-H "Authorization: Bearer $(gateway_token)")
      ;;
  esac
  since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  sleep 1
  code="$(curl -s -o /dev/null -w '%{http_code}' "${auth[@]}" "${GATEWAY_URL}${path}" || echo 000)"
  got="$(last_upstream "${since}" "${path}")"
  log "verify: GET ${path} -> HTTP ${code} served by ${got:-<nothing>}"
  [[ "${code}" =~ ^[234][0-9][0-9]$ ]] || die "gateway did not reach an upstream for ${m} (HTTP ${code})"
  printf '%s' "${got}"
}

flip() {
  local m="$1" to="$2" route_only="${3:-}" U from container upstream
  require_module "$m"
  U="$(upper "$m")"
  container="$(service_container "$m")"
  case "$to" in
    service)
      from=monolith
      upstream="$(service_upstream "$m")"
      ;;
    monolith)
      from=service
      upstream="${MONOLITH_UPSTREAM}"
      ;;
    *) usage ;;
  esac

  local current
  current="$(current_target "$m")"
  local in_progress
  in_progress="$(state_get "FLIP_${U}_IN_PROGRESS")"
  if [[ -n "${in_progress}" && "${in_progress}" != "${to}" ]]; then
    die "${m} is frozen mid-flip to ${in_progress}; resume with: $0 ${m} ${in_progress}"
  fi

  if [[ "${current}" == "${to}" ]]; then
    log "${m} already routed to ${to}; verifying"
  else
    check_order "$m" "$to"
    log "=== ${m}: ${current} -> ${to} ==="

    # A retry resumes after the last completed stage so a finished copy is never replayed over data the
    # new owner may already have processed.
    local stage
    stage="$(state_get "FLIP_${U}_STAGE")"
    [[ -z "${in_progress}" ]] && stage=""

    log "1/7 freeze ${m} writes at the gateway"
    state_set "FLIP_${U}_IN_PROGRESS" "${to}"
    state_set "GATEWAY_${U}_UPSTREAM" "${FROZEN_UPSTREAM}"
    # Booking reserves seats/reads passengers over gRPC, bypassing the moving context's frozen cluster,
    # so its writes are frozen too until the move is complete.
    if [[ ("$m" == flight || "$m" == passenger) && -z "$(state_get FLIP_BOOKING_HELD_UPSTREAM)" ]]; then
      local held
      held="$(state_get GATEWAY_BOOKING_UPSTREAM)"
      state_set FLIP_BOOKING_HELD_UPSTREAM "${held:-default}"
      state_set GATEWAY_BOOKING_UPSTREAM "${FROZEN_UPSTREAM}"
    fi
    apply gateway

    if [[ -z "${stage}" ]]; then
      log "2/7 drain ${from} outbox for ${m}"
      if [[ "${from}" == monolith ]]; then
        wait_for_drain "$(monolith_persist_db "$m")"
      else
        wait_for_drain "$(service_persist_db "$m")"
      fi

      log "3/7 release background processing on ${from}"
      if [[ "${from}" == monolith ]]; then
        state_set "MONOLITH_${U}_BACKGROUND" false
        [[ "$m" == flight || "$m" == passenger ]] && state_set "MONOLITH_GRPC_${U}" "http://${container}:81"
        apply booking_modular_monolith
      else
        state_set "SERVICE_${U}_BACKGROUND" false
        apply "${container}"
      fi
      stage=released
      state_set "FLIP_${U}_STAGE" "${stage}"
    fi

    if [[ "${stage}" == released ]]; then
      if [[ "${route_only}" == --route-only ]]; then
        log "4/7 skip data copy (--route-only: stores are shared or empty)"
      else
        log "4/7 copy ${m} data to ${to}"
        DRY_RUN="${DRY_RUN}" "${CUTOVER_DIR}/copy-data.sh" "$m" "to-${to}"
      fi
      stage=copied
      state_set "FLIP_${U}_STAGE" "${stage}"
    fi

    log "5/7 acquire background processing on ${to}"
    if [[ "${to}" == service ]]; then
      state_set "SERVICE_${U}_BACKGROUND" true
      apply "${container}"
    else
      state_set "MONOLITH_${U}_BACKGROUND" true
      [[ "$m" == flight || "$m" == passenger ]] && state_set "MONOLITH_GRPC_${U}" "http://localhost:81"
      apply booking_modular_monolith
    fi

    log "6/7 route ${m} -> ${upstream}"
    state_set "GATEWAY_${U}_UPSTREAM" "${upstream}"
    if [[ "$m" == flight || "$m" == passenger ]]; then
      local held
      held="$(state_get FLIP_BOOKING_HELD_UPSTREAM)"
      [[ "${held}" == default ]] && held=""
      state_set GATEWAY_BOOKING_UPSTREAM "${held}"
      state_set FLIP_BOOKING_HELD_UPSTREAM ""
    fi
    apply gateway
    state_set "FLIP_${U}_STAGE" ""
    state_set "FLIP_${U}_IN_PROGRESS" ""
  fi

  [[ "${DRY_RUN}" == 1 ]] && return 0
  log "7/7 verify"
  local served expected
  served="$(probe "$m")"
  expected="$(upstream_host "${upstream}")"
  [[ "${served}" == "${expected}" ]] || die "${m} served by '${served}', expected '${expected}'"
  log "=== ${m} is served by ${to} (${expected}) ==="
}

case "${1:-}" in
  status) status ;;
  up)
    state_init
    # Application services and their infrastructure only; add the observability stack with
    # CUTOVER_UP_SERVICES="" (everything) or an explicit list.
    # shellcheck disable=SC2086
    compose up -d --no-build --wait ${CUTOVER_UP_SERVICES-gateway booking_modular_monolith identity-service flight-service passenger-service booking-service}
    status
    ;;
  "" | -h | --help) usage ;;
  *) flip "$1" "${2:-}" "${3:-}" ;;
esac
