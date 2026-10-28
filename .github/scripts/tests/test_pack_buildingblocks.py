import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "scripts/pack-buildingblocks.sh"
PROJECTS = (
    "src/Contracts/Contracts.Messages/Contracts.Messages.csproj",
    "src/Contracts/Contracts.Grpc/Contracts.Grpc.csproj",
    "src/BuildingBlocks/Core/BuildingBlocks.Core.csproj",
    "src/BuildingBlocks/Web/BuildingBlocks.Web.csproj",
    "src/BuildingBlocks/BuildingBlocks.csproj",
)
TEST_PROJECT = "src/Contracts/tests/Contracts.Messages.Test/Contracts.Messages.Test.csproj"


class PackBuildingBlocksTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(SCRIPT.is_file(), f"Expected pack script at {SCRIPT}")
        self.tempdir = tempfile.TemporaryDirectory()
        self.addCleanup(self.tempdir.cleanup)
        self.root = Path(self.tempdir.name).resolve()
        (self.root / "scripts").mkdir()
        shutil.copy2(SCRIPT, self.root / "scripts/pack-buildingblocks.sh")

        for relative_path in (*PROJECTS, TEST_PROJECT):
            project = self.root / relative_path
            project.parent.mkdir(parents=True, exist_ok=True)
            project.touch()

        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        self.log = self.root / "dotnet.log"
        dotnet = bin_dir / "dotnet"
        dotnet.write_text(
            "#!/usr/bin/env python3\n"
            "import json\n"
            "import os\n"
            "import sys\n"
            "with open(os.environ['PACK_LOG'], 'a', encoding='utf-8') as log:\n"
            "    log.write(json.dumps(sys.argv[1:]) + '\\n')\n"
            "raise SystemExit(int(os.environ.get('DOTNET_EXIT', '0')))\n",
            encoding="utf-8",
        )
        dotnet.chmod(0o755)
        self.env = dict(
            os.environ,
            PATH=f"{bin_dir}{os.pathsep}{os.environ['PATH']}",
            PACK_LOG=str(self.log),
            DOTNET_EXIT="0",
        )

    def run_script(self, *args):
        return subprocess.run(
            ["bash", "scripts/pack-buildingblocks.sh", *args],
            cwd=self.root,
            env=self.env,
            capture_output=True,
            text=True,
        )

    def invocations(self):
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text(encoding="utf-8").splitlines()]

    def expected_invocation(self, project, version=None):
        invocation = [
            "pack",
            str(self.root / project),
            "-c",
            "Release",
        ]
        if version is not None:
            invocation.append(f"-p:Version={version}")
        invocation.extend(["-o", str(self.root / "artifacts/packages"), "--nologo"])
        return invocation

    def assert_project_set(self, invocations):
        project_paths = {invocation[1] for invocation in invocations}
        self.assertEqual(project_paths, {str(self.root / project) for project in PROJECTS})
        self.assertNotIn(str(self.root / TEST_PROJECT), project_paths)

    def test_default_packs_every_project_at_its_declared_version(self):
        run = self.run_script()

        self.assertEqual(run.returncode, 0, run.stderr)
        invocations = self.invocations()
        self.assert_project_set(invocations)
        self.assertEqual(
            invocations,
            [self.expected_invocation(project) for project in
             (PROJECTS[1], PROJECTS[0], PROJECTS[2], PROJECTS[3], PROJECTS[4])],
        )
        self.assertFalse(any(
            argument.startswith("-p:Version")
            for invocation in invocations
            for argument in invocation
        ))
        contract_indices = [
            index for index, invocation in enumerate(invocations)
            if "/src/Contracts/" in invocation[1]
        ]
        building_block_indices = [
            index for index, invocation in enumerate(invocations)
            if "/src/BuildingBlocks/" in invocation[1]
        ]
        self.assertLess(max(contract_indices), min(building_block_indices))
        self.assertTrue((self.root / "artifacts/packages").is_dir())
        self.assertIn("version declared per project", run.stdout)

    def test_explicit_version_overrides_every_project(self):
        run = self.run_script("2.3.4")

        self.assertEqual(run.returncode, 0, run.stderr)
        invocations = self.invocations()
        self.assert_project_set(invocations)
        self.assertEqual(
            invocations,
            [self.expected_invocation(project, "2.3.4") for project in
             (PROJECTS[1], PROJECTS[0], PROJECTS[2], PROJECTS[3], PROJECTS[4])],
        )
        for invocation in invocations:
            self.assertEqual(
                sum(argument == "-p:Version=2.3.4" for argument in invocation),
                1,
            )
        self.assertIn("(version 2.3.4)", run.stdout)

    def test_dotnet_failure_stops_the_script(self):
        self.env["DOTNET_EXIT"] = "1"

        run = self.run_script()

        self.assertNotEqual(run.returncode, 0)
        self.assertEqual(len(self.invocations()), 1)


if __name__ == "__main__":
    unittest.main()
