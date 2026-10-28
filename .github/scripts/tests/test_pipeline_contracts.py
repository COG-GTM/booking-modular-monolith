import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import yaml

ROOT = Path(__file__).resolve().parents[3]
SERVICES = ("gateway", "identity", "flight", "passenger", "booking")
PROTECTED_BRANCHES = ("main", "devin/ab-488-v2")


def load(path):
    return yaml.load((ROOT / path).read_text(encoding="utf-8-sig"), Loader=yaml.BaseLoader)


def step(steps, name):
    return next(item for item in steps if item.get("name") == name)


def fake_command(directory, name, exit_code=0):
    log = directory / f"{name}.log"
    command = directory / name
    command.write_text(
        "#!/bin/sh\n"
        f'for arg in "$@"; do printf "%s\\n" "$arg" >> "{log}"; done\n'
        f'printf "%s\\n" "--" >> "{log}"\n'
        f"exit {exit_code}\n"
    )
    command.chmod(0o755)
    return log


def calls(log):
    if not log.exists():
        return []
    return [call.strip("\n").split("\n") for call in log.read_text().split("--\n") if call]


spec = importlib.util.spec_from_file_location("shared_packages", ROOT / ".github/scripts/shared_packages.py")
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)


class ServiceWorkflowContractTests(unittest.TestCase):
    def test_service_entry_workflows_grant_only_the_permissions_the_pipeline_needs(self):
        for service in SERVICES:
            workflow = load(f".github/workflows/service-{service}.yml")
            self.assertEqual(workflow["permissions"], {"contents": "read"}, service)
            self.assertEqual(
                workflow["jobs"]["pipeline"]["permissions"],
                {"contents": "read", "packages": "write"},
                service,
            )

    def test_service_entry_workflows_trigger_on_protected_branches_and_cancel_only_prs(self):
        for service in SERVICES:
            workflow = load(f".github/workflows/service-{service}.yml")
            for event in ("push", "pull_request"):
                self.assertEqual(tuple(workflow["on"][event]["branches"]), PROTECTED_BRANCHES, service)
            self.assertIn("workflow_dispatch", workflow["on"])
            concurrency = workflow["concurrency"]
            self.assertTrue(concurrency["group"].startswith(f"{service}-"), service)
            self.assertEqual(concurrency["cancel-in-progress"], "${{ github.event_name == 'pull_request' }}")

    def test_verify_and_publish_use_the_same_immutable_ghcr_image(self):
        workflow = load(".github/workflows/service-pipeline.yml")
        verify = step(workflow["jobs"]["verify"]["steps"], "Verify container build without publishing")
        publish = workflow["jobs"]["publish"]["steps"][-1]
        for build in (verify, publish):
            self.assertEqual(build["uses"], "./.github/actions/docker-build-publish")
            self.assertEqual(build["with"]["registry"], "ghcr.io")
            self.assertEqual(build["with"]["image-name"], "cog-gtm/booking-${{ inputs.service }}")
            self.assertEqual(build["with"]["tag-name"], "sha-${{ github.sha }}")
            self.assertEqual(build["with"]["dockerfile-path"], "${{ inputs.dockerfile }}")
        self.assertEqual(verify["with"]["push"], "false")
        self.assertNotIn("registry-password", verify["with"])
        self.assertEqual(publish["with"]["push"], "true")
        self.assertEqual(publish["with"]["registry-password"], "${{ github.token }}")
        self.assertEqual(workflow["jobs"]["publish"]["needs"], "verify")
        self.assertIn("needs.verify.outputs.available == 'true'", workflow["jobs"]["publish"]["if"])

    def test_build_and_test_steps_are_skipped_until_host_scaffolding_exists(self):
        steps = load(".github/workflows/service-pipeline.yml")["jobs"]["verify"]["steps"]
        for name in ("Find service tests", "Build and test service", "Verify container build without publishing"):
            self.assertEqual(step(steps, name)["if"], "steps.host.outputs.available == 'true'", name)
        build = step(steps, "Build and test service")
        self.assertEqual(build["with"]["project-path"], "${{ inputs.project }}")
        self.assertEqual(build["with"]["tests-path"], "${{ steps.tests.outputs.path }}")


