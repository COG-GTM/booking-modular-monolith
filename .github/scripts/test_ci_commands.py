import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import call, patch

import ci


SCRIPT = Path(ci.__file__).resolve()


class CommandTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name).resolve()
        previous = Path.cwd()
        os.chdir(self.root)
        self.addCleanup(os.chdir, previous)

    def write(self, path, content=""):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)

    def invoke(self, *args):
        output = io.StringIO()
        with patch.object(sys, "argv", [str(SCRIPT), *args]), contextlib.redirect_stdout(output):
            ci.main()
        return dict(line.split("=", 1) for line in output.getvalue().splitlines())

    def select(self, paths, old="1.0.0", new="1.0.0", *args):
        def git(*command):
            if command[0] in ("diff", "ls-tree"):
                return "\n".join(paths)
            self.assertEqual("show", command[0])
            value = old if command[1].startswith("before:") else new
            return f"<Project><SharedPackageVersion>{value}</SharedPackageVersion></Project>"

        with patch.object(ci, "git", side_effect=git) as mocked:
            result = self.invoke("select", *args)
        return {key: json.loads(value) for key, value in result.items()}, mocked.call_args_list

    def test_select_uses_requested_revisions_and_keeps_deleted_paths(self):
        result, calls = self.select(
            ["src/Modules/Flight/src/Deleted.cs"], "1.0.0", "1.0.0",
            "--base", "before", "--head", "after",
        )
        self.assertEqual({"services": ["flight"], "shared": False, "apphost": False, "packages": False}, result)
        self.assertEqual([
            call("diff", "--name-only", "--no-renames", "before", "after"),
            call("show", "before:Directory.Build.props"),
            call("show", "after:Directory.Build.props"),
        ], calls)

    def test_initial_push_uses_tree_instead_of_an_invalid_base(self):
        for args in ((), ("--base", "0" * 40)):
            with self.subTest(args=args):
                result, calls = self.select(["Directory.Build.props"], "", "1.0.0", *args)
                self.assertEqual({"services": list(ci.MODULES), "shared": True,
                                  "apphost": True, "packages": True}, result)
                self.assertEqual([
                    call("ls-tree", "-r", "--name-only", "HEAD"),
                    call("show", "HEAD:Directory.Build.props"),
                ], calls)

    def test_version_change_selects_all_even_when_path_list_is_empty(self):
        result, _ = self.select([], "1.0.0", "1.0.1", "--base", "before")
        self.assertEqual({"services": list(ci.MODULES), "shared": True, "apphost": True, "packages": True}, result)

    def test_docs_only_does_not_pack_or_publish(self):
        result, _ = self.select(["docs/runbooks/service-ci.md"], "1.0.0", "1.0.0", "--base", "before")
        self.assertEqual({"services": [], "shared": False, "apphost": False, "packages": False}, result)

    def test_dispatch_force_accepts_workflow_boolean_strings(self):
        for force in ("true", "false"):
            with self.subTest(force=force):
                result, _ = self.select([], "1.0.0", "1.0.0", "--base", "before", "--force", force)
                self.assertEqual({
                    "services": list(ci.MODULES) if force == "true" else [],
                    "shared": force == "true", "apphost": force == "true", "packages": False,
                }, result)

    def test_gateway_joins_matrix_only_after_its_host_is_available(self):
        paths = ["src/Gateway/Program.cs"]
        result, _ = self.select(paths, "1.0.0", "1.0.0", "--base", "before")
        self.assertEqual([], result["services"])
        self.write("src/Gateway/Gateway.csproj")
        self.write("src/Gateway/Dockerfile")
        result, _ = self.select(paths, "1.0.0", "1.0.0", "--base", "before")
        self.assertEqual({"services": ["gateway"], "shared": False, "apphost": False, "packages": False}, result)

    def test_selection_fails_when_selected_host_has_no_dockerfile(self):
        self.write("src/Services/Flight.Host/Flight.Host.csproj")
        with self.assertRaises(FileNotFoundError):
            self.select(["src/Modules/Flight/src/Feature.cs"], "1.0.0", "1.0.0", "--base", "before")

    def test_git_failures_are_not_reported_as_successful_empty_selection(self):
        error = subprocess.CalledProcessError(128, ["git", "diff"])
        with patch.object(ci, "git", side_effect=error), self.assertRaises(subprocess.CalledProcessError):
            self.invoke("select", "--base", "missing")

    def test_layout_entrypoint_preserves_paths_with_spaces(self):
        self.write("src/Services/Flight.Host/Flight Host.csproj")
        self.write("src/Services/Flight.Host/Dockerfile")
        self.write("src/Modules/Flight/tests/Unit.Test/Unit.Test.csproj")
        completed = subprocess.run(
            [sys.executable, str(SCRIPT), "layout", "flight"],
            check=True, capture_output=True, text=True,
        )
        self.assertEqual({
            "project": "src/Services/Flight.Host/Flight Host.csproj",
            "tests": "src/Modules/Flight/tests",
            "dockerfile": "src/Services/Flight.Host/Dockerfile",
        }, dict(line.split("=", 1) for line in completed.stdout.splitlines()))

    def test_module_layout_retains_tests_during_host_migration(self):
        for module in ci.MODULES:
            with self.subTest(module=module):
                name = module.title()
                self.write(f"src/Modules/{name}/tests/Unit.Test/Unit.Test.csproj")
                self.assertEqual({
                    "project": f"src/Modules/{name}/src/{name}.csproj",
                    "tests": f"src/Modules/{name}/tests", "dockerfile": "",
                }, ci.layout(module))

    def test_gateway_layout_uses_its_own_tests(self):
        self.write("src/Gateway/Gateway.csproj")
        self.write("src/Gateway/Dockerfile")
        self.write("src/Gateway/tests/Unit.Test/Unit.Test.csproj")
        self.assertEqual({
            "project": "src/Gateway/Gateway.csproj", "tests": "src/Gateway/tests",
            "dockerfile": "src/Gateway/Dockerfile",
        }, self.invoke("layout", "gateway"))

    def test_absent_gateway_layout_exits_unsuccessfully(self):
        completed = subprocess.run(
            [sys.executable, str(SCRIPT), "layout", "gateway"],
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual("", completed.stdout)
        self.assertIn("Gateway has not been added yet", completed.stderr)

    def test_pack_discovers_only_active_shared_projects_and_resolves_output(self):
        projects = [
            "src/Contracts/Contracts.Messages/Contracts.Messages.csproj",
            "src\\BuildingBlocks\\BuildingBlocks.csproj",
            "src/BuildingBlocks/tests/Unit.Test/Unit.Test.csproj",
            "src/Modules/Flight/src/Flight.csproj",
            "src/BuildingBlocks/BuildingBlocks.csproj",
        ]
        self.write("booking-modular-monolith.sln", "\n".join(
            f'Project("id") = "name", "{project}", "id"' for project in projects
        ))
        self.write("src/BuildingBlocks/Mongo/Abandoned.csproj")
        with patch.object(ci.subprocess, "run") as run:
            self.assertEqual({}, self.invoke("pack", "--output", "artifacts/shared packages"))
        output = self.root / "artifacts/shared packages"
        self.assertTrue(output.is_dir())
        self.assertEqual([
            call(["dotnet", "pack", project, "-c", "Release", "-o", str(output),
                  "-p:ContinuousIntegrationBuild=true"], check=True)
            for project in ("src/BuildingBlocks/BuildingBlocks.csproj", projects[0])
        ], run.call_args_list)

    def test_pack_uses_default_output_directory(self):
        self.write("booking-modular-monolith.sln", '"src/BuildingBlocks/BuildingBlocks.csproj"')
        with patch.object(ci.subprocess, "run") as run:
            self.invoke("pack")
        self.assertEqual(str(self.root / "artifacts/packages"), run.call_args.args[0][6])
        self.assertTrue((self.root / "artifacts/packages").is_dir())

    def test_pack_rejects_solution_without_shared_projects(self):
        self.write("booking-modular-monolith.sln", '"src/Modules/Flight/src/Flight.csproj"')
        with patch.object(ci.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "No shared projects found"):
                self.invoke("pack")
        run.assert_not_called()

    def test_pack_stops_and_propagates_dotnet_failure(self):
        self.write("booking-modular-monolith.sln", '\n'.join([
            '"src/BuildingBlocks/BuildingBlocks.csproj"',
            '"src/Contracts/Contracts.Messages/Contracts.Messages.csproj"',
        ]))
        error = subprocess.CalledProcessError(1, ["dotnet", "pack"])
        with patch.object(ci.subprocess, "run", side_effect=error) as run:
            with self.assertRaises(subprocess.CalledProcessError):
                self.invoke("pack")
        run.assert_called_once()


if __name__ == "__main__":
    unittest.main()
