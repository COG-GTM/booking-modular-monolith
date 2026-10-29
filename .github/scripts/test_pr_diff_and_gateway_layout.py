import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

import ci


REPO = Path(ci.__file__).resolve().parents[2]
SCRIPT = Path(ci.__file__).resolve()


def evaluate_expression(expression, context):
    for operand in expression.split("||"):
        value = context
        parts = operand.strip().split(".")
        if parts[0] == "github":
            parts = parts[1:]
        for part in parts:
            if isinstance(value, dict) and part in value:
                value = value[part]
            else:
                value = ""
                break
        if value:
            return value
    return ""


def read_selection(completed):
    return {
        key: json.loads(value)
        for key, value in (line.split("=", 1) for line in completed.stdout.splitlines())
    }


def write_file(root, path, content):
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content)


def git(root, *args):
    return subprocess.check_output(["git", *args], cwd=root, text=True).strip()


def commit(root, message):
    subprocess.run([
        "git", "-c", "user.name=ci", "-c", "user.email=ci@example.com",
        "-c", "commit.gpgsign=false", "commit", "-m", message,
    ], cwd=root, check=True, capture_output=True, text=True)
    return git(root, "rev-parse", "HEAD")


class WorkflowRevisionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        workflow = (REPO / ".github/workflows/ci.yml").read_text()
        before = re.search(r"^\s+BEFORE: \$\{\{ (.+?) \}\}\s*$", workflow, re.MULTILINE)
        after = re.search(r"^\s+AFTER: \$\{\{ (.+?) \}\}\s*$", workflow, re.MULTILINE)
        assert before is not None, "BEFORE expression was not found"
        assert after is not None, "AFTER expression was not found"
        cls.before_expression = before.group(1)
        cls.after_expression = after.group(1)

    def test_workflow_revision_expressions_for_each_event(self):
        contexts = (
            (
                "pull_request synchronize",
                {
                    "event": {
                        "before": "last-push",
                        "pull_request": {
                            "base": {"sha": "base"},
                            "head": {"sha": "head"},
                        },
                    },
                    "sha": "merge",
                },
                "base",
                "head",
            ),
            (
                "push",
                {"event": {"before": "previous"}, "sha": "pushed"},
                "previous",
                "pushed",
            ),
            (
                "workflow_dispatch",
                {"event": {}, "sha": "dispatched"},
                "",
                "dispatched",
            ),
        )
        for name, context, expected_before, expected_after in contexts:
            with self.subTest(event=name):
                self.assertEqual(
                    expected_before,
                    evaluate_expression(self.before_expression, context),
                )
                self.assertEqual(
                    expected_after,
                    evaluate_expression(self.after_expression, context),
                )

    def test_synchronize_selection_covers_the_full_pull_request_diff(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(["git", "init"], cwd=root, check=True, capture_output=True, text=True)
            write_file(
                root,
                "Directory.Build.props",
                "<Project><PropertyGroup><SharedPackageVersion>1.0.0"
                "</SharedPackageVersion></PropertyGroup></Project>",
            )
            write_file(root, "src/Gateway/src/Gateway.csproj", "<Project />")
            write_file(root, "src/Gateway/Dockerfile", "FROM scratch\n")
            write_file(root, "src/Modules/Flight/src/A.cs", "class A {}\n")
            subprocess.run([
                "git", "add", "Directory.Build.props", "src/Gateway/src/Gateway.csproj",
                "src/Gateway/Dockerfile", "src/Modules/Flight/src/A.cs",
            ], cwd=root, check=True, capture_output=True, text=True)
            base = commit(root, "base")

            write_file(root, "src/Modules/Flight/src/A.cs", "class A { int value; }\n")
            subprocess.run(
                ["git", "add", "src/Modules/Flight/src/A.cs"],
                cwd=root, check=True, capture_output=True, text=True,
            )
            first_commit = commit(root, "flight change")

            write_file(root, "src/Gateway/src/Program.cs", "class Program {}\n")
            subprocess.run(
                ["git", "add", "src/Gateway/src/Program.cs"],
                cwd=root, check=True, capture_output=True, text=True,
            )
            second_commit = commit(root, "gateway change")

            context = {
                "event": {
                    "before": first_commit,
                    "pull_request": {
                        "base": {"sha": base},
                        "head": {"sha": second_commit},
                    },
                },
                "sha": "merge",
            }
            before = evaluate_expression(self.before_expression, context)
            after = evaluate_expression(self.after_expression, context)
            command = [
                sys.executable, str(SCRIPT), "select", "--base", before,
                "--head", after, "--force", "false",
            ]

            completed = subprocess.run(
                command, cwd=root, check=True, capture_output=True, text=True,
            )
            selection = read_selection(completed)
            self.assertEqual(["flight", "gateway"], selection["services"])
            self.assertFalse(selection["packages"])
            self.assertFalse(selection["shared"])

            old_precedence = subprocess.run(
                [
                    sys.executable, str(SCRIPT), "select", "--base", first_commit,
                    "--head", after, "--force", "false",
                ],
                cwd=root, check=True, capture_output=True, text=True,
            )
            self.assertEqual(["gateway"], read_selection(old_precedence)["services"])


class GatewayLayoutTests(unittest.TestCase):
    def test_real_gateway_layout_and_cli_output(self):
        expected = {
            "project": "src/Gateway/src/Gateway.csproj",
            "tests": "src/Gateway/tests",
            "dockerfile": "src/Gateway/Dockerfile",
        }
        self.assertEqual(expected, ci.layout("gateway", REPO))
        for path in expected.values():
            self.assertTrue((REPO / path).exists(), path)

        completed = subprocess.run(
            [sys.executable, str(SCRIPT), "layout", "gateway"],
            cwd=REPO, check=True, capture_output=True, text=True,
        )
        self.assertEqual(
            [
                "project=src/Gateway/src/Gateway.csproj",
                "tests=src/Gateway/tests",
                "dockerfile=src/Gateway/Dockerfile",
            ],
            completed.stdout.splitlines(),
        )

    def test_gateway_test_project_is_not_mistaken_for_the_host_project(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            write_file(root, "src/Gateway/tests/Gateway.Test/Gateway.Test.csproj", "<Project />")
            write_file(root, "src/Gateway/Dockerfile", "FROM scratch\n")
            self.assertIsNone(ci.layout("gateway", root))

    def test_nested_service_host_requires_its_root_dockerfile(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            write_file(
                root,
                "src/Services/Booking.Host/src/Booking.Host.csproj",
                "<Project />",
            )
            with self.assertRaises(FileNotFoundError):
                ci.layout("booking", root)


if __name__ == "__main__":
    unittest.main()
