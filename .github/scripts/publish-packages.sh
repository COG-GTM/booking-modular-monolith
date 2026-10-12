#!/usr/bin/env bash
# Packs the shared NuGet packages (src/BuildingBlocks/*, src/Contracts.Grpc, src/Contracts.Messages)
# and pushes every package whose version is not in the feed yet, i.e. only bumped versions.
# Versioning scheme: see docs/ci-cd.md.
#
# Env:
#   NUGET_SOURCE    NuGet v3 service index (e.g. https://nuget.pkg.github.com/<owner>/index.json)
#   NUGET_USER      user for feed reads (GitHub Packages requires auth for reads too)
#   NUGET_API_KEY   token for reads and pushes (GITHUB_TOKEN on GitHub Packages)
#   PUBLISH         "true" pushes; anything else is a dry run that packs every package
#   OUTPUT_DIR      where .nupkg files go (default: artifacts/packages)
#   BASE_REF        optional git ref; packages whose sources changed since it without a version bump get a warning
set -euo pipefail

: "${NUGET_SOURCE:?NUGET_SOURCE is required}"
PUBLISH="${PUBLISH:-false}"
OUTPUT_DIR="${OUTPUT_DIR:-artifacts/packages}"
auth=()
if [ -n "${NUGET_API_KEY:-}" ]; then auth=(-u "${NUGET_USER:-nuget}:${NUGET_API_KEY}"); fi

projects=()
for p in src/BuildingBlocks/*/*.csproj src/Contracts.Grpc/Contracts.Grpc.csproj src/Contracts.Messages/Contracts.Messages.csproj; do
  [ "$(dotnet msbuild "$p" -getProperty:IsPackable)" = "true" ] && projects+=("$p")
done

base_address=$(curl -fsSL "${auth[@]}" "$NUGET_SOURCE" |
  jq -r '[.resources[] | select((."@type" | tostring) | startswith("PackageBaseAddress/3.0.0"))][0]."@id"')
if [ -z "$base_address" ] || [ "$base_address" = "null" ]; then
  echo "::error::No PackageBaseAddress/3.0.0 resource in $NUGET_SOURCE"
  exit 1
fi
base_address="${base_address%/}"

changed_files=""
if [ -n "${BASE_REF:-}" ]; then
  changed_files=$(git diff --name-only "${BASE_REF}...HEAD" || true)
fi

# True when the project's directory or any Directory.*.props on its path to the repo root changed.
sources_changed() {
  [ -n "$changed_files" ] || return 1
  local dir
  dir="$(dirname "$1")"
  grep -q "^${dir}/" <<<"$changed_files" && return 0
  while :; do
    grep -qE "^(${dir}/)?Directory\.[^/]+\.props$" <<<"$changed_files" && return 0
    [ "$dir" = "." ] && return 1
    dir="$(dirname "$dir")"
  done
}

to_pack=()
summary="| Package | Version | Feed | Action |\n|---|---|---|---|\n"
for p in "${projects[@]}"; do
  props=$(dotnet msbuild "$p" -getProperty:PackageId -getProperty:PackageVersion)
  id=$(jq -r '.Properties.PackageId' <<<"$props")
  version=$(jq -r '.Properties.PackageVersion' <<<"$props")
  id_lower="${id,,}"
  versions_json=$(mktemp)
  status=$(curl -sS -o "$versions_json" -w '%{http_code}' "${auth[@]}" "$base_address/$id_lower/index.json")
  case "$status" in
    200) published=$(jq -r '.versions[]' "$versions_json" | tr '[:upper:]' '[:lower:]') ;;
    404) published="" ;;
    *)
      echo "::error::Feed lookup for $id returned HTTP $status"
      cat "$versions_json"
      exit 1
      ;;
  esac
  rm -f "$versions_json"

  if grep -qxF "${version,,}" <<<"$published"; then
    action="up to date"
    if sources_changed "$p"; then
      echo "::warning file=$p::$id sources changed but $version is already published; bump the version to publish it"
      action="changed, version not bumped"
    fi
    [ "$PUBLISH" = "true" ] || to_pack+=("$p")
  else
    action="publish"
    to_pack+=("$p")
  fi
  feed_versions=$(tr '\n' ' ' <<<"$published")
  summary+="| $id | $version | ${feed_versions:-none} | $action |\n"
  echo "$id $version -> $action"
done

# Pack into a fresh directory so only this run's packages are pushed, then copy them to OUTPUT_DIR.
mkdir -p "$OUTPUT_DIR"
pack_dir=$(mktemp -d)
trap 'rm -rf "$pack_dir"' EXIT
for p in "${to_pack[@]}"; do
  dotnet pack "$p" -c Release -o "$pack_dir" -p:ContinuousIntegrationBuild=true
done
shopt -s nullglob
packages=("$pack_dir"/*.nupkg)
if [ ${#packages[@]} -gt 0 ]; then cp "${packages[@]}" "$OUTPUT_DIR"/; fi

if [ "$PUBLISH" = "true" ]; then
  if [ ${#packages[@]} -eq 0 ]; then
    echo "No package version bumps; nothing to publish."
  else
    for pkg in "${packages[@]}"; do
      dotnet nuget push "$pkg" --source "$NUGET_SOURCE" --api-key "$NUGET_API_KEY" --skip-duplicate
    done
  fi
else
  echo "Dry run: packed ${#to_pack[@]} package(s), nothing pushed."
fi

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  { echo "### Shared NuGet packages (publish=$PUBLISH)"; echo -e "$summary"; } >> "$GITHUB_STEP_SUMMARY"
fi
