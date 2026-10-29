import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import tempfile
import unittest

import ci

SCRIPT = Path(__file__).resolve().with_name("ci.py")
REPO = SCRIPT.parents[2]
PACK_SCRIPT = REPO / "scripts/pack-building-blocks.sh"
OVERRIDE_VERSION = "9.8.7-ci.42"


def dotnet_available():
    try:
        return subprocess.run(["dotnet", "--version"], cwd=tempfile.gettempdir(),
                              capture_output=True).returncode == 0
    except OSError:
        return False


def find_bash():
    executable = shutil.which("bash")
    if not executable:
        return None
    output = subprocess.run([executable, "--version"], capture_output=True, text=True).stdout
    match = re.search(r"GNU bash, version (\d+)", output)
    return executable if match and int(match.group(1)) >= 4 else None


BASH = find_bash()


@unittest.skipUnless(dotnet_available(), "dotnet SDK not available")
class RealSharedProjectMetadataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.projects = [project for project in ci.shared_projects(
            (REPO / "booking-modular-monolith.sln").read_text()
        ) if project.startswith("src/BuildingBlocks/")]
        cls.version = ci.version((REPO / "Directory.Build.props").read_text())

    def properties(self, project, *options):
        output = subprocess.run(
            ["dotnet", "msbuild", project, *options],
            cwd=REPO, capture_output=True, text=True, check=True,
        ).stdout
        return json.loads(output)["Properties"]

    def test_real_buildingblocks_projects_have_expected_metadata(self):
        expected_projects = {
            path.relative_to(REPO).as_posix()
            for path in (REPO / "src/BuildingBlocks").rglob("BuildingBlocks*.csproj")
            if "tests" not in path.relative_to(REPO).parts
        }
        self.assertEqual(expected_projects, set(self.projects))

        for project in self.projects:
            with self.subTest(project=project):
                properties = self.properties(
                    project,
                    "-getProperty:PackageId",
                    "-getProperty:PackageVersion",
                    "-getProperty:IsPackable",
                )
                self.assertEqual(
                    f"BookingModularMonolith.{Path(project).stem}", properties["PackageId"]
                )
                self.assertEqual(self.version, properties["PackageVersion"])
                self.assertNotEqual("false", properties["IsPackable"].lower())

    def test_shared_package_version_override_updates_buildingblocks_metadata(self):
        projects = (
            "src/BuildingBlocks/BuildingBlocks.csproj",
            "src/BuildingBlocks/Core/BuildingBlocks.Core.csproj",
            "src/BuildingBlocks/Web/BuildingBlocks.Web.csproj",
        )
        for project in projects:
            with self.subTest(project=project):
                properties = self.properties(
                    project,
                    "-getProperty:PackageVersion",
                    "-getProperty:BuildingBlocksVersion",
                    f"-p:SharedPackageVersion={OVERRIDE_VERSION}",
                )
                self.assertEqual(OVERRIDE_VERSION, properties["PackageVersion"])
                self.assertEqual(OVERRIDE_VERSION, properties["BuildingBlocksVersion"])

    def test_aggregate_metadata_matches_ci_package_metadata_check(self):
        output = subprocess.run(
            [
                "dotnet", "msbuild", "src/BuildingBlocks/BuildingBlocks.csproj",
                "-getProperty:PackageId,PackageVersion",
            ],
            cwd=REPO, capture_output=True, check=True,
        ).stdout
        properties = json.loads(output.decode("utf-8-sig"))["Properties"]
        self.assertEqual(
            {
                "PackageId": "BookingModularMonolith.BuildingBlocks",
                "PackageVersion": self.version,
            },
            properties,
        )


@unittest.skipUnless(BASH, "bash >= 4 not available")
class PackScriptVersionTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)
        self.bin_dir = self.root / "bin"
        self.bin_dir.mkdir()
        self.log = self.root / "dotnet.log"
        dotnet = self.bin_dir / "dotnet"
        dotnet.write_text(
            f"#!{BASH}\nprintf '%q ' \"$@\" >> \"$DOTNET_LOG\"\nprintf '\\n' >> \"$DOTNET_LOG\"\n"
        )
        dotnet.chmod(0o755)

    def run_pack_script(self, *args):
        environment = os.environ.copy()
        environment["PATH"] = os.pathsep.join((str(self.bin_dir), environment["PATH"]))
        environment["DOTNET_LOG"] = str(self.log)
        subprocess.run(
            [BASH, str(PACK_SCRIPT), *args],
            cwd=REPO, env=environment, capture_output=True, text=True, check=True,
        )
        return [shlex.split(line) for line in self.log.read_text().splitlines()]

    def assert_one_invocation_per_project(self, invocations):
        expected_projects = sorted(
            str(path) for path in (REPO / "src/BuildingBlocks").rglob("BuildingBlocks*.csproj")
            if "tests" not in path.relative_to(REPO).parts
        )
        buildingblocks = [invocation for invocation in invocations
                          if invocation[1].startswith(str(REPO / "src/BuildingBlocks"))]
        self.assertEqual(len(expected_projects) + 1, len(invocations))
        self.assertCountEqual(expected_projects, [invocation[1] for invocation in buildingblocks])
        self.assertEqual(str(REPO / "src/Contracts/Contracts.Messages/Contracts.Messages.csproj"),
                         invocations[0][1])
        return buildingblocks

    def test_version_argument_uses_shared_package_version_property(self):
        feed = self.root / "feed"
        invocations = self.run_pack_script("2.0.0-ci.7", str(feed))

        buildingblocks = self.assert_one_invocation_per_project(invocations)
        for invocation in buildingblocks:
            with self.subTest(project=invocation[1]):
                self.assertIn("-p:SharedPackageVersion=2.0.0-ci.7", invocation)
                self.assertFalse(any("-p:Version=" in argument for argument in invocation))

    def test_omitted_version_argument_does_not_pass_version_override(self):
        default_feed = REPO / "artifacts/nuget"
        feed_existed = default_feed.exists()
        artifacts_existed = default_feed.parent.exists()
        try:
            invocations = self.run_pack_script()
            self.assert_one_invocation_per_project(invocations)
            for invocation in invocations:
                with self.subTest(project=invocation[1]):
                    self.assertFalse(any(
                        argument.startswith("-p:SharedPackageVersion=")
                        for argument in invocation
                    ))
        finally:
            if not feed_existed and default_feed.is_dir() and not any(default_feed.iterdir()):
                default_feed.rmdir()
            if not artifacts_existed and default_feed.parent.is_dir() and not any(default_feed.parent.iterdir()):
                default_feed.parent.rmdir()


if __name__ == "__main__":
    unittest.main()
