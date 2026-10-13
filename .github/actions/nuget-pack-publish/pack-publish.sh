#!/usr/bin/env bash
# Packs every IsPackable solution project under PROJECT_DIRS and pushes each package whose
# PackageVersion is not in the feed yet, so a package is published exactly once per version bump.
# Inputs come from env (see action.yml). PUBLISH!=true is a dry run: pack everything, push nothing.
set -euo pipefail

: "${NUGET_SOURCE:?NUGET_SOURCE is required}"
PROJECT_DIRS="${PROJECT_DIRS:-src/BuildingBlocks/ src/Contracts/}"
SOLUTION="${SOLUTION:-booking-modular-monolith.sln}"
PUBLISH="${PUBLISH:-false}"
OUTPUT_DIR="${OUTPUT_DIR:-artifacts/packages}"
auth=()
if [ -n "${NUGET_API_KEY:-}" ]; then auth=(-u "${NUGET_USER:-nuget}:${NUGET_API_KEY}"); fi

projects=()
while IFS= read -r p; do
  p="${p//\\//}"
  for prefix in $PROJECT_DIRS; do
    if [[ "$p" == "$prefix"* ]] && [ "$(dotnet msbuild "$p" -getProperty:IsPackable)" = "true" ]; then
      projects+=("$p")
      break
    fi
  done
done < <(dotnet sln "$SOLUTION" list | grep -E '\.csproj$')

if [ ${#projects[@]} -eq 0 ]; then
  echo "No packable projects under: $PROJECT_DIRS"
  echo "published=" >> "${GITHUB_OUTPUT:-/dev/null}"
  exit 0
fi

# Resolve the feed's flat-container (PackageBaseAddress) endpoint. In a dry run an unreachable or
# unauthorized feed (e.g. PRs from forks) only downgrades the bump check to a warning.
base_address=""
if index=$(curl -fsSL "${auth[@]}" "$NUGET_SOURCE" 2>/dev/null); then
  base_address=$(jq -r '[.resources[] | select((."@type" | tostring) | startswith("PackageBaseAddress/3.0.0"))][0]."@id" // empty' <<<"$index")
  base_address="${base_address%/}"
fi
if [ -z "$base_address" ]; then
  if [ "$PUBLISH" = "true" ]; then
    echo "::error::Cannot read PackageBaseAddress/3.0.0 from $NUGET_SOURCE"
    exit 1
  fi
  echo "::warning::Cannot read $NUGET_SOURCE; version-bump check skipped (dry run)."
fi

changed_files=""
if [ -n "${BASE_REF:-}" ]; then
  changed_files=$(git diff --name-only "${BASE_REF}...HEAD" || true)
fi

# True when the project's directory or a Directory.*.props on its path to the repo root changed.
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
published_list=()
summary="| Package | Version | In feed | Action |\n|---|---|---|---|\n"
for p in "${projects[@]}"; do
  props=$(dotnet msbuild "$p" -getProperty:PackageId -getProperty:PackageVersion)
  id=$(jq -r '.Properties.PackageId' <<<"$props")
  version=$(jq -r '.Properties.PackageVersion' <<<"$props")
  in_feed="unknown"
  if [ -n "$base_address" ]; then
    versions_json=$(mktemp)
    status=$(curl -sS -o "$versions_json" -w '%{http_code}' "${auth[@]}" "$base_address/${id,,}/index.json" || echo 000)
    case "$status" in
      200)
        if jq -r '.versions[]' "$versions_json" | tr '[:upper:]' '[:lower:]' | grep -qxF "${version,,}"; then
          in_feed="yes"
        else
          in_feed="no"
        fi
        ;;
      404) in_feed="no" ;;
      *)
        rm -f "$versions_json"
        if [ "$PUBLISH" = "true" ]; then
          echo "::error::Feed lookup for $id returned HTTP $status"
          exit 1
        fi
        echo "::warning::Feed lookup for $id returned HTTP $status (dry run)"
        ;;
    esac
    rm -f "$versions_json"
  fi

  case "$in_feed" in
    yes)
      action="up to date"
      if sources_changed "$p"; then
        echo "::warning file=$p::$id sources changed but $version is already published; bump the version to publish it"
        action="changed, version not bumped"
      fi
      ;;
    no)
      action="publish"
      published_list+=("$id.$version")
      ;;
    *) action="unknown (feed unreadable)" ;;
  esac
  # A dry run packs everything to prove packing works; a publish run packs only new versions.
  if [ "$PUBLISH" != "true" ] || [ "$in_feed" = "no" ]; then to_pack+=("$p"); fi
  summary+="| $id | $version | $in_feed | $action |\n"
  echo "$id $version (in feed: $in_feed) -> $action"
done

# Pack into a fresh directory so only this run's packages are pushed, then copy them to OUTPUT_DIR.
mkdir -p "$OUTPUT_DIR"
pack_dir=$(mktemp -d)
trap 'rm -rf "$pack_dir"' EXIT
repo_url="${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-COG-GTM/booking-modular-monolith}"
for p in "${to_pack[@]}"; do
  # RepositoryUrl links the package to this repository on GitHub Packages (needed for GITHUB_TOKEN pushes).
  dotnet pack "$p" -c Release -o "$pack_dir" -p:ContinuousIntegrationBuild=true \
    -p:RepositoryUrl="$repo_url" -p:RepositoryType=git
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
  echo "Dry run: packed ${#packages[@]} package(s), nothing pushed."
fi

echo "published=${published_list[*]}" >> "${GITHUB_OUTPUT:-/dev/null}"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  { echo "### Shared NuGet packages (publish=$PUBLISH)"; echo -e "$summary"; } >> "$GITHUB_STEP_SUMMARY"
fi
