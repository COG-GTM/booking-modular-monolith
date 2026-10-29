#!/usr/bin/env bash

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/cutover/lib.sh
source "$SCRIPT_DIR/lib.sh"

print_usage() {
  printf 'Usage: %s {up [--reset]|status|flip <identity|flight|passenger|booking> <service|monolith>|all|decommission}\n' "$0"
}

initialize_state() {
  IDENTITY_OWNER=monolith
  FLIGHT_OWNER=monolith
  PASSENGER_OWNER=monolith
  BOOKING_OWNER=monolith
  FLIP_IN_PROGRESS=
  MONOLITH_IMAGE="${MONOLITH_IMAGE:-booking-modular-monolith:legacy}"
  refresh_derived_state
  write_state
  write_gateway_overrides
}

status() {
  if [[ ! -f "$STATE_FILE" ]]; then
    printf 'Cutover state is not initialized.\n'
    return
  fi
  load_state
  printf 'identity=%s flight=%s passenger=%s booking=%s\n' \
    "$IDENTITY_OWNER" "$FLIGHT_OWNER" "$PASSENGER_OWNER" "$BOOKING_OWNER"
  printf 'flip_in_progress=%s\n' "${FLIP_IN_PROGRESS:-none}"
  compose ps
}

preflight() {
  local context="$1" target="$2"
  if [[ "$target" == monolith ]]; then
    compose up -d --no-deps monolith
  fi
  wait_healthy monolith
  wait_healthy "$context-service"
}

set_stage() {
  FLIP_IN_PROGRESS="$1:$2:$3"
  write_state
}

stage_before() {
  local current="$1" requested="$2" order=(begin preflight freeze quiesce stop copy start route)
  local current_index=-1 requested_index=-1 index
  for index in "${!order[@]}"; do
    [[ "${order[$index]}" == "$current" ]] && current_index="$index"
    [[ "${order[$index]}" == "$requested" ]] && requested_index="$index"
  done
  ((current_index < requested_index))
}

recreate_affected_containers() {
  local context="$1" context_service="${1}-service" service
  local -a services=(monolith "$context_service")
  if [[ "$context_service" != booking-service ]] && [[ -n "$(compose ps --status running -q booking-service)" ]]; then
    services+=(booking-service)
  fi
  compose up -d --no-deps --force-recreate "${services[@]}"
  for service in "${services[@]}"; do
    wait_healthy "$service"
  done
}

stop_context_service() {
  local service="$1" running
  compose stop "$service"
  running="$(compose ps --status running -q "$service")"
  if [[ -n "$running" ]]; then
    printf 'Refusing to copy while %s is still running (container %s).\n' "$service" "$running" >&2
    return 1
  fi
}

set_context_background() {
  local context="$1" owner="$2" enabled="$3" variable
  variable="${context^^}_${owner^^}_BACKGROUND"
  printf -v "$variable" '%s' "$enabled"
}

recover_route_probe_failure() {
  local context="$1" target="$2" source_owner="$3" copy_direction
  set_context_freeze "$context"
  FLIP_IN_PROGRESS="$context:$target:recover"
  set_context_background "$context" monolith false
  write_state
  stop_context_service "$context-service"
  compose up -d --no-deps --force-recreate monolith
  wait_healthy monolith
  if [[ "$target" == service ]]; then
    copy_direction=to-monolith
  else
    copy_direction=to-service
  fi
  if ! "$SCRIPT_DIR/copy-data.sh" "$context" "$copy_direction"; then
    printf 'Reverse copy failed; %s remains frozen at recover stage. Resolve the copy error, then rerun flip %s %s; do not clear the freeze or edit ownership manually.\n' \
      "$context" "$context" "$target" >&2
    return 1
  fi
  set_owner "$context" "$source_owner"
  refresh_derived_state
  set_context_background "$context" "$source_owner" false
  write_state
  recreate_affected_containers "$context"
  refresh_derived_state
  FLIP_IN_PROGRESS=
  write_state
  recreate_affected_containers "$context"
  clear_freeze
}

abort_interrupted_flip() {
  local context="$1" owner="$2"
  printf 'Aborting unfinished flip without copying data; %s remains authoritative.\n' "$owner"
  set_owner "$context" "$owner"
  refresh_derived_state
  write_state
  set_context_freeze "$context"
  recreate_affected_containers "$context"
  clear_freeze
  FLIP_IN_PROGRESS=
  write_state
  printf 'Flip aborted. Host is dark and the monolith route is restored.\n'
}

