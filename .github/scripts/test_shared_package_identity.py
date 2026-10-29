import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import ci
from ci import GLOBAL_FILES, SERVICES, select, shared_projects

SCRIPT = Path(__file__).resolve().with_name("ci.py")
REPO = SCRIPT.parents[2]


def write(root, path, content=""):
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content)


class SelectionEdgeTests(unittest.TestCase):
    def test_every_global_file_rebuilds_shared_and_all_services(self):
        for path in sorted(GLOBAL_FILES):
            with self.subTest(path=path):
                result = select([path], "1.0.0", "1.0.0")
                self.assertTrue(result["shared"])
                self.assertTrue(result["apphost"])
                self.assertEqual(list(SERVICES), result["services"])

    def test_pipeline_inputs_rebuild_services_without_packing(self):
        for path in (".github/actions/build/action.yml", ".github/scripts/ci.py", ".config/dotnet-tools.json",
                     "src/Api/Program.cs", ".github/workflows/ci.yml"):
            with self.subTest(path=path):
                result = select([path], "1.0.0", "1.0.0")
                self.assertEqual(list(SERVICES), result["services"])
                self.assertTrue(result["apphost"])
                self.assertFalse(result["shared"])

    def test_unrelated_workflows_and_lookalike_paths_select_nothing(self):
        result = select([".github/workflows/release-drafter-labeler.yml", "src/BuildingBlocksExtra/A.cs",
                         "src/Modules/FlightOps/A.cs", "src/Services/Flight.HostTools/A.cs", "Directory.Build.props.bak"],
                        "1.0.0", "1.0.0")
        self.assertEqual({"services": [], "shared": False, "apphost": False, "packages": False}, result)

    def test_removed_version_does_not_publish(self):
        result = select(["Directory.Build.props"], "1.0.0", "")
        self.assertFalse(result["packages"])
        self.assertTrue(result["shared"])

    def test_services_keep_canonical_order(self):
        result = select(["src/Gateway/A.cs", "src/Services/Booking.Host/A.cs", "src/Modules/Identity/src/A.cs"],
                        "1.0.0", "1.0.0")
        self.assertEqual(["identity", "booking", "gateway"], result["services"])


class SharedProjectTests(unittest.TestCase):
    def test_shared_projects_ignore_folders_tests_and_duplicates(self):
        solution = '\n'.join((
            'Project("{2150E333}") = "BuildingBlocks", "BuildingBlocks", "{1}"',
            'Project("{FAE04EC0}") = "BuildingBlocks", "src\\BuildingBlocks\\BuildingBlocks.csproj", "{2}"',
            'Project("{FAE04EC0}") = "BuildingBlocks", "src/BuildingBlocks/BuildingBlocks.csproj", "{3}"',
            'Project("{FAE04EC0}") = "Tests", "src\\Contracts\\Tests\\Contracts.Tests.csproj", "{4}"',
            'Project("{FAE04EC0}") = "Extra", "src\\BuildingBlocksExtra\\Extra.csproj", "{5}"'))
        self.assertEqual(["src/BuildingBlocks/BuildingBlocks.csproj"], shared_projects(solution))

    def test_repository_solution_packs_only_buildable_shared_projects(self):
        projects = shared_projects((REPO / "booking-modular-monolith.sln").read_text())
        self.assertIn("src/BuildingBlocks/BuildingBlocks.csproj", projects)
        self.assertNotIn("src/BuildingBlocks/Mongo/MicroBootstrap.Persistence.Mongo.csproj", projects)
        for project in projects:
            self.assertTrue((REPO / project).is_file(), project)


def dotnet_available():
    try:
        return subprocess.run(["dotnet", "--version"], cwd=tempfile.gettempdir(),
                              capture_output=True).returncode == 0
    except OSError:
        return False


@unittest.skipUnless(dotnet_available(), "dotnet SDK not available")
class PackageIdentityTests(unittest.TestCase):
    """Evaluates the real Directory.Build.props against throwaway projects."""

    @classmethod
    def setUpClass(cls):
        cls.root = Path(tempfile.mkdtemp()).resolve()
        shutil.copy(REPO / "Directory.Build.props", cls.root)
        cls.version = ci.version((REPO / "Directory.Build.props").read_text())

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.root, ignore_errors=True)

    def properties(self, project):
        write(self.root, project, '<Project Sdk="Microsoft.NET.Sdk" />')
        output = subprocess.run(["dotnet", "msbuild", project, "-getProperty:PackageId",
                                 "-getProperty:PackageVersion", "-getProperty:RepositoryUrl"],
                                cwd=self.root, capture_output=True, text=True, check=True).stdout
        return json.loads(output)["Properties"]

    def test_shared_projects_get_booking_identity_and_shared_version(self):
        for project, package in (("src/BuildingBlocks/Core/BuildingBlocks.Core.csproj", "Booking.BuildingBlocks.Core"),
                                 ("src/Contracts/Contracts.Grpc/Contracts.Grpc.csproj", "Booking.Contracts.Grpc")):
            with self.subTest(project=project):
                properties = self.properties(project)
                self.assertEqual(package, properties["PackageId"])
                self.assertEqual(self.version, properties["PackageVersion"])
                self.assertEqual("https://github.com/COG-GTM/booking-modular-monolith", properties["RepositoryUrl"])

    def test_service_and_lookalike_projects_keep_default_identity(self):
        for project, package in (("src/Modules/Flight/src/Flight.csproj", "Flight"),
                                 ("src/BuildingBlocksExtra/Extra.csproj", "Extra")):
            with self.subTest(project=project):
                properties = self.properties(project)
                self.assertEqual(package, properties["PackageId"])
                self.assertEqual("", properties["RepositoryUrl"])


if __name__ == "__main__":
    unittest.main()
