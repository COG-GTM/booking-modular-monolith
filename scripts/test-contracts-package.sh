#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-1.0.0}"
project="$root/src/Contracts/Contracts.Messages/Contracts.Messages.csproj"
feed="$root/artifacts/packages"
identity="$root/src/Modules/Identity/src/Identity.csproj"
passenger="$root/src/Modules/Passenger/src/Passenger.csproj"
identity_tests="$root/src/Modules/Identity/tests/Integration.Test/Integration.Test.csproj"
passenger_tests="$root/src/Modules/Passenger/tests/Integration.Test/Integration.Test.csproj"

mkdir -p "$feed"
dotnet pack "$project" -c Release -p:Version="$version" -o "$feed" --nologo

package_cache="${NUGET_PACKAGES:-$HOME/.nuget/packages}/bookingmodularmonolith.contracts.messages/$version"
rm -rf "$package_cache"

dotnet test "$root/src/Contracts/tests/Contracts.Messages.Test" \
    -p:UseContractsPackage=true -p:ContractsMessagesVersion="$version" --nologo

for consumer in "$identity" "$passenger" "$identity_tests" "$passenger_tests"; do
    dotnet build "$consumer" -p:UseContractsPackage=true -p:ContractsMessagesVersion="$version" --nologo
done

python3 - "$version" "$identity" "$passenger" "$identity_tests" "$passenger_tests" <<'PY'
import json
import pathlib
import sys

version, *projects = sys.argv[1:]
package_prefix = "bookingmodularmonolith.contracts.messages/"
for project in projects:
    assets_path = pathlib.Path(project).parent / "obj" / "project.assets.json"
    assets = json.loads(assets_path.read_text())
    package = next(
        (
            (name, details)
            for name, details in assets["libraries"].items()
            if name.lower() == f"{package_prefix}{version.lower()}"
        ),
        None,
    )
    if package is None or package[1].get("type") != "package":
        raise SystemExit(f"Expected package-mode asset in {assets_path}")
    print(f"PACKAGE {package[0]} type={package[1]['type']} project={project}")
PY

for consumer in "$identity" "$passenger" "$identity_tests" "$passenger_tests"; do
    dotnet build "$consumer" --nologo
done

python3 - "$identity" "$passenger" "$identity_tests" "$passenger_tests" <<'PY'
import json
import pathlib
import sys

package_prefix = "bookingmodularmonolith.contracts.messages/"
for project in sys.argv[1:]:
    assets_path = pathlib.Path(project).parent / "obj" / "project.assets.json"
    assets = json.loads(assets_path.read_text())
    projects = [
        (name, details)
        for name, details in assets["libraries"].items()
        if name.lower().startswith(package_prefix)
    ]
    if not projects or any(details.get("type") != "project" for _, details in projects):
        raise SystemExit(f"Expected project-mode asset after normal build in {assets_path}")
    print(f"PROJECT {projects[0][0]} type={projects[0][1]['type']} project={project}")
PY

echo "Contracts.Messages $version package-mode tests and consumer builds passed; normal project mode restored."