flip() {
  local context="$1" target="$2" owner token since source_owner destination_address
  [[ "$target" == service || "$target" == monolith ]] || { print_usage; return 2; }
  load_state
  owner="$(owner_for "$context")"
  if [[ -n "${FLIP_IN_PROGRESS:-}" ]]; then
    IFS=: read -r pending_context pending_target stage <<<"$FLIP_IN_PROGRESS"
    if [[ "$pending_context" == "$context" && "$pending_target" == "$target" ]]; then
      printf 'Resuming %s -> %s after stage %s.\n' "$context" "$target" "$stage"
      if [[ "$stage" == recover ]]; then
        if [[ "$target" == service ]]; then
          source_owner=monolith
        else
          source_owner=service
        fi
        if ! recover_route_probe_failure "$context" "$target" "$source_owner"; then
          return 1
        fi
        printf 'Route probe recovery restored %s ownership for %s; the interrupted flip was not resumed.\n' \
          "$source_owner" "$context" >&2
        return 1
      fi
    elif [[ "$pending_context" == "$context" && "$owner" == "$target" ]]; then
      abort_interrupted_flip "$context" "$owner"
      return
    else
      printf 'Flip %s is in progress; resume it or abort it before starting another.\n' "$FLIP_IN_PROGRESS" >&2
      return 1
    fi
  else
    if [[ "$owner" == "$target" ]]; then
      printf '%s is already owned by %s.\n' "$context" "$target"
      return
    fi
    stage=begin
    set_stage "$context" "$target" "$stage"
  fi
  source_owner="$owner"
  token="$(cutover_token)"
  if stage_before "$stage" preflight; then
    preflight "$context" "$target"
    set_stage "$context" "$target" preflight
  fi
  if stage_before "$stage" freeze; then
    set_context_freeze "$context"
    probe_frozen "$context" "$token"
    set_stage "$context" "$target" freeze
  fi
  if [[ "$source_owner" == monolith ]]; then
    if stage_before "$stage" quiesce; then
      case "$context" in
        identity) IDENTITY_MONOLITH_BACKGROUND=false ;;
        flight) FLIGHT_MONOLITH_BACKGROUND=false; FLIGHT_GRPC_ADDRESS=http://flight-service:81; FLIGHT_GRPC_ADDRESS_FOR_SERVICES=http://flight-service:81 ;;
        passenger) PASSENGER_MONOLITH_BACKGROUND=false; PASSENGER_GRPC_ADDRESS=http://passenger-service:81; PASSENGER_GRPC_ADDRESS_FOR_SERVICES=http://passenger-service:81 ;;
        booking) BOOKING_MONOLITH_BACKGROUND=false ;;
      esac
      write_state
      compose up -d --no-deps monolith
      if [[ "$(compose ps -q booking-service)" != "" ]]; then
        compose up -d --no-deps booking-service
      fi
      wait_healthy monolith
      set_stage "$context" "$target" quiesce
    fi
    if stage_before "$stage" stop; then
      stop_context_service "$context-service"
      set_stage "$context" "$target" stop
    fi
    if stage_before "$stage" copy; then
      stop_context_service "$context-service"
      "$SCRIPT_DIR/copy-data.sh" "$context" to-service
      set_stage "$context" "$target" copy
    fi
    if stage_before "$stage" start; then
      case "$context" in
        identity) IDENTITY_SERVICE_BACKGROUND=true ;;
        flight) FLIGHT_SERVICE_BACKGROUND=true ;;
        passenger) PASSENGER_SERVICE_BACKGROUND=true ;;
        booking) BOOKING_SERVICE_BACKGROUND=true ;;
      esac
      write_state
      compose up -d --no-deps "$context-service"
      wait_healthy "$context-service"
      set_stage "$context" "$target" start
    fi
  else
    if stage_before "$stage" stop; then
      stop_context_service "$context-service"
      set_stage "$context" "$target" stop
    fi
    if stage_before "$stage" copy; then
      stop_context_service "$context-service"
      "$SCRIPT_DIR/copy-data.sh" "$context" to-monolith
      set_stage "$context" "$target" copy
    fi
    if stage_before "$stage" start; then
      case "$context" in
        identity) IDENTITY_MONOLITH_BACKGROUND=true; IDENTITY_SERVICE_BACKGROUND=false ;;
        flight) FLIGHT_MONOLITH_BACKGROUND=true; FLIGHT_SERVICE_BACKGROUND=false; FLIGHT_GRPC_ADDRESS=http://monolith:81; FLIGHT_GRPC_ADDRESS_FOR_SERVICES=http://monolith:81 ;;
        passenger) PASSENGER_MONOLITH_BACKGROUND=true; PASSENGER_SERVICE_BACKGROUND=false; PASSENGER_GRPC_ADDRESS=http://monolith:81; PASSENGER_GRPC_ADDRESS_FOR_SERVICES=http://monolith:81 ;;
        booking) BOOKING_MONOLITH_BACKGROUND=true; BOOKING_SERVICE_BACKGROUND=false ;;
      esac
      write_state
      compose up -d --no-deps monolith
      if [[ "$(compose ps -q booking-service)" != "" ]]; then
        compose up -d --no-deps booking-service
      fi
      wait_healthy monolith
      compose up -d --no-deps "$context-service"
      wait_healthy "$context-service"
      set_stage "$context" "$target" start
    fi
  fi
  if stage_before "$stage" route; then
    set_owner "$context" "$target"
    refresh_derived_state
    set_context_freeze "$context"
    since="$(date --iso-8601=seconds)"
    clear_freeze
    destination_address="$(cluster_address "$context")"
    if ! probe_routed "$context" "$destination_address" "$token" "$since"; then
      if ! recover_route_probe_failure "$context" "$target" "$source_owner"; then
        printf 'Route probe failed; recovery is incomplete and %s remains frozen at recover stage.\n' \
          "$context" >&2
        return 1
      fi
      printf 'Route probe failed; restored %s ownership for %s after container recovery.\n' \
        "$source_owner" "$context" >&2
      return 1
    fi
    FLIP_IN_PROGRESS=
    write_state
    printf '%s now owned by %s.\n' "$context" "$target"
  fi
}

