#!/usr/bin/env bash
# Packs every BuildingBlocks.* library (and the transition aggregate) into a local NuGet feed, after the
# Contracts.Messages package they depend on (packed with its own version, see scripts/pack-contracts.sh).
# Usage: scripts/pack-building-blocks.sh [version] [feed-dir]
#   version  SemVer 2.0 package version (default: SharedPackageVersion from Directory.Build.props)
#   feed-dir output folder used as the local feed (default: artifacts/nuget)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-}"
feed="${2:-$repo_root/artifacts/nuget}"
semver='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$'

if [[ -n "$version" && ! "$version" =~ $semver ]]; then
  echo "error: '$version' is not a SemVer 2.0 version (e.g. 1.2.0 or 1.3.0-preview.1)" >&2
  exit 1
fi

mkdir -p "$feed"
version_args=()
[[ -n "$version" ]] && version_args=("-p:SharedPackageVersion=$version")

"$repo_root/scripts/pack-contracts.sh" "" "$feed"

mapfile -t projects < <(find "$repo_root/src/BuildingBlocks" -name 'BuildingBlocks*.csproj' -not -path '*/tests/*' | sort)
for project in "${projects[@]}"; do
  echo "==> packing $(basename "$project" .csproj)"
  dotnet pack "$project" -c Release -o "$feed" -nologo -v quiet "${version_args[@]}"
done

echo "Packed ${#projects[@]} packages into $feed"
