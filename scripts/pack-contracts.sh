#!/usr/bin/env bash
# Packs the versioned integration-event contracts (BookingModularMonolith.Contracts.Messages) into a local NuGet feed.
# Usage: scripts/pack-contracts.sh [version] [feed-dir]
#   version  SemVer 2.0 package version (default: ContractsMessagesVersion from src/Contracts/Versions.props)
#   feed-dir output folder used as the local feed (default: artifacts/nuget)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-}"
feed="${2:-$repo_root/artifacts/nuget}"
semver='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$'

if [[ -n "$version" && ! "$version" =~ $semver ]]; then
  echo "error: '$version' is not a SemVer 2.0 version (e.g. 1.1.0 or 2.0.0-preview.1)" >&2
  exit 1
fi

mkdir -p "$feed"
version_args=()
[[ -n "$version" ]] && version_args=("-p:ContractsMessagesVersion=$version")

echo "==> packing Contracts.Messages"
dotnet pack "$repo_root/src/Contracts/Contracts.Messages/Contracts.Messages.csproj" -c Release -o "$feed" -nologo -v quiet "${version_args[@]}"
echo "Packed BookingModularMonolith.Contracts.Messages into $feed"