all() {
  local context
  for context in flight passenger booking identity; do
    flip "$context" service
  done
}

up() {
  local context target stage context_service defer_context_service=
  local -a services=(flight-service passenger-service booking-service gateway)
  if [[ $# -gt 1 ]] || { [[ $# -eq 1 ]] && [[ "$1" != --reset ]]; }; then
    print_usage
    return 2
  fi
  if [[ "${1:-}" == --reset ]] || [[ ! -f "$STATE_FILE" ]]; then
    if [[ -f "$STATE_FILE" ]]; then
      load_state
    fi
    initialize_state
  else
    load_state
    if [[ -n "${FLIP_IN_PROGRESS:-}" ]]; then
      IFS=: read -r context target stage <<<"$FLIP_IN_PROGRESS"
      set_context_freeze "$context"
      if [[ "$stage" == stop || "$stage" == copy || "$stage" == recover ]]; then
        defer_context_service="${context}-service"
      fi
    else
      clear_freeze
    fi
    printf 'Using persisted cutover ownership and worker state.\n'
  fi
  compose up -d postgres mongo eventstore rabbitmq
  wait_healthy postgres
  wait_healthy mongo
  wait_healthy eventstore
  wait_healthy rabbitmq
  if [[ "$defer_context_service" != identity-service ]]; then
    compose up -d --no-deps identity-service
    wait_healthy identity-service
  fi
  compose up -d --no-deps monolith
  wait_healthy monolith
  if [[ -n "$defer_context_service" ]]; then
    local -a services_to_start=()
    for context_service in "${services[@]}"; do
      [[ "$context_service" == "$defer_context_service" ]] || services_to_start+=("$context_service")
    done
    services=("${services_to_start[@]}")
  fi
  compose up -d --no-deps "${services[@]}"
  for context_service in "${services[@]}"; do
    wait_healthy "$context_service"
  done
}

decommission() {
  load_state
  if [[ -n "${FLIP_IN_PROGRESS:-}" ]]; then
    printf 'Cannot decommission while flip %s is in progress.\n' "$FLIP_IN_PROGRESS" >&2
    return 1
  fi
  local context
  for context in identity flight passenger booking; do
    if [[ "$(owner_for "$context")" != service ]]; then
      printf 'Cannot decommission: %s is still owned by %s.\n' "$context" "$(owner_for "$context")" >&2
      return 1
    fi
  done
  IDENTITY_MONOLITH_BACKGROUND=false
  FLIGHT_MONOLITH_BACKGROUND=false
  PASSENGER_MONOLITH_BACKGROUND=false
  BOOKING_MONOLITH_BACKGROUND=false
  write_state
  compose stop monolith
}

main() {
  local command="${1:-}"
  case "$command" in
    up) shift; up "$@" ;;
    status) status ;;
    flip)
      [[ $# == 3 ]] || { print_usage; return 2; }
      flip "$2" "$3"
      ;;
    all) all ;;
    decommission) decommission ;;
    *) print_usage; return 2 ;;
  esac
}

main "$@"
