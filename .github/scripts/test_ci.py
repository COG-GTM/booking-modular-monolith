import tempfile
from pathlib import Path
import unittest

from ci import MODULES, SERVICES, layout, select, shared_projects, version


class SelectionTests(unittest.TestCase):
    def choose(self, *paths):
        return select(paths, "1.0.0", "1.0.0")

    def test_module_and_host_changes_select_only_their_service(self):
        for module in MODULES:
            for path in (f"src/Modules/{module.title()}/src/Feature.cs",
                         f"src/Services/{module.title()}.Host/Dockerfile",
                         f"src/Modules/{module.title()}/tests/Host.Test/Test.cs"):
                with self.subTest(path=path):
                    self.assertEqual([module], self.choose(path)["services"])

    def test_gateway_is_independent(self):
        self.assertEqual(["gateway"], self.choose("src/Gateway/Program.cs")["services"])

    def test_shared_changes_rebuild_all_without_republishing_same_version(self):
        for path in ("src/Contracts/Flight.proto", "src/BuildingBlocks/Core/Event.cs",
                     "Directory.Build.props", "src/Aspire/src/ServiceDefaults/Extensions.cs"):
            result = self.choose(path)
            self.assertEqual(list(SERVICES), result["services"])
            self.assertFalse(result["packages"])

    def test_version_bump_publishes_and_rebuilds_all(self):
        result = select(["Directory.Build.props"], "1.0.0", "1.0.1")
        self.assertTrue(result["packages"])
        self.assertTrue(result["shared"])
        self.assertEqual(list(SERVICES), result["services"])

    def test_initial_version_is_published(self):
        self.assertTrue(select([], "", "1.0.0")["packages"])

    def test_docs_and_deployment_changes_do_not_build_services(self):
        self.assertEqual([], self.choose("README.md", "docs/runbooks/ci.md",
                                        "deployments/k8s/values.yaml")["services"])

    def test_pipeline_changes_rebuild_all(self):
        self.assertEqual(list(SERVICES), self.choose(".github/workflows/service.yml")["services"])

    def test_multiple_services_union(self):
        self.assertEqual(["identity", "flight"], self.choose("src/Modules/Flight/src/A.cs",
                         "src/Modules/Identity/src/B.cs")["services"])

    def test_force_rebuilds_without_republishing_unchanged_packages(self):
        result = select([], "1.0.0", "1.0.0", force=True)
        self.assertEqual(list(SERVICES), result["services"])
        self.assertFalse(result["packages"])

    def test_version_reader(self):
        self.assertEqual("2.0.1", version("<Project><PropertyGroup>"
                         "<SharedPackageVersion>2.0.1</SharedPackageVersion></PropertyGroup></Project>"))


class LayoutTests(unittest.TestCase):
    def test_migration_and_host_layouts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.assertEqual("", layout("flight", root)["dockerfile"])
            self.assertIsNone(layout("gateway", root))
            host = root / "src/Services/Flight.Host"
            host.mkdir(parents=True)
            (host / "Flight.Host.csproj").touch()
            with self.assertRaises(FileNotFoundError):
                layout("flight", root)
            (host / "Dockerfile").touch()
            self.assertEqual("src/Services/Flight.Host/Flight.Host.csproj", layout("flight", root)["project"])
            self.assertEqual("src/Services/Flight.Host/Dockerfile", layout("flight", root)["dockerfile"])

    def test_only_active_shared_projects_are_packaged(self):
        solution = '\n'.join(f'Project("id") = "name", "{p}", "id"' for p in (
            "src\\BuildingBlocks\\BuildingBlocks.csproj",
            "src\\BuildingBlocks\\Core\\BuildingBlocks.Core.csproj",
            "src\\BuildingBlocks\\tests\\Unit.Test\\Unit.Test.csproj",
            "src\\Contracts\\Contracts.Messages\\Contracts.Messages.csproj",
            "src\\Modules\\Flight\\src\\Flight.csproj"))
        self.assertEqual(["src/BuildingBlocks/BuildingBlocks.csproj",
                          "src/BuildingBlocks/Core/BuildingBlocks.Core.csproj",
                          "src/Contracts/Contracts.Messages/Contracts.Messages.csproj"], shared_projects(solution))


if __name__ == "__main__":
    unittest.main()
