#!/usr/bin/env bash

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

if [[ -f src/Api/Dockerfile ]]; then
  legacy_commit=HEAD
else
  last_commit="$(git rev-list -n 1 HEAD -- src/Api/Dockerfile)"
  [[ -n "$last_commit" ]] || { printf 'No historical src/Api/Dockerfile revision found.\n' >&2; exit 1; }
  legacy_commit="${last_commit}^"
fi

worktree="$(mktemp -d "$HOME/work/ab505-legacy.XXXXXX")"
rmdir "$worktree"
cleanup() {
  git worktree remove --force "$worktree" >/dev/null 2>&1 || true
  git worktree prune
}
trap cleanup EXIT

git worktree add --detach "$worktree" "$legacy_commit"

key_pattern='(^|/)(tempkey\.jwk|is-signing-key-[^/]+\.(json|jwk)|[^/]*(private|signing|secret)[^/]*\.(json|jwk|pem|pfx|key))$'
mapfile -t signing_keys < <(git -C "$worktree" ls-files | rg -i "$key_pattern" || true)
for key_file in "${signing_keys[@]:-}"; do
  case "$key_file" in
    src/Api/* | src/Modules/Identity/*)
      printf 'Removing committed signing key from legacy build context: %s\n' "$key_file"
      rm -f -- "$worktree/$key_file"
      ;;
  esac
done

docker build -t booking-modular-monolith:legacy -f "$worktree/src/Api/Dockerfile" "$worktree"
