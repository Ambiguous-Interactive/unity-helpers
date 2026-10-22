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

VERIFY_SPEC = importlib.util.spec_from_file_location(
    "protobuf_shipped", Path(__file__).parents[1] / "verify-protobuf-shipped.py"
)
SHIPPED = importlib.util.module_from_spec(VERIFY_SPEC)
VERIFY_SPEC.loader.exec_module(SHIPPED)


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

    def test_offline_source_origin_is_canonical_before_building(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            CANDIDATE.run(["git", "init"], source, directory / "init.log")
            CANDIDATE.run(
                ["git", "remote", "add", "origin", str(directory / "offline-cache")],
                source,
                directory / "remote.log",
            )
            actual = CANDIDATE.canonicalize_origin(source, directory)
            self.assertEqual("https://github.com/protobuf-net/protobuf-net.git", actual)
            observed = CANDIDATE.run(
                ["git", "config", "--get", "remote.origin.url"],
                source,
                directory / "observed.log",
            )
            self.assertEqual(actual, observed)

    def test_canonical_origin_ignores_global_transport_rewrites(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            config = directory / "global.gitconfig"
            config.write_text(
                '[url "ssh://git@github.com/"]\n    insteadOf = https://github.com/\n'
            )
            with patch.dict(os.environ, {"GIT_CONFIG_GLOBAL": str(config)}):
                CANDIDATE.run(["git", "init"], source, directory / "init.log")
                CANDIDATE.run(
                    ["git", "remote", "add", "origin", CANDIDATE.SOURCE_URL],
                    source,
                    directory / "remote.log",
                )
                rewritten = CANDIDATE.run(
                    ["git", "remote", "get-url", "origin"],
                    source,
                    directory / "rewritten.log",
                )
                self.assertEqual(
                    "ssh://git@github.com/protobuf-net/protobuf-net.git", rewritten
                )
                actual = CANDIDATE.canonicalize_origin(source, directory)
                self.assertEqual(
                    "https://github.com/protobuf-net/protobuf-net.git", actual
                )

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


class ShippedIdentityTests(unittest.TestCase):
    def fixture(self, directory):
        candidate = directory / "candidate"
        shipped = directory / "shipped"
        candidate.mkdir()
        shipped.mkdir()
        source_patch = directory / "source.patch"
        source_patch.write_bytes(b"source patch\r\n")
        (shipped / "il2cpp-aot.patch").write_bytes(source_patch.read_bytes())
        files = {}
        for name in ("protobuf-net.dll", "protobuf-net.Core.dll"):
            contents = name.encode("utf-8")
            (candidate / name).write_bytes(contents)
            (shipped / name).write_bytes(contents)
            files[name] = hashlib.sha256(contents).hexdigest()
        receipt = {
            "sourceCommit": CANDIDATE.SOURCE_COMMIT,
            "sourceUrl": CANDIDATE.SOURCE_URL,
            "sdk": CANDIDATE.SDK_VERSION,
            "targetFramework": "netstandard2.1",
            "files": files,
            "patchSha256": SHIPPED.digest(source_patch),
        }
        (candidate / "candidate-receipt.json").write_text(json.dumps(receipt))
        return candidate, shipped, source_patch, receipt

    def test_exact_shipped_payloads_are_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            candidate, shipped, source_patch, _ = self.fixture(Path(temporary))
            SHIPPED.verify(candidate, shipped, source_patch)

    def test_each_changed_or_missing_payload_is_rejected(self):
        for side in ("candidate", "shipped"):
            for name in ("protobuf-net.dll", "protobuf-net.Core.dll"):
                for missing in (False, True):
                    with self.subTest(side=side, name=name, missing=missing):
                        with tempfile.TemporaryDirectory() as temporary:
                            candidate, shipped, source_patch, _ = self.fixture(
                                Path(temporary)
                            )
                            target = (
                                candidate if side == "candidate" else shipped
                            ) / name
                            if missing:
                                target.unlink()
                            else:
                                target.write_bytes(target.read_bytes() + b"tamper")
                            with self.assertRaises((ValueError, OSError)):
                                SHIPPED.verify(candidate, shipped, source_patch)

    def test_each_changed_provenance_field_is_rejected(self):
        for field in (
            "sourceCommit",
            "sourceUrl",
            "sdk",
            "targetFramework",
            "patchSha256",
        ):
            with self.subTest(field=field):
                with tempfile.TemporaryDirectory() as temporary:
                    candidate, shipped, source_patch, receipt = self.fixture(
                        Path(temporary)
                    )
                    receipt[field] = "changed"
                    (candidate / "candidate-receipt.json").write_text(
                        json.dumps(receipt)
                    )
                    with self.assertRaises(ValueError):
                        SHIPPED.verify(candidate, shipped, source_patch)

    def test_changed_shipped_or_source_patch_is_rejected(self):
        for source in (False, True):
            with self.subTest(source=source):
                with tempfile.TemporaryDirectory() as temporary:
                    candidate, shipped, source_patch, _ = self.fixture(Path(temporary))
                    target = source_patch if source else shipped / "il2cpp-aot.patch"
                    target.write_bytes(target.read_bytes() + b"tamper")
                    with self.assertRaises(ValueError):
                        SHIPPED.verify(candidate, shipped, source_patch)


if __name__ == "__main__":
    unittest.main()
