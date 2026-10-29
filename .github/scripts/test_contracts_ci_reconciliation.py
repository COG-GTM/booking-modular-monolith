import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

import ci
from test_split_shared_package_metadata import dotnet_available, find_bash


REPO = Path(ci.__file__).resolve().parents[2]
SCRIPT = Path(ci.__file__).resolve()
CONTRACTS_PROJECT = "src/Contracts/Contracts.Messages/Contracts.Messages.csproj"
VERSIONS = "src/Contracts/Versions.props"
BASH = find_bash()


class ContractsSelectionHistoryTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name).resolve()
        self.git("init", "-q")
        self.write("Directory.Build.props",
                   "<Project><PropertyGroup><SharedPackageVersion>1.1.0</SharedPackageVersion>"
                   "</PropertyGroup></Project>")

    def git(self, *args):
        return subprocess.check_output(["git", *args], cwd=self.root, text=True).strip()

    def write(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)

    def contracts_version(self, version):
        self.write(VERSIONS, "<Project><PropertyGroup>"
                   f"<ContractsMessagesVersion>{version}</ContractsMessagesVersion>"
                   "</PropertyGroup></Project>")

    def commit(self):
        self.git("add", "--all")
        environment = os.environ.copy()
        environment.update(GIT_AUTHOR_NAME="CI fixture", GIT_AUTHOR_EMAIL="ci@example.com",
                           GIT_COMMITTER_NAME="CI fixture", GIT_COMMITTER_EMAIL="ci@example.com")
        subprocess.run(["git", "commit", "-qm", "test fixture"], cwd=self.root,
                       env=environment, capture_output=True, text=True, check=True)
        return self.git("rev-parse", "HEAD")

    def select(self, base="", head="HEAD"):
        completed = subprocess.run(
            [sys.executable, str(SCRIPT), "select", "--base", base, "--head", head],
            cwd=self.root, capture_output=True, text=True, check=True,
        )
        return {key: json.loads(value) for key, value in
                (line.split("=", 1) for line in completed.stdout.splitlines())}

    def assert_shared_selection(self, result, publish):
        self.assertEqual({"services": list(ci.MODULES), "shared": True,
                          "apphost": True, "packages": publish}, result)

    def test_adding_contracts_version_to_existing_branch_enables_publishing(self):
        base = self.commit()
        self.contracts_version("1.0.0")
        head = self.commit()
        self.assert_shared_selection(self.select(base, head), True)

    def test_initial_contracts_version_publishes_without_a_shared_version(self):
        self.write("Directory.Build.props", "<Project />")
        self.contracts_version("1.0.0")
        self.commit()
        for base in ("", "0" * 40):
            with self.subTest(base=base):
                self.assert_shared_selection(self.select(base), True)

    def test_version_file_comment_edit_does_not_publish(self):
        self.contracts_version("1.0.0")
        base = self.commit()
        with (self.root / VERSIONS).open("a") as version_file:
            version_file.write("\n<!-- release notes only -->\n")
        self.commit()
        self.assert_shared_selection(self.select(base), False)

    def test_removed_version_file_does_not_publish(self):
        self.contracts_version("1.0.0")
        base = self.commit()
        (self.root / VERSIONS).unlink()
        self.commit()
        self.assert_shared_selection(self.select(base), False)

    def test_empty_contracts_version_does_not_publish(self):
        self.contracts_version("1.0.0")
        base = self.commit()
        self.contracts_version("")
        self.commit()
        self.assert_shared_selection(self.select(base), False)

    def test_contracts_source_change_without_version_bump_does_not_publish(self):
        self.contracts_version("1.0.0")
        base = self.commit()
        self.write("src/Contracts/Contracts.Messages/EventBus.Messages/Message.cs", "// additive change")
        self.commit()
        self.assert_shared_selection(self.select(base), False)

    def test_requested_head_version_wins_over_later_head_and_worktree_versions(self):
        self.contracts_version("1.0.0")
        base = self.commit()
        self.contracts_version("1.1.0-preview.1")
        head = self.commit()
        self.contracts_version("1.0.0")
        self.commit()
        self.contracts_version("9.0.0")
        self.assert_shared_selection(self.select(base, head), True)
        self.assertEqual({"services": [], "shared": False, "apphost": False,
                          "packages": False}, self.select(base))


