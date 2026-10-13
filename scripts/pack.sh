#!/usr/bin/env bash
# Packs every BuildingBlocks shared library into the local NuGet feed (./artifacts/packages).
# Usage: scripts/pack.sh [--version X.Y.Z] [--configuration Release]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_dir="${repo_root}/artifacts/packages"
configuration="Release"
version_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version_args=("-p:Version=$2")
      shift 2
      ;;
    --configuration | -c)
      configuration="$2"
      shift 2
      ;;
    *)
      echo "unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

mkdir -p "${output_dir}"

mapfile -t projects < <(find "${repo_root}/src/BuildingBlocks" -mindepth 2 -maxdepth 2 \
  -name 'BuildingBlocks.*.csproj' | sort)
projects+=("${repo_root}/src/BuildingBlocks/BuildingBlocks.csproj")

for project in "${projects[@]}"; do
  echo "==> packing ${project#"${repo_root}"/}"
  dotnet pack "${project}" -c "${configuration}" -o "${output_dir}" "${version_args[@]}"
done

echo "packages written to ${output_dir}:"
ls -1 "${output_dir}"
