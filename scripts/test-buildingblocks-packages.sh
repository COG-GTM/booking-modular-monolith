#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-1.0.0}"
project="$root/tests/BuildingBlocks.PackageSmoke/BuildingBlocks.PackageSmoke.csproj"

dotnet restore "$project" -p:BuildingBlocksVersion="$version" \
    --source "$root/artifacts/packages" --source https://api.nuget.org/v3/index.json
dotnet run --project "$project" --no-restore -p:BuildingBlocksVersion="$version"
if grep -q 'Microsoft.EntityFrameworkCore/' "$root/tests/BuildingBlocks.PackageSmoke/obj/project.assets.json"; then
    echo "Unexpected EF Core dependency in minimal package consumer" >&2
    exit 1
fi
echo "Minimal package dependency graph contains no EF Core."
