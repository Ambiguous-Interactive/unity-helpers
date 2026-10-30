"""Exercise candidate-build failure handling with real child processes and files."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Protocol, cast
from unittest.mock import patch

import pytest


class CandidateModule(Protocol):
    ROOT: Path
    SOURCE_COMMIT: str
    SOURCE_URL: str
    SDK_VERSION: str

    def digest(self, path: Path) -> str: ...
    def select_target(self, project: Path) -> None: ...
    def build(self, output: Path, dotnet: str, upstream: str) -> object: ...
    def run(
        self,
        arguments: list[str],
        directory: Path,
        log: Path,
        timeout_seconds: float = 900,
    ) -> str: ...
    def inventory(self, source: Path, output: Path, phase: str) -> list[dict[str, str]]: ...
    def canonicalize_origin(self, source: Path, output: Path) -> str: ...


class ShippedModule(Protocol):
    def digest(self, path: Path) -> str: ...
    def verify(self, candidate: Path, shipped: Path, patch: Path) -> None: ...


def load_module(name: str, filename: str) -> object:
    specification = importlib.util.spec_from_file_location(
        name, Path(__file__).parents[1] / filename
    )
    if specification is None or specification.loader is None:
        raise ImportError(filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


CANDIDATE = cast(
    "CandidateModule", load_module("protobuf_candidate", "build-protobuf-candidate.py")
)
SHIPPED = cast("ShippedModule", load_module("protobuf_shipped", "verify-protobuf-shipped.py"))


class CandidateBuildTests(unittest.TestCase):
    def test_digest_measures_exact_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "input"
            path.write_bytes(b"\x00\xff\r\n")
            assert hashlib.sha256(path.read_bytes()).hexdigest() == CANDIDATE.digest(path)

    def test_target_selection_preserves_other_project_content(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "project.csproj"
            project.write_text(
                "<Project><TargetFrameworks>net462;netstandard2.0;netstandard2.1;net8.0</TargetFrameworks>"
                "<Property>keep me</Property></Project>"
            )
            CANDIDATE.select_target(project)
            assert (
                project.read_text()
                == "<Project><TargetFrameworks>netstandard2.1</TargetFrameworks>"
                "<Property>keep me</Property></Project>"
            )

    def test_unexpected_or_duplicate_target_selection_preserves_input(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "project.csproj"
            declaration = (
                "<TargetFrameworks>net462;netstandard2.0;netstandard2.1;net8.0</TargetFrameworks>"
            )
            for contents in (
                "",
                "<TargetFramework>netstandard2.0</TargetFramework>",
                declaration * 2,
            ):
                with self.subTest(contents=contents):
                    project.write_text(contents)
                    with pytest.raises(ValueError, match="Unexpected upstream target"):
                        CANDIDATE.select_target(project)
                    assert contents == project.read_text()

    def test_existing_output_is_never_overwritten(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            sentinel = directory / "prior-evidence"
            sentinel.write_bytes(b"retain")
            with pytest.raises(ValueError, match="new directory"):
                CANDIDATE.build(directory, sys.executable, "unused")
            assert sentinel.read_bytes() == b"retain"
            assert [sentinel] == list(directory.iterdir())

    def test_visible_package_output_is_refused_before_creation(self) -> None:
        output = CANDIDATE.ROOT / "candidate-build-must-not-exist"
        with pytest.raises(ValueError, match="hidden from Unity"):
            CANDIDATE.build(output, sys.executable, "unused")
        assert not output.exists()

    def test_missing_dotnet_is_refused_before_creation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "candidate"
            with pytest.raises(ValueError, match=r"Cannot find \.NET"):
                CANDIDATE.build(output, str(Path(temporary) / "missing-dotnet"), "unused")
            assert not output.exists()

    def test_success_retains_natural_exit_and_merged_output(self) -> None:
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
            assert "stdout" in output
            assert "stderr" in output
            assert output == log.read_text().strip()
            receipt = json.loads(log.with_suffix(".process.json").read_text())
            assert receipt["exitCode"] == 0
            assert not receipt["timedOut"]

    def test_nonzero_exit_retains_original_output_and_status(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            with pytest.raises(RuntimeError, match="Command failed"):
                CANDIDATE.run(
                    [
                        sys.executable,
                        "-c",
                        "print('retained failure'); raise SystemExit(17)",
                    ],
                    directory,
                    log,
                )
            assert "retained failure" in log.read_text()
            receipt = json.loads(log.with_suffix(".process.json").read_text())
            assert receipt["exitCode"] == 17
            assert not receipt["timedOut"]

    def test_timeout_retains_partial_output_and_refuses_success(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            with pytest.raises(RuntimeError, match="timedOut=True"):
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
            assert "before timeout" in log.read_text()
            receipt = json.loads(log.with_suffix(".process.json").read_text())
            assert receipt["timedOut"]
            assert receipt["exitCode"] != 0

    @unittest.skipUnless(sys.platform == "linux", "Linux process-group cleanup control")
    def test_timeout_terminates_descendant_that_inherits_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            child = "import time; time.sleep(60)"
            parent = (
                "import subprocess, sys; "
                f"child = subprocess.Popen([sys.executable, '-c', {child!r}]); "
                "print(child.pid, flush=True); child.wait()"
            )
            with pytest.raises(RuntimeError, match="timedOut=True"):
                CANDIDATE.run(
                    [sys.executable, "-u", "-c", parent],
                    directory,
                    log,
                    timeout_seconds=0.5,
                )
            child_pid = int(log.read_text().strip())
            status = Path(f"/proc/{child_pid}/status")
            if status.exists():
                assert "State:\tZ" in status.read_text()

    def test_missing_executable_fails_without_a_success_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            log = directory / "process.log"
            with pytest.raises(FileNotFoundError):
                CANDIDATE.run([str(directory / "missing")], directory, log)
            assert not log.with_suffix(".process.json").exists()

    def test_inventory_ignores_inherited_repository_routing(self) -> None:
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
            assert [{"path": "input.cs", "sha256": CANDIDATE.digest(subject)}] == records
            assert not (directory / "must-not-create-index").exists()
            assert not (directory / "wrong-repository").exists()

    def test_empty_inventory_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            CANDIDATE.run(["git", "init"], source, directory / "init.log")
            with pytest.raises(ValueError, match="inventory is empty"):
                CANDIDATE.inventory(source, directory, "empty")

    def test_offline_source_origin_is_canonical_before_building(self) -> None:
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
            assert actual == "https://github.com/protobuf-net/protobuf-net.git"
            observed = CANDIDATE.run(
                ["git", "config", "--get", "remote.origin.url"],
                source,
                directory / "observed.log",
            )
            assert actual == observed

    def test_canonical_origin_ignores_global_transport_rewrites(self) -> None:
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
                assert rewritten == "ssh://git@github.com/protobuf-net/protobuf-net.git"
                actual = CANDIDATE.canonicalize_origin(source, directory)
                assert actual == "https://github.com/protobuf-net/protobuf-net.git"

    def test_inventory_retains_leading_spaces_in_file_names(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            source.mkdir()
            CANDIDATE.run(["git", "init"], source, directory / "init.log")
            subject = source / " input.cs"
            subject.write_bytes(b"exact bytes")
            records = CANDIDATE.inventory(source, directory, "spaces")
            assert [{"path": " input.cs", "sha256": CANDIDATE.digest(subject)}] == records


class ShippedIdentityTests(unittest.TestCase):
    def fixture(self, directory: Path) -> tuple[Path, Path, Path, dict[str, object]]:
        candidate = directory / "candidate"
        shipped = directory / "shipped"
        candidate.mkdir()
        shipped.mkdir()
        source_patch = directory / "source.patch"
        source_patch.write_bytes(b"source patch\r\n")
        (shipped / "il2cpp-aot.patch").write_bytes(source_patch.read_bytes())
        files: dict[str, str] = {}
        for name in ("protobuf-net.dll", "protobuf-net.Core.dll"):
            contents = name.encode("utf-8")
            (candidate / name).write_bytes(contents)
            (shipped / name).write_bytes(contents)
            files[name] = hashlib.sha256(contents).hexdigest()
        receipt: dict[str, object] = {
            "sourceCommit": CANDIDATE.SOURCE_COMMIT,
            "sourceUrl": CANDIDATE.SOURCE_URL,
            "sdk": CANDIDATE.SDK_VERSION,
            "targetFramework": "netstandard2.1",
            "files": files,
            "patchSha256": SHIPPED.digest(source_patch),
        }
        (candidate / "candidate-receipt.json").write_text(json.dumps(receipt))
        return candidate, shipped, source_patch, receipt

    def test_exact_shipped_payloads_are_accepted(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            candidate, shipped, source_patch, _ = self.fixture(Path(temporary))
            SHIPPED.verify(candidate, shipped, source_patch)

    def test_each_changed_or_missing_payload_is_rejected(self) -> None:
        for side in ("candidate", "shipped"):
            for name in ("protobuf-net.dll", "protobuf-net.Core.dll"):
                for missing in (False, True):
                    with (
                        self.subTest(side=side, name=name, missing=missing),
                        tempfile.TemporaryDirectory() as temporary,
                    ):
                        candidate, shipped, source_patch, _ = self.fixture(Path(temporary))
                        target = (candidate if side == "candidate" else shipped) / name
                        if missing:
                            target.unlink()
                        else:
                            target.write_bytes(target.read_bytes() + b"tamper")
                        with pytest.raises((ValueError, OSError)):
                            SHIPPED.verify(candidate, shipped, source_patch)

    def test_each_changed_provenance_field_is_rejected(self) -> None:
        for field in (
            "sourceCommit",
            "sourceUrl",
            "sdk",
            "targetFramework",
            "patchSha256",
        ):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                candidate, shipped, source_patch, receipt = self.fixture(Path(temporary))
                receipt[field] = "changed"
                (candidate / "candidate-receipt.json").write_text(json.dumps(receipt))
                with pytest.raises(ValueError, match=r"Candidate|Shipped AOT patch"):
                    SHIPPED.verify(candidate, shipped, source_patch)

    def test_invalid_receipt_shapes_are_rejected(self) -> None:
        cases: list[object] = [None, [], "receipt", 4]
        for value in cases:
            with self.subTest(value=value), tempfile.TemporaryDirectory() as temporary:
                candidate, shipped, source_patch, _ = self.fixture(Path(temporary))
                (candidate / "candidate-receipt.json").write_text(json.dumps(value))
                with pytest.raises(ValueError, match="Candidate receipt must be an object"):
                    SHIPPED.verify(candidate, shipped, source_patch)

    def test_invalid_hash_tables_are_rejected(self) -> None:
        cases: list[object] = [None, [], True, {}, {"protobuf-net.dll": 4}]
        for value in cases:
            with self.subTest(value=value), tempfile.TemporaryDirectory() as temporary:
                candidate, shipped, source_patch, receipt = self.fixture(Path(temporary))
                receipt["files"] = value
                (candidate / "candidate-receipt.json").write_text(json.dumps(receipt))
                with pytest.raises(ValueError, match="Candidate"):
                    SHIPPED.verify(candidate, shipped, source_patch)

    def test_changed_shipped_or_source_patch_is_rejected(self) -> None:
        for source in (False, True):
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temporary:
                candidate, shipped, source_patch, _ = self.fixture(Path(temporary))
                target = source_patch if source else shipped / "il2cpp-aot.patch"
                target.write_bytes(target.read_bytes() + b"tamper")
                with pytest.raises(ValueError, match="Shipped AOT patch"):
                    SHIPPED.verify(candidate, shipped, source_patch)


if __name__ == "__main__":
    unittest.main()
