#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-1.0.0}"
feed="$root/artifacts/packages"
mkdir -p "$feed"

for project in "$root"/src/BuildingBlocks/*/BuildingBlocks.*.csproj "$root"/src/BuildingBlocks/BuildingBlocks.csproj; do
    dotnet pack "$project" -c Release -p:Version="$version" -o "$feed" --nologo
done

echo "Local NuGet feed: $feed (version $version)"
