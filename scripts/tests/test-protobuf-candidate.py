"""Exercise candidate-build failure handling with real child processes and files."""

import hashlib
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location(
    "protobuf_candidate", Path(__file__).parents[1] / "build-protobuf-candidate.py"
)
CANDIDATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CANDIDATE)


class CandidateBuildTests(unittest.TestCase):
    def test_digest_measures_exact_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "input"
            path.write_bytes(b"\x00\xff\r\n")
            self.assertEqual(
                hashlib.sha256(path.read_bytes()).hexdigest(), CANDIDATE.digest(path)
            )

    def test_target_selection_preserves_other_project_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "project.csproj"
            project.write_text(
                "<Project><TargetFrameworks>net462;netstandard2.0;netstandard2.1;net8.0</TargetFrameworks>"
                "<Property>keep me</Property></Project>"
            )
            CANDIDATE.select_target(project)
            self.assertEqual(
                "<Project><TargetFrameworks>netstandard2.1</TargetFrameworks>"
                "<Property>keep me</Property></Project>",
                project.read_text(),
            )

    def test_unexpected_or_duplicate_target_selection_preserves_input(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "project.csproj"
            declaration = "<TargetFrameworks>net462;netstandard2.0;netstandard2.1;net8.0</TargetFrameworks>"
            for contents in (
                "",
                "<TargetFramework>netstandard2.0</TargetFramework>",
                declaration * 2,
            ):
                with self.subTest(contents=contents):
                    project.write_text(contents)
                    with self.assertRaisesRegex(
                        ValueError, "Unexpected upstream target"
                    ):
                        CANDIDATE.select_target(project)
                    self.assertEqual(contents, project.read_text())

    def test_existing_output_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            sentinel = directory / "prior-evidence"
            sentinel.write_bytes(b"retain")
            with self.assertRaisesRegex(ValueError, "new directory"):
                CANDIDATE.build(directory, sys.executable, "unused")
            self.assertEqual(b"retain", sentinel.read_bytes())
            self.assertEqual([sentinel], list(directory.iterdir()))

    def test_visible_package_output_is_refused_before_creation(self):
        output = CANDIDATE.ROOT / "candidate-build-must-not-exist"
        with self.assertRaisesRegex(ValueError, "hidden from Unity"):
            CANDIDATE.build(output, sys.executable, "unused")
        self.assertFalse(output.exists())

    def test_missing_dotnet_is_refused_before_creation(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "candidate"
            with self.assertRaisesRegex(ValueError, "Cannot find .NET"):
                CANDIDATE.build(
                    output, str(Path(temporary) / "missing-dotnet"), "unused"
                )
            self.assertFalse(output.exists())

    def test_success_retains_natural_exit_and_merged_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            output = CANDIDATE.run(
                [
                    sys.executable,
                    "-c",
                    "import sys; print('stdout'); print('stderr', file=sys.stderr)",
                ],
                directory,
                log,
            )
            self.assertIn("stdout", output)
            self.assertIn("stderr", output)
            self.assertEqual(output, log.read_text().strip())
            receipt = json.loads(log.with_suffix(".process.json").read_text())
            self.assertEqual(0, receipt["exitCode"])
            self.assertFalse(receipt["timedOut"])

    def test_nonzero_exit_retains_original_output_and_status(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            with self.assertRaisesRegex(RuntimeError, "Command failed"):
                CANDIDATE.run(
                    [
                        sys.executable,
                        "-c",
                        "print('retained failure'); raise SystemExit(17)",
                    ],
                    directory,
                    log,
                )
            self.assertIn("retained failure", log.read_text())
            receipt = json.loads(log.with_suffix(".process.json").read_text())
            self.assertEqual(17, receipt["exitCode"])
            self.assertFalse(receipt["timedOut"])

    def test_timeout_retains_partial_output_and_refuses_success(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            with self.assertRaisesRegex(RuntimeError, "timedOut=True"):
                CANDIDATE.run(
                    [
                        sys.executable,
                        "-u",
                        "-c",
                        "import time; print('before timeout'); time.sleep(60)",
                    ],
                    directory,
                    log,
                    timeout_seconds=0.5,
                )
            self.assertIn("before timeout", log.read_text())
            receipt = json.loads(log.with_suffix(".process.json").read_text())
            self.assertTrue(receipt["timedOut"])
            self.assertNotEqual(0, receipt["exitCode"])

    @unittest.skipUnless(sys.platform == "linux", "Linux process-group cleanup control")
    def test_timeout_terminates_descendant_that_inherits_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            child = "import time; time.sleep(60)"
            parent = (
                "import subprocess, sys; "
                f"child = subprocess.Popen([sys.executable, '-c', {child!r}]); "
                "print(child.pid, flush=True); child.wait()"
            )
            with self.assertRaisesRegex(RuntimeError, "timedOut=True"):
                CANDIDATE.run(
                    [sys.executable, "-u", "-c", parent],
                    directory,
                    log,
                    timeout_seconds=0.5,
                )
            child_pid = int(log.read_text().strip())
            status = Path(f"/proc/{child_pid}/status")
            if status.exists():
                self.assertIn("State:\tZ", status.read_text())

    def test_missing_executable_fails_without_a_success_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            with self.assertRaises(OSError):
                CANDIDATE.run([str(directory / "missing")], directory, log)
            self.assertFalse(log.with_suffix(".process.json").exists())

    def test_inventory_ignores_inherited_repository_routing(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            logs = directory / "logs"
            logs.mkdir()
            CANDIDATE.run(["git", "init"], source, logs / "init.log")
            subject = source / "input.cs"
            subject.write_bytes(b"source bytes\r\n")
            with patch.dict(
                os.environ,
                {
                    "GIT_DIR": str(directory / "wrong-repository"),
                    "GIT_INDEX_FILE": str(directory / "must-not-create-index"),
                    "GIT_CONFIG_COUNT": "1",
                    "GIT_CONFIG_KEY_0": "core.worktree",
                    "GIT_CONFIG_VALUE_0": str(directory / "wrong-worktree"),
                },
            ):
                records = CANDIDATE.inventory(source, logs, "control")
            self.assertEqual(
                [{"path": "input.cs", "sha256": CANDIDATE.digest(subject)}], records
            )
            self.assertFalse((directory / "must-not-create-index").exists())
            self.assertFalse((directory / "wrong-repository").exists())

    def test_empty_inventory_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            CANDIDATE.run(["git", "init"], source, directory / "init.log")
            with self.assertRaisesRegex(ValueError, "inventory is empty"):
                CANDIDATE.inventory(source, directory, "empty")

    def test_inventory_retains_leading_spaces_in_file_names(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            CANDIDATE.run(["git", "init"], source, directory / "init.log")
            subject = source / " input.cs"
            subject.write_bytes(b"exact bytes")
            records = CANDIDATE.inventory(source, directory, "spaces")
            self.assertEqual(
                [{"path": " input.cs", "sha256": CANDIDATE.digest(subject)}], records
            )


if __name__ == "__main__":
    unittest.main()
