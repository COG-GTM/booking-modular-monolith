import fnmatch
import importlib.util
import io
import json
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


SERVICE_HOSTS = {
    "gateway": ("src/Gateway/Gateway.csproj", "src/Gateway/Dockerfile", "src/Gateway/tests"),
    **{
        service: (
            f"src/Services/{service.title()}.Host/{service.title()}.Host.csproj",
            f"src/Services/{service.title()}.Host/Dockerfile",
            f"src/Modules/{service.title()}/tests",
        )
        for service in ("identity", "flight", "passenger", "booking")
    },
}


def step(steps, name):
    return next(item for item in steps if item.get("name") == name)


class ServiceWorkflowTests(unittest.TestCase):
    def test_each_service_calls_reusable_pipeline_with_its_host_contract(self):
        for service, (project, dockerfile, tests) in SERVICE_HOSTS.items():
            data = load(f".github/workflows/service-{service}.yml")
            self.assertEqual(data["permissions"], {"contents": "read"}, service)
            job = data["jobs"]["pipeline"]
            self.assertEqual(job["uses"], "./.github/workflows/service-pipeline.yml", service)
            self.assertEqual(job["with"], {
                "service": service, "project": project, "dockerfile": dockerfile, "tests": tests,
            }, service)
            group = f"{service}-${{{{ github.event.pull_request.number || github.ref }}}}"
            self.assertEqual(data["concurrency"]["group"], group)
            for event in ("push", "pull_request"):
                self.assertEqual(data["on"][event]["branches"], ["main", "devin/ab-488-v2"], service)

    def test_images_use_ghcr_service_name_and_full_sha_tag(self):
        workflow = load(".github/workflows/service-pipeline.yml")
        verify = step(workflow["jobs"]["verify"]["steps"], "Verify container build without publishing")
        publish = workflow["jobs"]["publish"]["steps"][-1]
        for uses, push in ((verify, "false"), (publish, "true")):
            self.assertEqual(uses["uses"], "./.github/actions/docker-build-publish")
            self.assertEqual(uses["with"]["registry"], "ghcr.io")
            self.assertEqual(uses["with"]["image-name"], "cog-gtm/booking-${{ inputs.service }}")
            self.assertEqual(uses["with"]["tag-name"], "sha-${{ github.sha }}")
            self.assertEqual(uses["with"]["push"], push)
        self.assertIn("needs.verify.outputs.available == 'true'", workflow["jobs"]["publish"]["if"])

    def test_service_tests_are_discovered_only_when_a_project_exists(self):
        workflow = load(".github/workflows/service-pipeline.yml")
        script = step(workflow["jobs"]["verify"]["steps"], "Find service tests")["run"]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "output"
            env = dict(os.environ, TESTS="tests dir", GITHUB_OUTPUT=str(output))
            for layout in ("missing", "empty", "project"):
                output.write_text("")
                if layout == "empty":
                    (root / "tests dir/Unit").mkdir(parents=True)
                if layout == "project":
                    (root / "tests dir/Unit/Unit.Test.csproj").touch()
                run = subprocess.run(["bash", "-e", "-c", script], cwd=root, env=env, capture_output=True, text=True)
                self.assertEqual(run.returncode, 0, run.stderr)
                if layout == "project":
                    self.assertEqual(output.read_text().strip(), "path=tests dir")
                else:
                    self.assertEqual(output.read_text(), "")
                    self.assertIn("::notice::No test projects yet at tests dir.", run.stdout)

    def test_publish_step_pushes_exact_image_and_quotes_summary(self):
        action = load(".github/actions/docker-build-publish/action.yml")
        script = step(action["runs"]["steps"], "Publish image")["run"]
        image = "ghcr.io/cog-gtm/booking-flight:sha-" + "a" * 40
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log, summary = root / "docker.log", root / "summary.md"
            docker = root / "docker"
            docker.write_text(f'#!/bin/sh\necho "$@" >> "{log}"\n')
            docker.chmod(0o755)
            run = subprocess.run(["bash", "-e", "-c", script], cwd=root, env=dict(
                os.environ, PATH=f"{root}:{os.environ['PATH']}", IMAGE=image, GITHUB_STEP_SUMMARY=str(summary),
            ), capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            self.assertEqual(log.read_text().strip(), f"push {image}")
            self.assertEqual(summary.read_text(), f"Published `{image}`\n")

    def test_build_restores_and_builds_only_the_selected_project(self):
        action = load(".github/actions/build/action.yml")
        steps = action["runs"]["steps"]
        for name, expected in (
            ("Restore NuGet packages", ["restore", "project with spaces/Host.csproj"]),
            ("Build Service", ["build", "project with spaces/Host.csproj", "-c", "Release", "--no-restore"]),
        ):
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                log = root / "dotnet.log"
                dotnet = root / "dotnet"
                dotnet.write_text(f'#!/bin/sh\nprintf "%s|" "$@" >> "{log}"\n')
                dotnet.chmod(0o755)
                run = subprocess.run(["bash", "-e", "-c", step(steps, name)["run"]], cwd=root, env=dict(
                    os.environ, PATH=f"{root}:{os.environ['PATH']}", PROJECT="project with spaces/Host.csproj",
                ), capture_output=True, text=True)
                self.assertEqual(run.returncode, 0, run.stderr)
                self.assertEqual(log.read_text().rstrip("|").split("|"), expected)

    def test_test_action_succeeds_when_every_project_passes(self):
        action = load(".github/actions/test/action.yml")
        script = action["runs"]["steps"][0]["run"].replace("${{ inputs.no-restore }}", "true")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("A", "B"):
                (root / name).mkdir()
                (root / name / f"{name}.Test.csproj").touch()
            log = root / "dotnet.log"
            dotnet = root / "dotnet"
            dotnet.write_text(f'#!/bin/sh\necho "$@" >> "{log}"\n')
            dotnet.chmod(0o755)
            run = subprocess.run(["bash", "-e", "-c", script], cwd=root, env=dict(
                os.environ, PATH=f"{root}:{os.environ['PATH']}"
            ), capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            calls = log.read_text().splitlines()
            self.assertEqual(len(calls), 2)
            self.assertTrue(all("--no-restore" in call for call in calls))


class SharedPackageSelectionTests(unittest.TestCase):
    def msbuild(self, properties_by_project):
        def fake(command, **kwargs):
            name = Path(command[2]).name
            return json.dumps({"Properties": properties_by_project[name]})
        return fake

    def project(self, root, path):
        project = root / path
        project.parent.mkdir(parents=True, exist_ok=True)
        project.touch()

    def test_duplicate_package_ids_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.project(root, "src/Contracts/Contracts.A/Contracts.A.csproj")
            self.project(root, "src/BuildingBlocks/Core/BuildingBlocks.Core.csproj")
            same = {"PackageId": "Shared", "PackageVersion": "1.0.0", "IsPackable": "true"}
            with patch.object(shared.subprocess, "check_output", side_effect=self.msbuild({
                "Contracts.A.csproj": same, "BuildingBlocks.Core.csproj": same,
            })):
                with self.assertRaisesRegex(ValueError, "Duplicate shared PackageId: Shared"):
                    shared.packages(root)

    def test_build_output_and_unrelated_projects_are_not_evaluated(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for path in (
                "src/Contracts/Contracts.A/Contracts.A.csproj",
                "src/Contracts/Contracts.A/obj/Contracts.Obj.csproj",
                "src/BuildingBlocks/bin/BuildingBlocks.Bin.csproj",
                "src/Contracts/Other/Other.csproj",
                "src/Modules/Flight/src/Contracts.Flight.csproj",
            ):
                self.project(root, path)
            evaluated = []

            def fake(command, **kwargs):
                evaluated.append(Path(command[2]).relative_to(root).as_posix())
                return json.dumps({"Properties": {
                    "PackageId": "A", "PackageVersion": "1.0.0", "IsPackable": "True",
                }})

            with patch.object(shared.subprocess, "check_output", side_effect=fake):
                self.assertEqual(shared.packages(root), {"A": {
                    "project": "src/Contracts/Contracts.A/Contracts.A.csproj", "id": "A", "version": "1.0.0",
                }})
            self.assertEqual(evaluated, ["src/Contracts/Contracts.A/Contracts.A.csproj"])

    def test_empty_base_compares_against_previous_commit(self):
        commands = []

        def fake(command, **kwargs):
            commands.append(command)
            if command[:2] == ["git", "rev-parse"]:
                return "b" * 40
            archive = io.BytesIO()
            tarfile.open(fileobj=archive, mode="w").close()
            return archive.getvalue()

        with patch.object(shared, "packages", return_value={}), \
                patch.object(shared.subprocess, "check_output", side_effect=fake):
            self.assertEqual(shared.select(ROOT, ""), [])
        self.assertEqual(commands[0][-1], "HEAD^^{commit}")
        self.assertEqual(commands[1], ["git", "archive", "b" * 40])

    def test_main_writes_selection_count_and_notice(self):
        package = {"id": "A", "version": "1.1.0", "project": "src/Contracts/Contracts.A/Contracts.A.csproj"}
        for selected, count in (([], 0), ([package], 1)):
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                output, github_output = root / "packages.json", root / "github_output"
                github_output.write_text("existing=1\n")
                stdout = io.StringIO()
                with patch.object(shared, "select", return_value=selected) as select, \
                        patch.object(shared.subprocess, "check_output", return_value=f"{root}\n"), \
                        patch("sys.argv", [
                            "shared_packages.py", "--base", "abc", "--output", str(output),
                        ]), \
                        patch.dict(os.environ, {"GITHUB_OUTPUT": str(github_output)}), \
                        patch("sys.stdout", stdout):
                    shared.main()
                select.assert_called_once_with(root, "abc")
                self.assertEqual(json.loads(output.read_text()), selected)
                self.assertEqual(github_output.read_text(), f"existing=1\ncount={count}\n")
                if count:
                    self.assertIn("A 1.1.0 (src/Contracts/Contracts.A/Contracts.A.csproj)", stdout.getvalue())
                    self.assertNotIn("::notice::", stdout.getvalue())
                else:
                    self.assertIn("::notice::No shared package version changes", stdout.getvalue())


if __name__ == "__main__":
    unittest.main()
