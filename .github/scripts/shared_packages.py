"""Select shared NuGet packages only when their evaluated version changes."""

import argparse
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile


def packages(root):
    projects = set(root.glob("src/Contracts/**/Contracts.*.csproj"))
    projects.update(root.glob("src/BuildingBlocks/**/BuildingBlocks.*.csproj"))
    projects.update(root.glob("src/BuildingBlocks/BuildingBlocks.csproj"))
    result = {}
    for project in sorted(projects):
        if {"tests", "obj", "bin"}.intersection(project.relative_to(root).parts):
            continue
        output = subprocess.check_output(
            [
                "dotnet", "msbuild", str(project), "-nologo",
                "-p:Configuration=Release",
                "-getProperty:PackageId,PackageVersion,IsPackable",
            ],
            cwd=root, text=True,
        )
        properties = json.loads(output)["Properties"]
        if properties["IsPackable"].lower() != "true":
            continue
        package_id = properties["PackageId"]
        if package_id in result:
            raise ValueError(f"Duplicate shared PackageId: {package_id}")
        result[package_id] = {
            "project": project.relative_to(root).as_posix(),
            "id": package_id,
            "version": properties["PackageVersion"],
        }
    return result


def changed_packages(current, previous):
    return [
        package for package_id, package in current.items()
        if package_id not in previous
        or package["version"] != previous[package_id]["version"]
    ]


def select(root, base):
    current = packages(root)
    if base and set(base) == {"0"}:
        return changed_packages(current, {})
    revision = subprocess.check_output(
        ["git", "rev-parse", "--verify", f"{base or 'HEAD^'}^{{commit}}"],
        cwd=root, text=True,
    ).strip()
    archive = subprocess.check_output(["git", "archive", revision], cwd=root)
    with tempfile.TemporaryDirectory(prefix="package-baseline-") as directory:
        baseline = Path(directory)
        with tarfile.open(fileobj=io.BytesIO(archive)) as files:
            files.extractall(baseline, filter="data")
        previous = packages(baseline)
    return changed_packages(current, previous)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path(subprocess.check_output(
        ["git", "rev-parse", "--show-toplevel"], text=True,
    ).strip())
    selected = select(root, args.base)
    args.output.write_text(json.dumps(selected, indent=2) + "\n")
    if "GITHUB_OUTPUT" in os.environ:
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"count={len(selected)}\n")
    if not selected:
        print("::notice::No shared package version changes; nothing to publish.")
    for package in selected:
        print(f"{package['id']} {package['version']} ({package['project']})")


if __name__ == "__main__":
    main()
