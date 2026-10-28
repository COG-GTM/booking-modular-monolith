import fnmatch
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import yaml

ROOT = Path(__file__).resolve().parents[3]
SERVICES = ("gateway", "identity", "flight", "passenger", "booking")


def load(path):
    return yaml.load((ROOT / path).read_text(encoding="utf-8-sig"), Loader=yaml.BaseLoader)


spec = importlib.util.spec_from_file_location("shared_packages", ROOT / ".github/scripts/shared_packages.py")
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)


class PipelineTests(unittest.TestCase):
    def selected(self, path, event="push"):
        return {
            service for service in SERVICES
            if any(fnmatch.fnmatchcase(path, pattern) for pattern in load(
                f".github/workflows/service-{service}.yml"
            )["on"][event]["paths"])
        }

    def test_module_and_host_changes_are_isolated(self):
        for service in SERVICES:
            name = service.title()
            host = "Gateway" if service == "gateway" else f"Services/{name}.Host"
            for event in ("push", "pull_request"):
                self.assertEqual(self.selected(f"src/{host}/Dockerfile", event), {service})
                if service != "gateway":
                    self.assertEqual(self.selected(
                        f"src/Modules/{name}/tests/Host.Test/Smoke.cs", event
                    ), {service})
                self.assertEqual(self.selected(
                    f".github/workflows/service-{service}.yml", event
                ), {service})

    def test_shared_dependencies_fan_out(self):
        for path in (
            "src/Contracts/Contracts.Messages/Event.cs",
            "src/Contracts/Contracts.Grpc/flight.proto",
            "src/BuildingBlocks/Core/Domain.cs",
            "src/Aspire/src/ServiceDefaults/Extensions.cs",
            "Directory.Build.props", "global.json", "package-lock.json",
            ".config/dotnet-tools.json", ".dockerignore",
            ".github/actions/test/action.yml", ".github/workflows/service-pipeline.yml",
        ):
            for event in ("push", "pull_request"):
                self.assertEqual(self.selected(path, event), set(SERVICES), path)
        self.assertEqual(self.selected("README.md"), set())

    def test_no_pr_publication_or_write_token_in_verification(self):
        for workflow, verify in (("service-pipeline", "verify"), ("shared-packages", "pack")):
            data = load(f".github/workflows/{workflow}.yml")
            self.assertEqual(data["permissions"], {"contents": "read"})
            self.assertNotEqual(data["jobs"][verify].get("permissions", {}).get("packages"), "write")
            publish = data["jobs"]["publish"]
            self.assertEqual(publish["permissions"]["packages"], "write")
            self.assertIn("github.event_name != 'pull_request'", publish["if"])
            self.assertIn("refs/heads/main", publish["if"])
            self.assertIn("refs/heads/devin/ab-488-v2", publish["if"])
        action = load(".github/actions/docker-build-publish/action.yml")
        self.assertEqual(action["inputs"]["push"]["default"], "false")
        for step in action["runs"]["steps"]:
            if step["name"] in ("Login to registry", "Publish image"):
                self.assertIn("inputs.push == 'true'", step["if"])
                self.assertIn("github.event_name != 'pull_request'", step["if"])

    def test_missing_partial_and_complete_scaffolding(self):
        workflow = load(".github/workflows/service-pipeline.yml")
        script = workflow["jobs"]["verify"]["steps"][1]["run"]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "output"
            env = dict(os.environ, PROJECT="Gateway.csproj", DOCKERFILE="Dockerfile", GITHUB_OUTPUT=str(output))
            for complete in (0, 1, 2):
                output.write_text("")
                if complete >= 1:
                    (root / "Gateway.csproj").touch()
                if complete == 2:
                    (root / "Dockerfile").touch()
                run = subprocess.run(["bash", "-e", "-c", script], cwd=root, env=env, capture_output=True, text=True)
                self.assertEqual(run.returncode, 0, run.stderr)
                self.assertEqual(output.read_text().strip(), f"available={'true' if complete == 2 else 'false'}")
                if complete != 2:
                    self.assertIn("::notice::Skipping", run.stdout)

    def test_test_failures_propagate_even_without_trx(self):
        action = load(".github/actions/test/action.yml")
        script = action["runs"]["steps"][0]["run"].replace("${{ inputs.no-restore }}", "false")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "project with spaces").mkdir()
            (root / "project with spaces/Test.csproj").touch()
            dotnet = root / "dotnet"
            dotnet.write_text("#!/bin/sh\nexit 7\n")
            dotnet.chmod(0o755)
            run = subprocess.run(["bash", "-e", "-c", script], cwd=root, env=dict(
                os.environ, PATH=f"{root}:{os.environ['PATH']}"
            ), capture_output=True, text=True)
            self.assertEqual(run.returncode, 1)
            self.assertIn("project with spaces/Test.csproj", run.stdout)


