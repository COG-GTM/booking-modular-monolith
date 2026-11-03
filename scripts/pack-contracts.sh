#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$root/artifacts/nuget"
dotnet pack "$root/src/Contracts.Messages/Contracts.Messages.csproj" \
  --configuration Release --output "$root/artifacts/nuget" "$@"
