import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import textwrap
import unittest


ACTION = Path(__file__).resolve().parents[1] / "actions/test/action.yml"


class TestActionTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.tests = self.root / "test projects"
        self.tests.mkdir()
        self.log = self.root / "calls.jsonl"
        executable = self.root / "dotnet"
        executable.write_text(f"#!{sys.executable}\n" + textwrap.dedent("""\
            import json
            import os
            from pathlib import Path
            import sys

            with open(os.environ["DOTNET_TEST_LOG"], "a") as log:
                log.write(json.dumps(sys.argv[1:]) + "\\n")
            sys.exit(1 if Path(sys.argv[2]).name == "B Fail.csproj" else 0)
            """))
        executable.chmod(0o755)

    def project(self, name):
        project = self.tests / name
        project.parent.mkdir(parents=True, exist_ok=True)
        project.touch()
        return str(project)

    def run_action(self, no_restore="false"):
        # Execute the actual YAML block with GitHub's explicit bash error flags.
        block = ACTION.read_text().split("      run: |\n", 1)[1].split("    - name:", 1)[0]
        result = subprocess.run(
            ["bash", "--noprofile", "--norc", "-e", "-o", "pipefail", "-c", textwrap.dedent(block)],
            env={**os.environ, "PATH": f"{self.root}{os.pathsep}{os.environ['PATH']}",
                 "TESTS": str(self.tests), "NO_RESTORE": no_restore, "DOTNET_TEST_LOG": str(self.log)},
            capture_output=True, text=True,
        )
        calls = [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []
        return result, calls

    def test_all_projects_run_with_spaces_and_restore_enabled_by_default(self):
        last = self.project("Z Unit/Unit.Test.csproj")
        first = self.project("A Integration/Integration.Test.csproj")
        result, calls = self.run_action()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual([
            ["test", project, "-c", "Release", "--logger", "trx"] for project in (first, last)
        ], calls)

    def test_failure_is_propagated_after_remaining_projects_run(self):
        projects = [self.project(name) for name in ("A Pass.csproj", "B Fail.csproj", "C Pass.csproj")]
        result, calls = self.run_action()
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(projects, [args[1] for args in calls])

    def test_empty_test_directory_fails_instead_of_succeeding_without_tests(self):
        result, calls = self.run_action()
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], calls)

    def test_no_restore_is_forwarded_only_when_explicitly_enabled(self):
        project = self.project("Unit.Test.csproj")
        result, calls = self.run_action(no_restore="true")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual([["test", project, "-c", "Release", "--no-restore", "--logger", "trx"]], calls)


if __name__ == "__main__":
    unittest.main()