class PackageTests(unittest.TestCase):
    def test_version_gate_supports_new_changed_unchanged_and_relocated_packages(self):
        def package(version, project="src/Contracts/Contracts.Messages/Contracts.Messages.csproj"):
            return {"id": "Messages", "version": version, "project": project}
        self.assertEqual(shared.changed_packages({"Messages": package("1.0.0")}, {}), [package("1.0.0")])
        self.assertEqual(shared.changed_packages({"Messages": package("1.0.0")}, {"Messages": package("1.0.0")}), [])
        self.assertEqual(shared.changed_packages(
            {"Messages": package("1.1.0")}, {"Messages": package("1.0.0")}
        ), [package("1.1.0")])
        self.assertEqual(shared.changed_packages(
            {"Messages": package("1.0.0", "moved.csproj")}, {"Messages": package("1.0.0")}
        ), [])

    def test_real_msbuild_evaluates_imported_versions_and_packability(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Directory.Build.props").write_text(
                "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                "<Version>2.3.4</Version></PropertyGroup></Project>"
            )
            projects = {
                "src/Contracts/Contracts.Messages/Contracts.Messages.csproj": (
                    "<PackageId>BookingModularMonolith.Contracts.Messages</PackageId>"
                ),
                "src/BuildingBlocks/Core/BuildingBlocks.Core.csproj": "",
                "src/BuildingBlocks/BuildingBlocks.csproj": "<IsPackable>false</IsPackable>",
                "src/BuildingBlocks/tests/BuildingBlocks.Test.csproj": "",
            }
            for path, properties in projects.items():
                project = root / path
                project.parent.mkdir(parents=True, exist_ok=True)
                project.write_text(
                    f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>{properties}</PropertyGroup></Project>'
                )
            evaluated = shared.packages(root)
            self.assertEqual(set(evaluated), {"BookingModularMonolith.Contracts.Messages", "BuildingBlocks.Core"})
            self.assertEqual({value["version"] for value in evaluated.values()}, {"2.3.4"})
            archive = io.BytesIO()
            with tarfile.open(fileobj=archive, mode="w") as files:
                for path in root.rglob("*"):
                    if path.is_file():
                        files.add(path, arcname=path.relative_to(root))
            check_output = subprocess.check_output

            def snapshot(command, **kwargs):
                if command[:2] == ["git", "rev-parse"]:
                    return "a" * 40
                if command[:2] == ["git", "archive"]:
                    return archive.getvalue()
                return check_output(command, **kwargs)

            with patch.object(shared.subprocess, "check_output", side_effect=snapshot):
                self.assertEqual(shared.select(root, "HEAD"), [])
                props = root / "Directory.Build.props"
                props.write_text(props.read_text().replace("2.3.4", "2.4.0"))
                self.assertEqual(len(shared.select(root, "HEAD")), 2)
                self.assertEqual(len(shared.select(root, "0" * 40)), 2)
            packed = subprocess.run([
                "dotnet", "pack", str(root / next(iter(projects))),
                "-c", "Release", "--output", str(root / "artifacts"),
            ], cwd=root, capture_output=True, text=True)
            self.assertEqual(packed.returncode, 0, packed.stdout + packed.stderr)
            self.assertTrue((root / "artifacts/BookingModularMonolith.Contracts.Messages.2.4.0.nupkg").exists())


if __name__ == "__main__":
    unittest.main()
