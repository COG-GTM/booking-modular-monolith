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
  git worktree remove "$worktree" >/dev/null 2>&1 || true
}
trap cleanup EXIT

git worktree add --detach "$worktree" "$legacy_commit"
docker build -t booking-modular-monolith:legacy -f "$worktree/src/Api/Dockerfile" "$worktree"
