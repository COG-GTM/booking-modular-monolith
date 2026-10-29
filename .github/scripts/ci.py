"""Dependency-aware CI selection and shared-package packing (stdlib only)."""

import argparse
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


MODULES = ("identity", "flight", "passenger", "booking")
SERVICES = (*MODULES, "gateway")
SHARED = ("src/BuildingBlocks/", "src/Contracts/")
GLOBAL_FILES = {
    "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
    "global.json", "NuGet.Config", "nuget.config", ".dockerignore", ".editorconfig",
    "package.json", "package-lock.json", "booking-modular-monolith.sln",
}


def git(*args):
    return subprocess.check_output(["git", *args]).decode()


def version(xml):
    return ET.fromstring(xml).findtext(".//SharedPackageVersion", default="")


def select(paths, old_version, new_version, force=False):
    shared = force or old_version != new_version or any(
        p.startswith(SHARED) or p in GLOBAL_FILES for p in paths
    )
    all_services = shared or force or any(
        p.startswith((".github/actions/", ".github/scripts/", ".config/", "src/Api/",
                      "src/Aspire/src/ServiceDefaults/"))
        or p in {".github/workflows/ci.yml", ".github/workflows/service.yml"}
        for p in paths
    )
    services = [s for s in SERVICES if all_services or any(
        p.startswith((f"src/Modules/{s.title()}/", f"src/Services/{s.title()}.Host/"))
        or (s == "gateway" and p.startswith("src/Gateway/")) for p in paths
    )]
    apphost = all_services or any(p.startswith("src/Aspire/src/AppHost/") for p in paths)
    return {"services": services, "shared": shared, "apphost": apphost,
            "packages": bool(new_version and old_version != new_version)}


def layout(service, root=Path(".")):
    if service not in SERVICES:
        raise ValueError(f"Unknown service: {service}")
    host = Path("src/Gateway") if service == "gateway" else Path(f"src/Services/{service.title()}.Host")
    projects = sorted((root / host).glob("*.csproj"))
    if projects:
        project = str(projects[0].relative_to(root))
        dockerfile = str(host / "Dockerfile")
        if not (root / dockerfile).is_file():
            raise FileNotFoundError(f"Host exists without its required Dockerfile: {host}")
    elif service == "gateway":
        return None
    else:
        project = f"src/Modules/{service.title()}/src/{service.title()}.csproj"
        dockerfile = ""
    tests = f"src/Modules/{service.title()}/tests" if service != "gateway" else "src/Gateway/tests"
    if not (root / tests).is_dir():
        tests = ""
    return {"project": project, "tests": tests, "dockerfile": dockerfile}


def shared_projects(solution):
    # Only active solution projects: an abandoned net6 Mongo csproj is not buildable.
    return sorted({p.replace("\\", "/") for p in re.findall(r'"([^"\n]+\.csproj)"', solution)
                   if p.replace("\\", "/").startswith(SHARED)
                   and "/tests/" not in p.replace("\\", "/").lower()})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    selection = commands.add_parser("select")
    selection.add_argument("--base", default="")
    selection.add_argument("--head", default="HEAD")
    selection.add_argument("--force", default="false")
    commands.add_parser("layout").add_argument("service", choices=SERVICES)
    pack = commands.add_parser("pack")
    pack.add_argument("--output", default="artifacts/packages")
    args = parser.parse_args()
    if args.command == "layout":
        result = layout(args.service)
        if result is None:
            raise ValueError("Gateway has not been added yet")
        for key, value in result.items():
            print(f"{key}={value}")
    elif args.command == "select":
        base = args.base
        if not base or set(base) == {"0"}:
            paths = git("ls-tree", "-r", "--name-only", args.head).splitlines()
            previous = ""
        else:
            paths = git("diff", "--name-only", "--no-renames", base, args.head).splitlines()
            previous = version(git("show", f"{base}:Directory.Build.props"))
        current = version(git("show", f"{args.head}:Directory.Build.props"))
        result = select(paths, previous, current, args.force == "true")
        result["services"] = [s for s in result["services"] if layout(s) is not None]
        for key, value in result.items():
            print(f"{key}={json.dumps(value, separators=(',', ':'))}")
    else:
        output = Path(args.output).resolve()
        output.mkdir(parents=True, exist_ok=True)
        projects = shared_projects(Path("booking-modular-monolith.sln").read_text())
        if not projects:
            raise ValueError("No shared projects found in solution")
        for project in projects:
            subprocess.run(["dotnet", "pack", project, "-c", "Release", "-o", str(output),
                            "-p:ContinuousIntegrationBuild=true"], check=True)


if __name__ == "__main__":
    main()
