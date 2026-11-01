"""Controls for the browser consumer's acceptance boundaries."""

import json
import os
import signal
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import browser_consumer as consumer
import pytest


class BrowserConsumerTests(unittest.TestCase):
    def test_exact_case_inventory(self) -> None:
        cases = consumer.cases()
        assert len(cases) == 23
        assert len(set(cases)) == 23
        assert ("safety", "populated") in cases
        assert ("serialize-read", "populated") in cases
        assert ("serialize-read", "null") in cases
        assert ("merge", "populated") in cases
        assert ("presence-foreign", "presence") in cases
        assert sum(mode.startswith("presence-") for mode, _ in cases) == 6

    def test_generated_profile_preserves_legacy_tags_and_removes_suppression(self) -> None:
        source = "#pragma warning disable WPROTO030\n[ProtoContract] class Doc { [ProtoMember(7)] public int Value; }\n#pragma warning restore WPROTO030\n"
        generated = consumer.generated_contract(source, [7])
        assert "[ProtoContract, global::" in generated
        assert "WProtoContract]" in generated
        assert "partial class Doc" in generated
        assert "[ProtoMember(7), global::" in generated
        assert "WProtoMember(7)]" in generated
        assert "#pragma" not in generated

    def test_changed_contract_shape_requires_review(self) -> None:
        for source in (
            "",
            "[ProtoContract] class Doc {}",
            "[ProtoContract] class Doc { [ProtoMember(8)] int Value; }",
        ):
            with (
                self.subTest(source=source),
                pytest.raises(ValueError, match="Consumer contract shape changed"),
            ):
                consumer.generated_contract(source, [7])

    def test_all_four_real_contracts_receive_generated_annotations(self) -> None:
        template = (
            Path(__file__).resolve().parents[2]
            / "Tests/Acceptance~/NullableMapConsumer/Assets/Consumer"
        )
        declarations = {
            "Doc": [1, 7, 11, 17, 23],
            "OwnedMessage": [1, 3, 5],
            "PresenceValue": [1],
            "PresenceDoc": list(range(1, 25)),
        }
        for name, tags in declarations.items():
            with self.subTest(contract=name):
                generated = consumer.generated_contract((template / f"{name}.cs").read_text(), tags)
                assert len(tags) == generated.count("WProtoMember(")
                assert generated.count("WProtoContract]") == 1

    def test_browser_startup_failure_gets_current_failed_status(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = consumer.new_browser_run(root)
            consumer.record_browser_status(root, first, "passed")
            with pytest.raises((ModuleNotFoundError, FileNotFoundError)):
                consumer.run_browser(root)
            latest = json.loads((root / "latest-run.json").read_text())
            assert latest["status"] == "failed"
            assert first.relative_to(root).as_posix() != latest["run"]

    def test_result_identity_is_exact(self) -> None:
        expected = "Passed|serialize-read|populated|6000.4.6f1|source=correct"
        consumer.validate_result(expected, expected, [])
        for actual in (
            "",
            expected + "\n",
            expected.replace("correct", "stale"),
            "Failed|failure",
        ):
            with self.subTest(actual=actual), pytest.raises(RuntimeError):
                consumer.validate_result(actual, expected, [])

    def test_browser_errors_cannot_pass_with_success_receipt(self) -> None:
        with pytest.raises(RuntimeError):
            consumer.validate_result("Passed", "Passed", ["abort at runtime"])

    def test_inventory_rejects_empty_subjects(self) -> None:
        with (
            tempfile.TemporaryDirectory() as temporary,
            pytest.raises(RuntimeError),
        ):
            consumer.require_inventory(Path(temporary), {})

    def test_inventory_detects_changed_added_and_missing_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "player.wasm"
            source.write_bytes(b"original")
            expected = consumer.inventory(root)
            consumer.require_inventory(root, expected)
            source.write_bytes(b"changed")
            with pytest.raises(RuntimeError):
                consumer.require_inventory(root, expected)
            source.write_bytes(b"original")
            added = root / "extra.data"
            added.write_bytes(b"extra")
            with pytest.raises(RuntimeError):
                consumer.require_inventory(root, expected)
            added.unlink()
            source.unlink()
            with pytest.raises(RuntimeError):
                consumer.require_inventory(root, expected)

    def test_existing_artifacts_are_never_overwritten(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            sentinel = root / "kept.txt"
            sentinel.write_text("prior evidence")
            with pytest.raises(ValueError, match="Artifacts directory must be fresh"):
                consumer.prepare(root, root, root / "editor", "6000.4.6f1")
            assert sentinel.read_text() == "prior evidence"

    def test_editor_nonzero_exit_is_recorded_and_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            process = MagicMock()
            process.__enter__.return_value = process
            process.wait.return_value = 5
            process.poll.return_value = 5
            process.pid = 42
            with (
                patch.object(subprocess, "Popen", return_value=process),
                pytest.raises(RuntimeError),
            ):
                consumer.run_editor(
                    root / "Unity", root, "Build", root / "build.log", root / "player"
                )
            record = json.loads((root / "build.process.json").read_text())
            assert record["exitCode"] == 5
            assert not record["interrupted"]

    def test_editor_watchdog_retains_interruption_and_reaps_process(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            process = MagicMock()
            process.__enter__.return_value = process
            process.wait.side_effect = [subprocess.TimeoutExpired("Unity", 1), 0]
            process.poll.side_effect = [None, 0]
            process.pid = 42
            with (
                patch.object(subprocess, "Popen", return_value=process),
                patch.object(subprocess, "run"),
                patch.object(os, "killpg", create=True) as kill_group,
                pytest.raises(subprocess.TimeoutExpired),
            ):
                consumer.run_editor(
                    root / "Unity", root, "Build", root / "build.log", root / "player"
                )
            assert process.wait.call_count == 2
            if sys.platform != "win32":
                kill_group.assert_called_once_with(42, signal.SIGKILL)
            record = json.loads((root / "build.process.json").read_text())
            assert record["interrupted"]
            assert record["exitCode"] == 0

    def test_rerun_preserves_earlier_receipts_and_replaces_latest_status(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = consumer.new_browser_run(root)
            (first / "result.json").write_text("original failure evidence")
            consumer.record_browser_status(root, first, "passed")
            second = consumer.new_browser_run(root)
            consumer.record_browser_status(root, second, "failed", "browser timeout")
            assert first != second
            assert (first / "result.json").read_text() == "original failure evidence"
            assert json.loads((first / "status.json").read_text())["status"] == "passed"
            latest = json.loads((root / "latest-run.json").read_text())
            assert latest["status"] == "failed"
            assert second.relative_to(root).as_posix() == latest["run"]


@pytest.mark.parametrize("value", [None, [], 1, {"key": 1}, {"key": None}, {1: "value"}])
def test_string_mapping_refuses_wrong_json_shapes(value: object) -> None:
    with pytest.raises(ValueError, match="string keys and values"):
        consumer.string_mapping(value)


def test_string_mapping_preserves_unicode_and_whitespace(tmp_path: Path) -> None:
    source = tmp_path / "manifest.json"
    source.write_text('{"key ü": " value 🦄 "}', encoding="utf-8")
    assert consumer.read_string_mapping(source) == {"key ü": " value 🦄 "}


def test_actual_process_failure_records_native_exit(tmp_path: Path) -> None:
    with pytest.raises(RuntimeError, match="Unity exited"):
        consumer.run_editor(
            Path(sys.executable), tmp_path, "Build", tmp_path / "build.log", tmp_path / "player"
        )
    record = json.loads((tmp_path / "build.process.json").read_text(encoding="utf-8"))
    assert record["exitCode"] != 0
    assert record["interrupted"] is False
    assert record["command"][0] == sys.executable


@pytest.mark.parametrize("legacy", [False, True])
def test_help_works_outside_the_checkout(tmp_path: Path, legacy: bool) -> None:
    name = "run-browser-consumer.py" if legacy else "browser_consumer.py"
    script = Path(__file__).resolve().parents[1] / "unity" / name
    result = subprocess.run(
        [sys.executable, str(script), "--help"],
        cwd=tmp_path,
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert "--browser-only" in result.stdout
    assert "--artifacts" in result.stdout


if __name__ == "__main__":
    unittest.main()
