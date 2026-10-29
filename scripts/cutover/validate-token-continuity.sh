#!/usr/bin/env bash

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/cutover/lib.sh
source "$SCRIPT_DIR/lib.sh"

load_state
token="$(cutover_token)"
"$SCRIPT_DIR/cutover.sh" flip identity service

response="$(gateway_probe flight "$token")"
status="$(probe_status "$response")"
if [[ "$status" == 401 || "$status" == 403 ]]; then
  printf 'A token minted before the Identity flip was rejected afterward (HTTP %s).\n' "$status" >&2
  exit 1
fi
printf 'Token minted before the Identity flip remains accepted afterward (HTTP %s).\n' "$status"