class CompositeActionContractTests(unittest.TestCase):
    def run_step(self, action, name, env, command, exit_code=0):
        script = step(load(f".github/actions/{action}/action.yml")["runs"]["steps"], name)["run"]
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        directory = Path(temporary.name)
        log = fake_command(directory, command, exit_code)
        summary = directory / "summary"
        run = subprocess.run(["bash", "-e", "-c", script], cwd=directory, env=dict(
            os.environ, PATH=f"{directory}:{os.environ['PATH']}", GITHUB_STEP_SUMMARY=str(summary), **env
        ), capture_output=True, text=True)
        return run, calls(log), summary

    def test_build_restore_failure_fails_the_step(self):
        run, _, _ = self.run_step("build", "Restore NuGet packages", {"PROJECT": "Host.csproj"}, "dotnet", 3)
        self.assertNotEqual(run.returncode, 0)

    def test_build_test_composite_restores_before_testing(self):
        steps = load(".github/actions/build-test/action.yml")["runs"]["steps"]
        self.assertEqual(step(steps, "Call Composite Action test")["with"]["no-restore"], "false")

    def test_docker_build_tags_registry_qualified_image_with_oci_labels(self):
        image = "ghcr.io/cog-gtm/booking-flight:sha-" + "a" * 40
        run, docker, _ = self.run_step("docker-build-publish", "Build image", {
            "IMAGE": image,
            "DOCKERFILE": "src/Services/Flight.Host/Dockerfile",
            "GITHUB_SERVER_URL": "https://github.com",
            "GITHUB_REPOSITORY": "COG-GTM/booking-modular-monolith",
            "GITHUB_SHA": "a" * 40,
        }, "docker")
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertEqual(docker, [[
            "build",
            "--label", "org.opencontainers.image.source=https://github.com/COG-GTM/booking-modular-monolith",
            "--label", f"org.opencontainers.image.revision={'a' * 40}",
            "--tag", image, "--file", "src/Services/Flight.Host/Dockerfile", ".",
        ]])

    def test_docker_publish_failure_fails_the_step(self):
        run, _, summary = self.run_step("docker-build-publish", "Publish image", {"IMAGE": "x:y"}, "docker", 1)
        self.assertNotEqual(run.returncode, 0)
        self.assertFalse(summary.exists())

    def test_docker_build_and_publish_reference_the_same_image(self):
        action = load(".github/actions/docker-build-publish/action.yml")
        steps = action["runs"]["steps"]
        image = "${{ inputs.registry }}/${{ inputs.image-name }}:${{ inputs.tag-name }}"
        self.assertEqual(step(steps, "Build image")["env"]["IMAGE"], image)
        self.assertEqual(step(steps, "Publish image")["env"]["IMAGE"], image)
        self.assertEqual(step(steps, "Login to registry")["with"]["registry"], "${{ inputs.registry }}")
        self.assertNotIn("if", step(steps, "Build image"))
        for name in ("registry-username", "registry-password"):
            self.assertEqual(action["inputs"][name]["required"], "false")


class SharedPackageWorkflowTests(unittest.TestCase):
    def test_pack_compares_against_full_history_baseline(self):
        workflow = load(".github/workflows/shared-packages.yml")
        steps = workflow["jobs"]["pack"]["steps"]
        self.assertEqual(steps[0]["with"]["fetch-depth"], "0")
        select = step(steps, "Select new or version-bumped packages")
        self.assertEqual(
            select["env"]["BASE"],
            "${{ inputs.base || github.event.pull_request.base.sha || github.event.before }}",
        )
        self.assertIn('--base "$BASE"', select["run"])
        self.assertEqual(workflow["jobs"]["pack"]["outputs"]["count"], "${{ steps.select.outputs.count }}")

    def test_pack_upload_and_publish_are_skipped_when_nothing_changed(self):
        workflow = load(".github/workflows/shared-packages.yml")
        steps = workflow["jobs"]["pack"]["steps"]
        self.assertEqual(step(steps, "Pack selected projects")["if"], "steps.select.outputs.count != '0'")
        upload = next(item for item in steps if item.get("uses", "").startswith("actions/upload-artifact"))
        self.assertEqual(upload["if"], "steps.select.outputs.count != '0'")
        self.assertEqual(upload["with"]["if-no-files-found"], "error")
        self.assertIn("needs.pack.outputs.count != '0'", workflow["jobs"]["publish"]["if"])

    def test_publish_pushes_to_org_github_packages_feed_without_overwriting(self):
        run = step(load(".github/workflows/shared-packages.yml")["jobs"]["publish"]["steps"],
                   "Publish versioned NuGet artifacts")["run"]
        self.assertIn("--source https://nuget.pkg.github.com/COG-GTM/index.json", run)
        self.assertIn("--skip-duplicate", run)


class SharedPackageSelectionTests(unittest.TestCase):
    def test_unknown_base_fails_instead_of_publishing_everything(self):
        with patch.object(shared, "packages", return_value={"A": {"id": "A", "version": "1", "project": "a"}}):
            with self.assertRaises(subprocess.CalledProcessError):
                shared.select(ROOT, "f" * 39 + "e")


class SharedPackageCliTests(unittest.TestCase):
    def test_runs_outside_actions_without_github_output(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "packages.json"
            stdout = io.StringIO()
            env = {key: value for key, value in os.environ.items() if key != "GITHUB_OUTPUT"}
            with patch.object(sys, "argv", ["shared_packages.py", "--output", str(output)]), \
                    patch.dict(os.environ, env, clear=True), \
                    patch.object(shared, "select", return_value=[]) as select, \
                    contextlib.redirect_stdout(stdout):
                shared.main()
            self.assertEqual(select.call_args.args[1], "")
            self.assertEqual(json.loads(output.read_text()), [])
            self.assertFalse((Path(directory) / "github_output").exists())
            self.assertIn("::notice::No shared package version changes; nothing to publish.", stdout.getvalue())


if __name__ == "__main__":
    unittest.main()
