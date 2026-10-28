#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-}"
feed="$root/artifacts/packages"
pack_args=()
if [[ -n "$version" ]]; then
    pack_args+=("-p:Version=$version" "-p:PackageVersion=$version")
fi
mkdir -p "$feed"

for project in "$root"/src/Contracts/*/Contracts.*.csproj; do
    case "$project" in
        */tests/*) continue ;;
    esac
    dotnet pack "$project" -c Release "${pack_args[@]}" -o "$feed" --nologo
done

for project in "$root"/src/BuildingBlocks/*/BuildingBlocks.*.csproj "$root"/src/BuildingBlocks/BuildingBlocks.csproj; do
    dotnet pack "$project" -c Release "${pack_args[@]}" -o "$feed" --nologo
done

echo "Local NuGet feed: $feed (version ${version:-declared per project})"