@unittest.skipUnless(dotnet_available(), "dotnet SDK not available")
class IndependentPackageVersionTests(unittest.TestCase):
    def properties(self, project, *options):
        output = subprocess.run(
            ["dotnet", "msbuild", project, "-getProperty:PackageId,Version,PackageVersion", *options],
            cwd=REPO, capture_output=True, check=True,
        ).stdout
        return json.loads(output.decode("utf-8-sig"))["Properties"]

    def test_contracts_metadata_matches_its_own_version_source(self):
        version = ET.parse(REPO / VERSIONS).findtext(".//ContractsMessagesVersion")
        self.assertEqual({
            "PackageId": "BookingModularMonolith.Contracts.Messages",
            "Version": version, "PackageVersion": version,
        }, self.properties(CONTRACTS_PROJECT))

    def test_buildingblocks_override_cannot_reversion_contracts(self):
        expected = ET.parse(REPO / VERSIONS).findtext(".//ContractsMessagesVersion")
        properties = self.properties(CONTRACTS_PROJECT, "-p:SharedPackageVersion=9.8.7-ci.42")
        self.assertEqual(expected, properties["Version"])
        self.assertEqual(expected, properties["PackageVersion"])

    def test_both_packages_keep_their_own_explicit_version_overrides(self):
        options = ("-p:SharedPackageVersion=9.8.7-ci.42", "-p:ContractsMessagesVersion=2.0.0-preview.3")
        for project, expected in (
            (CONTRACTS_PROJECT, "2.0.0-preview.3"),
            ("src/BuildingBlocks/Core/BuildingBlocks.Core.csproj", "9.8.7-ci.42"),
        ):
            with self.subTest(project=project):
                properties = self.properties(project, *options)
                self.assertEqual(expected, properties["Version"])
                self.assertEqual(expected, properties["PackageVersion"])


@unittest.skipUnless(BASH, "bash >= 4 not available")
class ContractsFirstPackTests(unittest.TestCase):
    def run_pack(self, fail_contracts=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binary = root / "dotnet"
            binary.write_text(
                f"#!{sys.executable}\n"
                "import json, os, sys\n"
                "with open(os.environ['DOTNET_LOG'], 'a') as log:\n"
                "    log.write(json.dumps(sys.argv[1:]) + '\\n')\n"
                "if os.environ['FAIL_CONTRACTS'] == 'true' and 'Contracts.Messages.csproj' in sys.argv[2]:\n"
                "    sys.exit(23)\n"
            )
            binary.chmod(0o755)
            log = root / "pack.jsonl"
            feed = root / "feed with spaces"
            environment = os.environ.copy()
            environment.update(PATH=str(root) + os.pathsep + environment["PATH"],
                               DOTNET_LOG=str(log), FAIL_CONTRACTS=str(fail_contracts).lower())
            completed = subprocess.run(
                [BASH, str(REPO / "scripts/pack-building-blocks.sh"), "2.0.0-ci.7", str(feed)],
                cwd=REPO, env=environment, capture_output=True, text=True,
            )
            return completed, [json.loads(line) for line in log.read_text().splitlines()], str(feed)

    def test_contracts_pack_first_in_same_feed_without_buildingblocks_override(self):
        completed, invocations, feed = self.run_pack()
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(str(REPO / CONTRACTS_PROJECT), invocations[0][1])
        self.assertFalse(any(argument.startswith("-p:") for argument in invocations[0]))
        self.assertGreater(len(invocations), 1)
        for invocation in invocations:
            self.assertEqual(feed, invocation[invocation.index("-o") + 1])
        for invocation in invocations[1:]:
            self.assertIn("-p:SharedPackageVersion=2.0.0-ci.7", invocation)

    def test_contracts_pack_failure_stops_before_buildingblocks(self):
        completed, invocations, _ = self.run_pack(fail_contracts=True)
        self.assertEqual(23, completed.returncode)
        self.assertEqual(1, len(invocations))
        self.assertEqual(str(REPO / CONTRACTS_PROJECT), invocations[0][1])


if __name__ == "__main__":
    unittest.main()
